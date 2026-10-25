using System;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Core.Download;
using MAH.AddressableManager.Core.Initialize;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MAH.AddressableManager
{
    /// <summary>
    /// 機能内クラス（Controller・Handler 等）の生成と寿命管理、Unity ライフサイクルの中継のみを行う。ロジックは持たない。
    /// 公開するのは Manager の取得（AccInstance）と Handler の取得（AccHandler）だけで、初期化・ダウンロード・読み込み・解放の操作はすべて IAddressableHandler から行う。
    /// Addressables は最初のロード時に自動で初期化されるため、読み込み・解放は InitializeAsync に依存しない（既定）。
    /// 初期化の開始方法（Awake で自動／Handler の InitializeAsync で手動）と、初期化前の Load の扱いは、Runtime SO の初期化設定で選ぶ。
    /// </summary>
    [DefaultExecutionOrder(-1000)] // 利用者の Awake より先に AccInstance を用意する
    public sealed class AddressableManager : MonoBehaviour
    {
        [SerializeField] private AddressableRuntimeSettingsSO _settings;

        private static AddressableManager _instance;
        private CatalogCacheBustingService _catalogCacheBusting; // URL キャッシュ対策が OFF のときは null
        private InitializeController _initializeController;
        private DownloadController _downloadController;
        private LoadingController _loadingController;
        private IAddressableHandler _handler;
#if UNITY_EDITOR
        private IAddressableDebugReader _debugReader;
#endif

        /// <summary> 実行中の Manager </summary>
        public static AddressableManager AccInstance => _instance;
        /// <summary> 初期化・読み込み・解放の窓口（利用者が触れる唯一の入口） </summary>
        public IAddressableHandler AccHandler => _handler;
#if UNITY_EDITOR
        // 2026-10-14 Editor のデバッグウィンドウが、実行中の状態を読み取り専用で見るための窓口（本番ビルドには含まれない）
        internal IAddressableDebugReader AccDebugReader => _debugReader;
#endif

        /// <summary> Unityの毎フレーム更新を各Controllerへ中継するイベント </summary>
        private event Action<float> OnLateUpdate;
        /// <summary> シーンのアンロードを各Controllerへ中継するイベント </summary>
        private event Action<Scene> OnSceneUnloaded;

        private void Awake()
        {
            // 2つ目以降の Manager は破棄する
            if (_instance != null)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            if (_settings == null)
            {
                AddressableLogger.LogWarning($"{nameof(AddressableRuntimeSettingsSO)} が未設定のため、既定値で動作します。Inspector で割り当ててください。");
                _settings = ScriptableObject.CreateInstance<AddressableRuntimeSettingsSO>();
            }
            AddressableLogger.AccLevel = _settings.AccLogLevel;

            // Controller には自分の機能の設定だけを渡す。Model は Manager で生成して注入する
            var initialize_settings = _settings.AccInitialize;

            // 初期化の方法に関わらず、先に設定する。手動モード＋PassThrough では、InitializeAsync より前の Load で Addressables が自動で初期化されるため。
            // 重複した Manager の確認の後に行う（確認より前だと、重複した分だけ変換関数が二重に連結される）
            if (initialize_settings.AccEnableCatalogCacheBusting)
            {
                _catalogCacheBusting = new CatalogCacheBustingService(DateTime.UtcNow.Ticks);
                _catalogCacheBusting.Apply();
            }

            _initializeController = new InitializeController(
                initialize_settings,
                new InitializeStateModel(),
                new AddressablesInitializeService());

            _downloadController = new DownloadController(
                _settings.AccDownload,
                new DownloadStatusModel(),
                new AddressablesDownloadService(),
                _initializeController); // Download は IInitializationGate としてしか知らない

            var loading_settings = _settings.AccLoading;
            // Registry 系の Model は、Controller と Editor の窓口の両方へ渡すため、ローカル変数に出す
            var asset_registry = new AssetRegistryModel();
            var issued_ticket_registry = new IssuedTicketRegistryModel();
            var owner_watch_registry = new OwnerWatchRegistryModel();
#if UNITY_EDITOR
            var leak_registry = new LeakRegistryModel();
            var owner_reclaim_registry = new OwnerReclaimRegistryModel();
#endif
            _loadingController = new LoadingController(
                loading_settings,
                new AssetLoaderServiceFactory(loading_settings),
                new LabelLocationService(),
                new AssetLifetimeWatcherBinder(),
                _initializeController, // Loading は IInitializationGate としてしか知らない
                asset_registry,
                issued_ticket_registry,
                owner_watch_registry
#if UNITY_EDITOR
                , leak_registry
                , owner_reclaim_registry
#endif
                );
#if UNITY_EDITOR
            _debugReader = new AddressableDebugReader(asset_registry, issued_ticket_registry, leak_registry, owner_watch_registry, owner_reclaim_registry);
#endif
            _handler = new AddressableHandler(_initializeController, _downloadController, _loadingController);

            // Addressables が初期化済みだと、自動開始と同時（Awake の中）に完了する。
            // 初期化イベントは Handler が Controller へ直接つなぐため、Manager は購読も中継もしない
            OnLateUpdate += _initializeController.Tick;
            OnLateUpdate += _downloadController.Tick;
            OnLateUpdate += _loadingController.Tick;
            OnSceneUnloaded += _loadingController.HandleSceneUnloaded;
            SceneManager.sceneUnloaded += RelaySceneUnloaded;

            if (initialize_settings.AccMode == InitializeMode.Auto)
                _initializeController.StartAutoInitialize(); // 失敗の記録・ログ・例外の観測は Controller が行う
        }

        // 当該フレーム中のDestroyを取りこぼさないため Update ではなく LateUpdate を使用する。
        // 渡す経過時間は、ポーズ（timeScale = 0）の間も進む unscaledDeltaTime にする。
        // owner のポーリングは「ゲーム内の時間」ではなく、解放の遅れを許容できる実時間で間隔を決めているため
        private void LateUpdate() => OnLateUpdate?.Invoke(Time.unscaledDeltaTime);

        private void RelaySceneUnloaded(Scene scene) => OnSceneUnloaded?.Invoke(scene);

        private void OnDestroy()
        {
            if (_instance != this) return; // 重複で破棄された Manager は何も持っていない

            // 順序：イベントの購読解除 → Loading の Dispose → Download の Dispose → Initialize の Dispose → 変換関数の復元
            SceneManager.sceneUnloaded -= RelaySceneUnloaded;
            OnSceneUnloaded -= _loadingController.HandleSceneUnloaded;
            OnLateUpdate -= _loadingController.Tick;
            OnLateUpdate -= _downloadController.Tick;
            OnLateUpdate -= _initializeController.Tick;
            _loadingController.Dispose(); // 残 Entry をすべて強制解放する。先に止めることで、初期化のキャンセルで再開した Load が、破棄済みの判定に確実に引っかかる
            _downloadController.Dispose(); // 実行中なら Canceled を通知して終える。初期化の待機を持つため、Initialize より先に止める
            _initializeController.Dispose();
            _catalogCacheBusting?.Restore(); // 自分の設定した関数のままのときだけ、設定前へ戻す
            _handler = null;
#if UNITY_EDITOR
            _debugReader = null;
#endif
            _instance = null;
        }

        // ドメインリロードを無効にした環境でも、次の Play 開始時に static を確実にリセットする
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticStateOnLoad()
        {
            _instance = null;
        }
    }
}
