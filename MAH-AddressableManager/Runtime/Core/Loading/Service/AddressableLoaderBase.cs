using System;
using System.Threading;
using System.Threading.Tasks;
using MAH.AddressableManager;
using MAH.AddressableManager.Core;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#if ADDRESSABLE_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#endif

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// Addressables からの実ロードを担うベースクラス。リトライ・ロギング・キャンセル処理・handle 解放を担当する。
    /// dedup・RefCount は LoadingController 側の責務であり、ここでは関知しない。
    /// string キー（AssetKey.AccValue）のみを受け取るため、Path/Reference どちらの入口かは意識しない。
    /// </summary>
    internal abstract class AddressableLoaderBase<T> : IAssetLoaderWorker where T : UnityEngine.Object
    {
        private readonly AssetKey _key;
        private readonly int _maxRetry;
        private readonly int _retryDelayMs;
        private AsyncOperationHandle<T> _handle;
        private T _loadedAsset;
        private bool _isLoading; // handle の完了待ち中か。この間の Dispose は handle を解放せず、LoadAsync 側に委ねる
        private bool _isDisposed;

        /// <inheritdoc/>
        public UnityEngine.Object AccAsset => _loadedAsset;
        /// <inheritdoc/>
        public Type AccAssetType => typeof(T);
        /// <inheritdoc/>
        public AsyncOperationStatus AccStatus => _handle.IsValid() ? _handle.Status : AsyncOperationStatus.None;

        protected AddressableLoaderBase(AssetKey key, int max_retry, int retry_delay_ms)
        {
            if (string.IsNullOrEmpty(key.AccValue))
                throw new ArgumentException("読み込み用のキーが null または空文字です。", nameof(key));
            if (max_retry <= 0)
                throw new ArgumentOutOfRangeException(nameof(max_retry), "リトライ回数は1以上を指定してください。");

            _key = key;
            _maxRetry = max_retry;
            _retryDelayMs = retry_delay_ms;
        }

        /// <summary> アセット読み込み。失敗するたびにリトライし、使い切ったら AssetLoadException を投げる </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        public async UniTask LoadAsync(CancellationToken token)
#else
        public async Task LoadAsync(CancellationToken token)
#endif
        {
            Exception last_exception = null;
            var is_retry_hopeless = false; // 回復の見込みがない失敗で、リトライを打ち切ったか

            for (int attempt = 1; attempt <= _maxRetry; attempt++)
            {
                ThrowIfDisposed(); // リトライ待機の間に Dispose された（handle は解放済みのため、ここで抜けてよい）

                try
                {
                    _handle = Addressables.LoadAssetAsync<T>(_key.AccValue);
                    _isLoading = true;
#if ADDRESSABLE_UNITASK_SUPPORT
                    await _handle.ToUniTask(cancellationToken: token);
#else
                    await _handle.ToTask().WithCancellation(token);
#endif
                    _isLoading = false;

                    // 完了待ちの間に Dispose された場合は、下の catch で、この handle を解放してから抜ける
                    ThrowIfDisposed();

                    if (_handle.Status == AsyncOperationStatus.Succeeded)
                    {
                        _loadedAsset = _handle.Result;
                        AddressableLogger.Log($"{_key} のリソース読み込みが完了しました。");
                        return;
                    }

                    last_exception = _handle.OperationException;
                    AddressableLogger.LogWarning($"{_key} のリソース読み込みに失敗しました [Status: {_handle.Status}]");
                }
                catch (OperationCanceledException)
                {
                    // キャンセル時・破棄時も handle は必ず解放する（呼び出し元の待機だけが止まり、リトライは行わない）
                    ReleaseHandle();
                    AddressableLogger.Log(_isDisposed
                        ? $"{_key} の Worker が破棄されたため、リソース読み込みを中断しました。"
                        : $"{_key} のリソース読み込みがキャンセルされました。");
                    throw;
                }
                catch (Exception ex)
                {
                    last_exception = ex;
                    AddressableLogger.LogException(ex);
                }
                finally
                {
                    _isLoading = false; // 例外で抜けた場合も、完了待ちの印を戻す
                }

                ReleaseHandle(); // 失敗した handle は次のリトライ前に解放しておく

                // アドレスの書き間違いなど、待ってもやり直しても結果が変わらない失敗は、リトライせずに打ち切る
                if (IsUnrecoverable(last_exception))
                {
                    is_retry_hopeless = true;
                    break;
                }

                if (attempt < _maxRetry)
                {
                    ThrowIfDisposed(); // 破棄済みなら、リトライ待機に入らずに抜ける
                    await AsyncCompat.Delay(_retryDelayMs, token); // ポーズ中も実時間で待つ（AsyncCompat.Delay 参照）
                }
            }

            var message = is_retry_hopeless
                ? $"{_key} のリソースロードが、リトライしても回復しない原因（キーが無効）で失敗しました。アドレスを確認してください。"
                : $"{_key} のリソースロードが最大試行回数（{_maxRetry}回）を超えて失敗しました。";
            AddressableLogger.LogError(message);
            throw new AssetLoadException(_key, message, last_exception);
        }

        /// <summary> 例外（とその InnerException）に InvalidKeyException を含む場合は、リトライしても回復しない </summary>
        private static bool IsUnrecoverable(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is InvalidKeyException)
                    return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // 完了前の handle は解放しない。完了待ちの LoadAsync が、再開後に解放して抜ける
            // （ここで解放すると、再開した LoadAsync が失敗とみなしてリトライし、新しい handle がリークする）
            if (_isLoading) return;
            ReleaseHandle();
        }

        // 破棄による中断はキャンセル扱いにする（共有ロードタスクを Faulted にせず、未観測の例外を出さないため）
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new OperationCanceledException($"{_key} の Worker が破棄されたため、リソース読み込みを中断しました。");
        }

        private void ReleaseHandle()
        {
            _loadedAsset = null;
            if (!_handle.IsValid())
                return;
            Addressables.Release(_handle);
            _handle = default;
        }
    }
}
