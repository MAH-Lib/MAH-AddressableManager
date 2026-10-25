namespace MAH.AddressableManager.Core.Download
{
    /// <summary>
    /// ダウンロードが必要なバンドルの数と合計バイト数。Service が計算して、DownloadController へ渡す。
    /// Addressables.GetDownloadSizeAsync は合計バイトしか返さないため、件数もあわせて数えた結果をこの型で受け渡す。
    /// </summary>
    internal readonly struct DownloadSizeInfo
    {
        private readonly int _bundleCount;
        private readonly long _totalBytes;

        /// <summary> ダウンロードが必要な AssetBundle の数（サイズが 0 より大きいものを重複なく数えた値） </summary>
        internal int AccBundleCount => _bundleCount;
        /// <summary> ダウンロードが必要な合計バイト数 </summary>
        internal long AccTotalBytes => _totalBytes;

        internal DownloadSizeInfo(int bundle_count, long total_bytes)
        {
            _bundleCount = bundle_count;
            _totalBytes = total_bytes;
        }
    }
}
