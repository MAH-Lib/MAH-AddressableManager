using System;
using System.Collections.Generic;
using System.Text;
using MAH.AddressableManager.Attribute;
using MAH.AddressableManager.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MAH.AddressableManager.Samples
{
    /// <summary>
    /// 初期化機能（Auto / Manual、初期化前の Load の扱い）を確かめるサンプル。
    /// <para>
    /// Manager はシーンに置かず、設定の違う Manager Prefab をボタンで生成・破棄する。
    /// Manager は <c>Awake</c> でしか設定を読まないため、設定を切り替えるには「その設定の Prefab を生成する」しかない。
    /// </para>
    /// <para>
    /// 【確かめられること】
    /// <list type="bullet">
    /// <item>Auto：生成した時点で初期化が始まる（Addressables が初期化済みなら、生成の途中で終わる）</item>
    /// <item>Manual：<c>InitializeAsync</c> を呼ぶまで初期化しない。呼び出しは何度でも安全（2回目以降はすぐ完了）</item>
    /// <item>Manual + PassThrough：初期化前の Load は通る（Addressables が自分で初期化する。Manager の状態は NotStarted のまま）</item>
    /// <item>Manual + WaitForInitialize：NotStarted の間の Load は <see cref="InvalidOperationException"/> になる</item>
    /// </list>
    /// </para>
    /// <para>
    /// ⚠ Addressables の初期化は、アプリの1回の起動で1度しか行われない。
    /// 「最初の初期化」を確かめられるのは、起動（Play 開始）後に最初に Manager を生成したときだけ。
    /// 別の設定で確かめるときは、Play を止めて（実機ではアプリを終了して）やり直す。
    /// </para>
    /// <para>
    /// 【実装例として真似る部分】<see cref="InitializeAsync"/> の呼び方と、<see cref="HandleSpawned"/> の購読（成功・失敗の両方）。
    /// <c>// [検証用]</c> と書かれた処理は動作確認のためだけのもので、実装では不要。
    /// </para>
    /// </summary>
    public sealed class InitializeSampleView : MonoBehaviour, IProgress<float>
    {
        // [検証用] Console 上で、モジュール本体のログ（[AddressableManager]）とサンプルのログを見分けるためのタイトル
        private const string LOG_TITLE = "AddressableSample";
        // [検証用] 画面のログ欄に残す件数
        private const int LOG_CAPACITY = 10;

        [Header("生成する Manager Prefab（設定ごと）")]
        [SerializeField] private AddressableManager _autoManagerPrefab;
        [SerializeField] private AddressableManager _manualManagerPrefab;
        [SerializeField] private AddressableManager _manualWaitManagerPrefab;

        [Header("Load するアセット")]
        [SerializeField, AddressablePath] private string _testAddress;
        [SerializeField] private Image _previewImage;

        [Header("操作ボタン")]
        [SerializeField] private Button _spawnAutoButton;
        [SerializeField] private Button _spawnManualButton;
        [SerializeField] private Button _spawnManualWaitButton;
        [SerializeField] private Button _initializeButton;
        [SerializeField] private Button _loadButton;
        [SerializeField] private Button _releaseButton;
        [SerializeField] private Button _destroyButton;

        [Header("状態表示")]
        [SerializeField] private Text _statusText;
        // [検証用] 実機には Console が無いため、操作と結果を画面にも出す
        [SerializeField] private Text _logText;

        // この View が生成した Manager。AccInstance ではなく、自分で生成したものだけを扱う
        private AddressableManager _manager;
        private string _managerLabel = "-";

        // Load したチケット。Release するまで保持する必要がある
        private readonly List<AssetTicket<Sprite>> _tickets = new();

        // [検証用] 画面表示用の記録
        private int _initializedCount;
        private float _lastProgress = -1f;
        private string _lastResult = "None";
        private readonly string[] _logs = new string[LOG_CAPACITY];
        private int _logCount;
        private int _logNext;
        private readonly StringBuilder _builder = new();

        // ボタンとイベントに渡す delegate は Awake で1度だけ作って保持する（購読・解除を対で書くため）
        private UnityAction _onSpawnAutoClicked;
        private UnityAction _onSpawnManualClicked;
        private UnityAction _onSpawnManualWaitClicked;
        private UnityAction _onInitializeClicked;
        private UnityAction _onLoadClicked;
        private UnityAction _onReleaseClicked;
        private UnityAction _onDestroyClicked;
        private Action _onInitialized;
        private Action<AddressableInitializeException> _onInitializeFailed;

        private void Awake()
        {
            _onSpawnAutoClicked = () => Spawn(_autoManagerPrefab);
            _onSpawnManualClicked = () => Spawn(_manualManagerPrefab);
            _onSpawnManualWaitClicked = () => Spawn(_manualWaitManagerPrefab);
            _onInitializeClicked = OnInitializeClicked;
            _onLoadClicked = OnLoadClicked;
            _onReleaseClicked = OnReleaseClicked;
            _onDestroyClicked = OnDestroyClicked;
            _onInitialized = HandleInitialized;
            _onInitializeFailed = HandleInitializeFailed;

            AddListener(_spawnAutoButton, _onSpawnAutoClicked);
            AddListener(_spawnManualButton, _onSpawnManualClicked);
            AddListener(_spawnManualWaitButton, _onSpawnManualWaitClicked);
            AddListener(_initializeButton, _onInitializeClicked);
            AddListener(_loadButton, _onLoadClicked);
            AddListener(_releaseButton, _onReleaseClicked);
            AddListener(_destroyButton, _onDestroyClicked);

            // Sprite が無い状態で Image を有効にすると白い四角が出るため、読み込むまで隠す
            HidePreview();
            RefreshStatus();
        }

        private void OnDestroy()
        {
            // イベント購読は必ず対で解除する
            RemoveListener(_spawnAutoButton, _onSpawnAutoClicked);
            RemoveListener(_spawnManualButton, _onSpawnManualClicked);
            RemoveListener(_spawnManualWaitButton, _onSpawnManualWaitClicked);
            RemoveListener(_initializeButton, _onInitializeClicked);
            RemoveListener(_loadButton, _onLoadClicked);
            RemoveListener(_releaseButton, _onReleaseClicked);
            RemoveListener(_destroyButton, _onDestroyClicked);

            // 【後始末】Manager が残っていれば、購読を解除し、Manual 寿命のチケットを Release する。
            // ⚠ Play 停止時は Manager が先に破棄されていることがある。その場合は Manager の破棄時に
            //    全アセットが解放済みなので、ここでは何もしない（null かどうかで判断する）。
            if (_manager != null)
            {
                var handler = _manager.AccHandler;
                handler.OnInitialized -= _onInitialized;
                handler.OnInitializeFailed -= _onInitializeFailed;
                foreach (var ticket in _tickets)
                {
                    handler.Release(ticket);
                }
            }
            _tickets.Clear();
        }

        /// <summary> 進捗の通知を受け取る（<see cref="IProgress{T}"/>）。Progress&lt;T&gt; やラムダを使わず、確保を増やさない </summary>
        public void Report(float value)
        {
            // [検証用] 進捗を画面に出す
            _lastProgress = value;
            RefreshStatus();
        }

        private void Spawn(AddressableManager prefab)
        {
            if (prefab == null)
            {
                SetResult("Manager prefab is not assigned");
                return;
            }

            // Manager は1つしか持てない（2つ目は自分で破棄してしまう）。AccInstance は DontDestroyOnLoad の Manager を指す
            if (AddressableManager.AccInstance != null)
            {
                SetResult("Manager already exists. Press Destroy Manager first");
                return;
            }

            _manager = Instantiate(prefab);
            _managerLabel = prefab.name;
            HandleSpawned();
            SetResult($"Spawned {_managerLabel}");
        }

        private void HandleSpawned()
        {
            // 【購読の罠】Auto では、生成した瞬間（Manager の Awake の中）に初期化が終わることがある（成功も失敗も）。
            // その場合は購読した時点で OnInitialized / OnInitializeFailed は二度と来ないため、購読したらすぐ状態も確かめる。
            // 初期化・読み込み・解放の入口は Handler 1つ。Manager から取得して使う
            var handler = _manager.AccHandler;
            handler.OnInitialized += _onInitialized;
            handler.OnInitializeFailed += _onInitializeFailed;
            if (handler.AccIsInitialized)
                HandleInitialized();
            else if (handler.AccInitializeState == InitializeState.Failed)
                ShowInitializeFailed("failed before subscribing, exception not available"); // 購読前の失敗は、例外を受け取れない
        }

        private void HandleInitialized()
        {
            // [検証用] 受信回数を数える。何度 InitializeAsync を呼んでも 1 のままになることを確かめる
            _initializedCount++;
            AddLog($"OnInitialized (count: {_initializedCount})");
            RefreshStatus();
        }

        private void HandleInitializeFailed(AddressableInitializeException ex)
        {
            // 初期化の失敗は致命的で、Addressables はアプリを再起動するまでやり直せない。
            // 実装では、ここで再起動を促すダイアログなどを出す。InnerException に Addressables の元の例外が入っている
            ShowInitializeFailed(ex.InnerException != null ? ex.InnerException.GetType().Name : ex.GetType().Name);
        }

        // [検証用] 失敗を画面に出す。購読前に失敗していた場合と、イベントで受け取った場合で共通
        private void ShowInitializeFailed(string detail)
        {
            AddLog($"OnInitializeFailed ({detail})");
            SetResult("Initialize failed. Restart the app (Addressables cannot recover)");
        }

        // async void はボタンのハンドラのように戻り値を受け取れない入口だけに使い、中は必ず try / catch で囲む
        private async void OnInitializeClicked()
        {
            try
            {
                if (!HasManager()) return;

                // 【Initialize】Manual のときに呼ぶ。何度呼んでも安全で、初期化中・初期化済みなら同じ結果をすぐ受け取れる。
                // destroyCancellationToken を渡すと、この View が破棄されたとき（画面遷移など）に待機をやめられる。
                // キャンセルで止まるのは待機だけで、初期化そのものは止まらない。
                _lastProgress = -1f;

                // [検証用] 初期化済みで呼んだことを、ログで見分けられるようにする。呼び出し自体は安全で、すぐ完了する
                var handler = _manager.AccHandler;
                var was_initialized = handler.AccIsInitialized;
                AddLog(was_initialized
                    ? $"InitializeAsync: already initialized (state: {handler.AccInitializeState}). Completes immediately"
                    : "InitializeAsync: start");

                await handler.InitializeAsync(this, destroyCancellationToken);
                SetResult(was_initialized ? "Initialize skipped: already initialized" : "Initialize succeeded");
            }
            catch (OperationCanceledException)
            {
                // 待機中に View または Manager が破棄された（Manager の破棄でも、待機は OperationCanceledException で終わる）。UI はもう無いので、ログだけ出す
                AddressableLogger.Log("待機中に View または Manager が破棄されました。", LOG_TITLE);
            }
            catch (Exception ex)
            {
                // 初期化の失敗は AddressableInitializeException で届く（このサンプルでは再現しない）
                SetResult($"Initialize failed: {ex.GetType().Name}");
                AddLog($"{ex.GetType().Name}: {ex.Message}");
                AddressableLogger.LogException(ex);
            }
        }

        private async void OnLoadClicked()
        {
            try
            {
                if (!HasManager()) return;

                // 初期化前に Load したときの動きは、Manager の設定（PassThrough / WaitForInitialize）で変わる
                var key = AssetKey.FromPath(_testAddress);
                AddLog($"LoadAsync: start (state: {_manager.AccHandler.AccInitializeState})");
                var ticket = await _manager.AccHandler.LoadAsync<Sprite>(key, AssetLifetime.Manual, destroyCancellationToken);

                // ⚠ await の間に View が破棄されていたら、チケットを受け取ったここで返しておく
                if (destroyCancellationToken.IsCancellationRequested)
                {
                    if (_manager != null) _manager.AccHandler.Release(ticket);
                    return;
                }

                // ⚠ await の間に Manager が破棄されていたら、アセットは解放済みで使えない
                if (_manager == null)
                {
                    SetResult("Manager was destroyed while loading");
                    return;
                }

                // チケットは Release するまで保持する。無くすと解放できなくなる（Manager の破棄まで残る）
                _tickets.Add(ticket);
                _previewImage.sprite = ticket.AccAsset;
                _previewImage.enabled = true;
                SetResult($"Load succeeded (state: {_manager.AccHandler.AccInitializeState})");
            }
            catch (OperationCanceledException)
            {
                AddressableLogger.Log("待機中にキャンセルされました。", LOG_TITLE);
            }
            catch (Exception ex)
            {
                SetResult($"Load failed: {ex.GetType().Name}");
                AddLog($"{ex.GetType().Name}: {ex.Message}");
                AddressableLogger.LogException(ex);
            }
        }

        private void OnReleaseClicked()
        {
            if (!HasManager()) return;

            if (_tickets.Count == 0)
            {
                SetResult("No ticket to release. Press Load first");
                return;
            }

            var last_index = _tickets.Count - 1;
            var ticket = _tickets[last_index];
            _tickets.RemoveAt(last_index);

            // ⚠ アセットを使っているものは、Release の前に片付ける。
            //    最後のチケットを Release するとアセットがアンロードされ、Image が参照したままだと表示が壊れる。
            if (_tickets.Count == 0) HidePreview();

            _manager.AccHandler.Release(ticket);
            SetResult($"Released. Remaining tickets: {_tickets.Count}");
        }

        private void OnDestroyClicked()
        {
            if (_manager == null)
            {
                SetResult("No manager to destroy");
                return;
            }

            // Manager の破棄で、読み込み済みのアセットはすべて解放される。チケットは参照を捨てるだけでよい
            _manager.AccHandler.OnInitialized -= _onInitialized;
            _manager.AccHandler.OnInitializeFailed -= _onInitializeFailed;
            Destroy(_manager.gameObject);
            _manager = null;
            _managerLabel = "-";
            _tickets.Clear();
            HidePreview();

            // [検証用] 次に生成する Manager のために数え直す
            _initializedCount = 0;
            _lastProgress = -1f;
            SetResult("Destroyed manager");
        }

        /// <summary> Manager が無ければ表示だけ出して false を返す </summary>
        private bool HasManager()
        {
            if (_manager != null) return true;

            SetResult("No manager. Press Spawn first");
            return false;
        }

        private void HidePreview()
        {
            if (_previewImage == null) return;
            _previewImage.sprite = null;
            _previewImage.enabled = false;
        }

        // [検証用] 最後の操作の結果を、ログ欄にも残して画面に出す
        private void SetResult(string result)
        {
            _lastResult = result;
            AddLog(result);
            RefreshStatus();
        }

        // [検証用] 直近 LOG_CAPACITY 件を、固定長の配列に使い回して残す
        private void AddLog(string message)
        {
            AddressableLogger.Log(message, LOG_TITLE);

            _logs[_logNext] = message;
            _logNext = (_logNext + 1) % LOG_CAPACITY;
            if (_logCount < LOG_CAPACITY) _logCount++;

            if (_logText == null) return;
            _builder.Clear();
            // 新しい順に並べる
            for (var i = 0; i < _logCount; i++)
            {
                var index = (_logNext - 1 - i + LOG_CAPACITY) % LOG_CAPACITY;
                _builder.Append(_logs[index]).Append('\n');
            }
            _logText.text = _builder.ToString();
        }

        // [検証用] 状態を画面に出す。イベントと操作の完了時にだけ呼ぶ（毎フレームは更新しない）
        private void RefreshStatus()
        {
            if (_statusText == null) return;

            var has_manager = _manager != null;
            var state = has_manager ? _manager.AccHandler.AccInitializeState.ToString() : "-";
            var is_initialized = has_manager ? _manager.AccHandler.AccIsInitialized.ToString() : "-";
            var progress = _lastProgress < 0f ? "-" : _lastProgress.ToString("0.00");

            _builder.Clear();
            _builder.Append("Manager: ").Append(has_manager ? "exists" : "none").Append('\n')
                .Append("  prefab: ").Append(_managerLabel).Append('\n')
                .Append("  state: ").Append(state).Append('\n')
                .Append("  AccIsInitialized: ").Append(is_initialized).Append('\n')
                .Append("  progress: ").Append(progress).Append('\n')
                .Append("  OnInitialized received: ").Append(_initializedCount).Append('\n')
                .Append("Tickets held: ").Append(_tickets.Count).Append('\n')
                .Append("Last action: ").Append(_lastResult);
            _statusText.text = _builder.ToString();
        }

        private static void AddListener(Button button, UnityAction action)
        {
            if (button != null) button.onClick.AddListener(action);
        }

        private static void RemoveListener(Button button, UnityAction action)
        {
            if (button != null) button.onClick.RemoveListener(action);
        }
    }
}
