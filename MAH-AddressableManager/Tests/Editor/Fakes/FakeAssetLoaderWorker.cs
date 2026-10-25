using System;
using System.Threading;
using System.Threading.Tasks;
using MAH.AddressableManager.Core.Loading;
using UnityEngine.ResourceManagement.AsyncOperations;
#if ADDRESSABLE_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#endif

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// IAssetLoaderWorker の偽実装。実際の Addressables を呼ばず、テストから完了・失敗を手動で制御する。
    /// </summary>
    internal sealed class FakeAssetLoaderWorker : IAssetLoaderWorker
    {
        private readonly TaskCompletionSource<bool> _tcs = new();
        private Exception _exception;

        /// <summary> Dispose が呼ばれた回数 </summary>
        internal int DisposeCount { get; private set; }

        public UnityEngine.Object AccAsset { get; private set; }
        public Type AccAssetType { get; }
        public AsyncOperationStatus AccStatus { get; private set; } = AsyncOperationStatus.None;

        internal FakeAssetLoaderWorker(Type asset_type)
        {
            AccAssetType = asset_type;
        }

#if ADDRESSABLE_UNITASK_SUPPORT
        public async UniTask LoadAsync(CancellationToken token)
#else
        public async Task LoadAsync(CancellationToken token)
#endif
        {
            await _tcs.Task; // Task/UniTaskどちらのasyncメソッドからでもTaskをawaitできる
            if (_exception != null)
                throw _exception;
        }

        /// <summary> テストからロード成功を発火させる </summary>
        internal void Complete(UnityEngine.Object asset)
        {
            AccAsset = asset;
            AccStatus = AsyncOperationStatus.Succeeded;
            _tcs.TrySetResult(true);
        }

        /// <summary> テストからロード失敗を発火させる </summary>
        internal void Fail(Exception exception)
        {
            _exception = exception;
            AccStatus = AsyncOperationStatus.Failed;
            _tcs.TrySetResult(true);
        }

        public void Dispose() => DisposeCount++;
    }
}
