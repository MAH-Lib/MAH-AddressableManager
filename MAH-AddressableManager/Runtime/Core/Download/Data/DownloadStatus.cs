using UnityEngine;

namespace MAH.AddressableManager
{
    /// <summary>
    /// 取得・通知した時点のダウンロードの状態（値のコピー）。後から値は変わらないため、最新の値が必要なときは取得し直す。
    /// 状態そのものは DownloadStatusModel が持ち、取得・通知のたびにこの構造体を作って渡す（前の処理の値を誤って読む問題を、構造上起こさないため）。
    /// default 値は「まだ何も処理していない」状態（AccPhase == None）。
    /// 失敗の例外は持たない。呼び出し元の await に AddressableDownloadException として返る。
    /// </summary>
    public readonly struct DownloadStatus
    {
        private readonly DownloadPhase _phase;
        private readonly long _downloadedBytes;
        private readonly long _totalBytes;
        private readonly int _retryCount;
        private readonly float _percent;
        private readonly bool _isBusy;
        private readonly bool _isDownloading;

        /// <summary> 処理の段階 </summary>
        public DownloadPhase AccPhase => _phase;
        /// <summary> ダウンロード中か（AccPhase が Downloading） </summary>
        public bool AccIsDownloading => _isDownloading;
        /// <summary> 処理の途中か（確認中・更新中・サイズ計算中・ダウンロード中） </summary>
        public bool AccIsBusy => _isBusy;
        /// <summary> ダウンロード済みのバイト数 </summary>
        public long AccDownloadedBytes => _downloadedBytes;
        /// <summary> ダウンロードの合計バイト数 </summary>
        public long AccTotalBytes => _totalBytes;
        /// <summary> ダウンロードの割合（0〜1）。Completed なら 1、合計バイト数が 0 以下のときは 0 </summary>
        public float AccPercent => _percent;
        /// <summary> 現在の段階でのリトライ回数。段階が進むと 0 に戻る </summary>
        public int AccRetryCount => _retryCount;

        /// <summary> DownloadStatusModel が、取得・通知のたびに作る </summary>
        internal DownloadStatus(DownloadPhase phase, long downloaded_bytes, long total_bytes, int retry_count)
        {
            _phase = phase;
            _downloadedBytes = downloaded_bytes;
            _totalBytes = total_bytes;
            _retryCount = retry_count;

            // 割合と処理中の判定は、アクセサの中で計算せず、生成時に求める（アクセサにロジックを書かない規則のため）
            if (phase == DownloadPhase.Completed)
                _percent = 1f;
            else if (total_bytes > 0)
                _percent = Mathf.Clamp01((float)downloaded_bytes / total_bytes);
            else
                _percent = 0f;

            _isDownloading = phase == DownloadPhase.Downloading;
            _isBusy = phase == DownloadPhase.CheckingCatalog
                || phase == DownloadPhase.UpdatingCatalog
                || phase == DownloadPhase.CalculatingSize
                || phase == DownloadPhase.Downloading;
        }
    }
}
