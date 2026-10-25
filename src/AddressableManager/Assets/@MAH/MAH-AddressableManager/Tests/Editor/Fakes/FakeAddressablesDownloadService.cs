using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MAH.AddressableManager.Core.Download;
#if ADDRESSABLE_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#endif

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// IAddressablesDownloadService の偽実装。実際の Addressables を呼ばず、テストから戻り値・失敗・完了のタイミング・バイト数を操作できる。
    /// 既定では、どの呼び出しもすぐ成功する（確認は更新なし、サイズは 0 件）。
    /// Service の処理の呼び出し（Check / Update / Size / StartDownload）は CallLog に、呼ばれた順に記録する。
    /// ReleaseDownloadHandle は、Dispose などでも呼ばれるため CallLog には入れず、ReleaseCallCount で数える。
    /// </summary>
    internal sealed class FakeAddressablesDownloadService : IAddressablesDownloadService
    {
        /// <summary> 1つの呼び出し（確認・更新・サイズ計算・ダウンロード）の振る舞い </summary>
        internal sealed class Step
        {
            /// <summary> 呼ばれた回数 </summary>
            internal int CallCount { get; set; }

            /// <summary> 設定すると、呼び出しは Complete / Fail が呼ばれるまで終わらない（手動で完了させる）。1回の呼び出しで使い切る </summary>
            internal TaskCompletionSource<bool> Hold { get; set; }

            /// <summary> 失敗させる例外 </summary>
            internal Exception Failure { get; set; }

            /// <summary> 失敗させる残りの回数。呼ばれるたびに 1 減り、0 になると成功する </summary>
            internal int FailuresRemaining { get; set; }

            /// <summary> 次の呼び出しを、手動で完了させるために止める </summary>
            internal TaskCompletionSource<bool> HoldNext() => Hold = new TaskCompletionSource<bool>();

            // 呼び出しの共通の処理。止められていれば待ち、失敗が残っていれば例外を投げる。UniTask 版・Task 版のどちらの async メソッドからも await できるよう、常に Task にする
            internal async Task RunAsync()
            {
                CallCount++;
                var hold = Hold;
                Hold = null;
                if (hold != null)
                    await hold.Task;

                if (FailuresRemaining > 0)
                {
                    FailuresRemaining--;
                    throw Failure ?? new Exception("偽の失敗");
                }
            }
        }

        private readonly List<string> _callLog = new();

        /// <summary> 処理の呼び出しの記録（Check / Update / Size / StartDownload の順序） </summary>
        internal IReadOnlyList<string> CallLog => _callLog;

        internal Step Check { get; } = new();
        internal Step Update { get; } = new();
        internal Step Size { get; } = new();
        internal Step Download { get; } = new();

        /// <summary> CheckForCatalogUpdatesAsync が返す、更新のあるカタログの ID </summary>
        internal List<string> CatalogIds { get; } = new();

        /// <summary> CalculateDownloadSizeAsync が返す値 </summary>
        internal DownloadSizeInfo SizeInfo { get; set; } = new DownloadSizeInfo(0, 0);

        /// <summary> GetDownloadBytes が返すバイト数 </summary>
        internal long DownloadedBytes { get; set; }
        internal long TotalBytes { get; set; }

        /// <summary> UpdateCatalogsAsync に最後に渡されたカタログ ID・autoCleanBundleCache </summary>
        internal IReadOnlyList<string> LastUpdatedCatalogIds { get; private set; }
        internal bool LastAutoClean { get; private set; }

        /// <summary> 最後に渡された Label </summary>
        internal IReadOnlyList<string> LastSizeLabels { get; private set; }
        internal IReadOnlyList<string> LastDownloadLabels { get; private set; }

        /// <summary> ReleaseDownloadHandle が呼ばれた回数 </summary>
        internal int ReleaseCallCount { get; private set; }

#if ADDRESSABLE_UNITASK_SUPPORT
        public async UniTask<IReadOnlyList<string>> CheckForCatalogUpdatesAsync()
#else
        public async Task<IReadOnlyList<string>> CheckForCatalogUpdatesAsync()
#endif
        {
            _callLog.Add("Check");
            await Check.RunAsync();
            return new List<string>(CatalogIds);
        }

#if ADDRESSABLE_UNITASK_SUPPORT
        public async UniTask UpdateCatalogsAsync(IReadOnlyList<string> catalog_ids, bool auto_clean_bundle_cache)
#else
        public async Task UpdateCatalogsAsync(IReadOnlyList<string> catalog_ids, bool auto_clean_bundle_cache)
#endif
        {
            _callLog.Add("Update");
            LastUpdatedCatalogIds = catalog_ids;
            LastAutoClean = auto_clean_bundle_cache;
            await Update.RunAsync();
        }

#if ADDRESSABLE_UNITASK_SUPPORT
        public async UniTask<DownloadSizeInfo> CalculateDownloadSizeAsync(IReadOnlyList<string> labels)
#else
        public async Task<DownloadSizeInfo> CalculateDownloadSizeAsync(IReadOnlyList<string> labels)
#endif
        {
            _callLog.Add("Size");
            LastSizeLabels = labels;
            await Size.RunAsync();
            return SizeInfo;
        }

#if ADDRESSABLE_UNITASK_SUPPORT
        public async UniTask StartDownloadAsync(IReadOnlyList<string> labels)
#else
        public async Task StartDownloadAsync(IReadOnlyList<string> labels)
#endif
        {
            _callLog.Add("StartDownload");
            LastDownloadLabels = labels;
            await Download.RunAsync();
        }

        public void GetDownloadBytes(out long downloaded_bytes, out long total_bytes)
        {
            downloaded_bytes = DownloadedBytes;
            total_bytes = TotalBytes;
        }

        public void ReleaseDownloadHandle() => ReleaseCallCount++;

        /// <summary> ReleaseDownloadHandle の呼び出し回数を 0 に戻す（テスト中に作り直した Controller の Dispose を、数えないため） </summary>
        internal void ResetReleaseCallCount() => ReleaseCallCount = 0;
    }
}
