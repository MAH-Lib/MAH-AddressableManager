using System;
using System.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MAH.AddressableManager.Core.Initialize
{
    /// <summary>
    /// Addressables.InitializeAsync を1回呼び、完了を Task に変えて返す Service。
    /// 結果は Completed コールバックの中で読む（await の後では、autoReleaseHandle=true の handle が解放済みで読めない場合があるため）。
    /// AsyncCompat.ToTask は UniTask 版では定義されないため使わず、TaskCompletionSource で直接変換する。
    /// </summary>
    internal sealed class AddressablesInitializeService : IAddressablesInitializeService
    {
        private readonly Action<AsyncOperationHandle<IResourceLocator>> _onCompleted; // 毎回ラムダを作らないよう、キャッシュしておく
        private AsyncOperationHandle<IResourceLocator> _handle;
        private TaskCompletionSource<bool> _completionSource;
        private bool _autoReleaseHandle;
        private bool _isCompleted;

        internal AddressablesInitializeService()
        {
            _onCompleted = Complete;
        }

        public Task StartInitialize(bool auto_release_handle)
        {
            if (_completionSource != null)
                throw new InvalidOperationException("初期化はすでに始まっています。Service は1回しか使えません。");

            _completionSource = new TaskCompletionSource<bool>();
            _autoReleaseHandle = auto_release_handle;

            try
            {
                _handle = Addressables.InitializeAsync(auto_release_handle);
            }
            catch (Exception exception)
            {
                // 失敗済みの初期化を呼び直したときなど、Addressables は同期で例外を投げる。呼び出し元には失敗した Task として渡す
                _isCompleted = true;
                _completionSource.TrySetException(exception);
                return _completionSource.Task;
            }

            if (_handle.IsDone)
                Complete(_handle); // 初期化済みなどで、呼んだ時点で完了している場合は、Completed を待たずにその場で結果を読む
            else
                _handle.Completed += _onCompleted;
            return _completionSource.Task;
        }

        public float GetPercentComplete()
        {
            if (_isCompleted) return 1f;
            return _handle.IsValid() ? _handle.PercentComplete : 0f;
        }

        public void ReleaseHandle()
        {
            if (!_handle.IsValid()) return;
            Addressables.Release(_handle);
            _handle = default;
        }

        private void Complete(AsyncOperationHandle<IResourceLocator> handle)
        {
            // 結果は、完了通知の間に読み切る。以降は handle が解放されている場合がある
            var status = handle.Status;
            var exception = handle.OperationException;
            _isCompleted = true;
            if (_autoReleaseHandle)
                _handle = default; // Addressables が解放するため、こちらでは持たない

            if (status == AsyncOperationStatus.Succeeded)
                _completionSource.TrySetResult(true);
            else
                _completionSource.TrySetException(exception ?? new Exception("Addressables の初期化に失敗しました。"));
        }
    }
}
