using System;

namespace MAH.AddressableManager
{
    /// <summary>
    /// ダウンロード機能の処理（カタログの確認・更新・サイズ計算・ダウンロード）に失敗したことを示す例外。リトライを使い切っても成功しなかった場合に投げられる。
    /// 失敗した段階とリトライ回数を持ち、最後に発生した例外（Addressables の OperationException 等）は InnerException に入る。
    /// 呼び出し元の await に返り、DownloadStatus には持たせない。
    /// 初期化の待機で失敗した場合は、この例外に包まず、初期化の例外（AddressableInitializeException 等）をそのまま返す。
    /// </summary>
    public sealed class AddressableDownloadException : Exception
    {
        private readonly DownloadPhase _phase;
        private readonly int _retryCount;

        /// <summary> 失敗した段階 </summary>
        public DownloadPhase AccPhase => _phase;
        /// <summary> 失敗した段階で行ったリトライ回数 </summary>
        public int AccRetryCount => _retryCount;

        public AddressableDownloadException(DownloadPhase phase, int retry_count, string message, Exception inner_exception)
            : base(message, inner_exception)
        {
            _phase = phase;
            _retryCount = retry_count;
        }
    }
}
