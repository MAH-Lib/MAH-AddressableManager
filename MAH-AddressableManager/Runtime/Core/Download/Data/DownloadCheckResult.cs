namespace MAH.AddressableManager
{
    /// <summary>
    /// CheckUpdateAsync の戻り値。更新されたカタログ数・ダウンロードが必要な AssetBundle 数・合計バイト数を持つ。
    /// 確認ダイアログに「N 件 / xx MB」と表示するために使う。
    /// </summary>
    public readonly struct DownloadCheckResult
    {
        private readonly int _catalogUpdateCount;
        private readonly int _bundleCount;
        private readonly long _totalBytes;
        private readonly bool _isCatalogUpdatePending;
        private readonly bool _hasDownload;

        /// <summary>
        /// 更新があると分かったカタログの数。CheckAndUpdate では適用した数、CheckOnly では適用していない数。方針が None のときは 0
        /// </summary>
        public int AccCatalogUpdateCount => _catalogUpdateCount;
        /// <summary> ダウンロードが必要な AssetBundle の数（サイズが 0 より大きいものを重複なく数えた値） </summary>
        public int AccBundleCount => _bundleCount;
        /// <summary> ダウンロードが必要な合計バイト数 </summary>
        public long AccTotalBytes => _totalBytes;
        /// <summary>
        /// CheckOnly で、更新のあるカタログがあるのに適用していないか。
        /// このとき AccBundleCount・AccTotalBytes は、保留中の更新を含まない今のカタログに対する値
        /// </summary>
        public bool AccIsCatalogUpdatePending => _isCatalogUpdatePending;
        /// <summary> ダウンロードが必要なバンドルがあるか（AccBundleCount が 0 より大きい） </summary>
        public bool AccHasDownload => _hasDownload;

        /// <summary> DownloadController が、確認の結果から作る </summary>
        internal DownloadCheckResult(int catalog_update_count, int bundle_count, long total_bytes, bool is_catalog_update_pending)
        {
            _catalogUpdateCount = catalog_update_count;
            _bundleCount = bundle_count;
            _totalBytes = total_bytes;
            _isCatalogUpdatePending = is_catalog_update_pending;
            _hasDownload = bundle_count > 0; // アクセサの中で比較しないため、生成時に求める
        }
    }
}
