using System;
using System.Collections.Generic;
using System.Threading;
using MAH.AddressableManager.Core.Download;
using MAH.AddressableManager.Core.Initialize;
using MAH.AddressableManager.Core.Loading;
#if ADDRESSABLE_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#else
using System.Threading.Tasks;
#endif

namespace MAH.AddressableManager.Core
{
    /// <summary>
    /// IAddressableHandler の実装。状態を持たず、各 Controller への転送に徹する。
    /// 複数の機能の Controller を知るため、どの機能フォルダにも置かない（機能フォルダ同士の相互参照を避ける）。
    /// </summary>
    internal sealed class AddressableHandler : IAddressableHandler
    {
        private readonly InitializeController _initializeController;
        private readonly DownloadController _downloadController;
        private readonly LoadingController _loadingController;

        internal AddressableHandler(InitializeController initializeController, DownloadController downloadController, LoadingController loadingController)
        {
            _initializeController = initializeController;
            _downloadController = downloadController;
            _loadingController = loadingController;
        }

        #region 初期化

        public InitializeState AccInitializeState => _initializeController.AccState;

        public bool AccIsInitialized => _initializeController.AccIsInitialized;

        // イベントは Handler にフィールドを持たせず、Controller のイベントへ直接つなぐ（購読の状態を Handler に作らないため）
        public event Action OnInitialized
        {
            add => _initializeController.OnSucceeded += value;
            remove => _initializeController.OnSucceeded -= value;
        }

        public event Action<AddressableInitializeException> OnInitializeFailed
        {
            add => _initializeController.OnFailed += value;
            remove => _initializeController.OnFailed -= value;
        }

#if ADDRESSABLE_UNITASK_SUPPORT
        public async UniTask InitializeAsync(IProgress<float> progress = null, CancellationToken ct = default)
            => await _initializeController.InitializeAsync(progress, ct); // 共有するタスクは Task のまま。公開 API の戻り値だけを UniTask にする
#else
        public Task InitializeAsync(IProgress<float> progress = null, CancellationToken ct = default)
            => _initializeController.InitializeAsync(progress, ct);
#endif

        #endregion

        #region ダウンロード

        public DownloadStatus AccDownloadStatus => _downloadController.AccStatus;

        // Label を渡さない版は null を渡す（DownloadController が Runtime SO の Label に解決する。Handler は設定を持たない）。
        // Label を渡す版は、null を ArgumentNullException にする（DownloadController では null が「SO の Label を使う」を意味するため）。
        // 呼び出し時に投げず、失敗した Task / UniTask で返す（Controller の検証エラーと同じく、await の時点で例外が届くようにそろえるため）

#if ADDRESSABLE_UNITASK_SUPPORT
        public UniTask<bool> HasUpdateAsync(CancellationToken ct = default)
            => _downloadController.HasUpdateAsync(null, ct);

        public UniTask<bool> HasUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct = default)
            => labels == null ? UniTask.FromException<bool>(CreateNullLabelsException()) : _downloadController.HasUpdateAsync(labels, ct);

        public UniTask<DownloadCheckResult> CheckUpdateAsync(CancellationToken ct = default)
            => _downloadController.CheckUpdateAsync(null, ct);

        public UniTask<DownloadCheckResult> CheckUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct = default)
            => labels == null ? UniTask.FromException<DownloadCheckResult>(CreateNullLabelsException()) : _downloadController.CheckUpdateAsync(labels, ct);

        public UniTask DownloadAsync(IProgress<DownloadStatus> progress = null, CancellationToken ct = default)
            => _downloadController.DownloadAsync(null, progress, ct);

        public UniTask DownloadAsync(IReadOnlyList<string> labels, IProgress<DownloadStatus> progress = null, CancellationToken ct = default)
            => labels == null ? UniTask.FromException(CreateNullLabelsException()) : _downloadController.DownloadAsync(labels, progress, ct);
#else
        public Task<bool> HasUpdateAsync(CancellationToken ct = default)
            => _downloadController.HasUpdateAsync(null, ct);

        public Task<bool> HasUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct = default)
            => labels == null ? Task.FromException<bool>(CreateNullLabelsException()) : _downloadController.HasUpdateAsync(labels, ct);

        public Task<DownloadCheckResult> CheckUpdateAsync(CancellationToken ct = default)
            => _downloadController.CheckUpdateAsync(null, ct);

        public Task<DownloadCheckResult> CheckUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct = default)
            => labels == null ? Task.FromException<DownloadCheckResult>(CreateNullLabelsException()) : _downloadController.CheckUpdateAsync(labels, ct);

        public Task DownloadAsync(IProgress<DownloadStatus> progress = null, CancellationToken ct = default)
            => _downloadController.DownloadAsync(null, progress, ct);

        public Task DownloadAsync(IReadOnlyList<string> labels, IProgress<DownloadStatus> progress = null, CancellationToken ct = default)
            => labels == null ? Task.FromException(CreateNullLabelsException()) : _downloadController.DownloadAsync(labels, progress, ct);
#endif

        private static ArgumentNullException CreateNullLabelsException()
            => new ArgumentNullException("labels");

        #endregion

        #region アセットの読み込み・解放

#if ADDRESSABLE_UNITASK_SUPPORT
        public UniTask<AssetTicket<T>> LoadAsync<T>(AssetKey key, AssetLifetime lifetime, CancellationToken ct = default)
            where T : UnityEngine.Object
            => _loadingController.LoadAsync<T>(key, lifetime, ct);
#else
        public Task<AssetTicket<T>> LoadAsync<T>(AssetKey key, AssetLifetime lifetime, CancellationToken ct = default)
            where T : UnityEngine.Object
            => _loadingController.LoadAsync<T>(key, lifetime, ct);
#endif

        public void Release<T>(AssetTicket<T> ticket) where T : UnityEngine.Object
            => _loadingController.Release(ticket);

#if ADDRESSABLE_UNITASK_SUPPORT
        public UniTask<AssetGroupTicket> LoadByLabelAsync(string label, AssetLifetime lifetime, CancellationToken ct = default)
            => _loadingController.LoadByLabelAsync(label, lifetime, ct);
#else
        public Task<AssetGroupTicket> LoadByLabelAsync(string label, AssetLifetime lifetime, CancellationToken ct = default)
            => _loadingController.LoadByLabelAsync(label, lifetime, ct);
#endif

        public void Release(AssetGroupTicket ticket)
            => _loadingController.Release(ticket);

        #endregion
    }
}
