using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
#if ADDRESSABLE_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#endif

namespace MAH.AddressableManager.Core.Download
{
    /// <summary>
    /// Addressables のカタログ確認・更新・サイズ計算・ダウンロードを呼ぶ Service。Addressables の API を呼ぶのは、この Service だけ。
    /// handle は必ず解放する（確認・更新・サイズ計算は結果を読んだ直後に、ダウンロードは完了時または ReleaseDownloadHandle で）。
    /// handle の完了待ちは、UniTask 使用時も、TaskCompletionSource で作った Task を await する
    /// （AsyncCompat.ToTask は UniTask 版では未定義で、非ジェネリックの handle には無いため、AddressablesInitializeService と同じ形で自前で変換する）。
    /// ダウンロードは同時に1つだけ。ダウンロード中の handle と、最後に読んだバイト数を保持する。
    /// </summary>
    internal sealed class AddressablesDownloadService : IAddressablesDownloadService
    {
        private AsyncOperationHandle _downloadHandle;
        private long _lastDownloadedBytes;
        private long _lastTotalBytes;

#if ADDRESSABLE_UNITASK_SUPPORT
        public async UniTask<IReadOnlyList<string>> CheckForCatalogUpdatesAsync()
#else
        public async Task<IReadOnlyList<string>> CheckForCatalogUpdatesAsync()
#endif
        {
            var handle = Addressables.CheckForCatalogUpdates(false);
            try
            {
                await WaitAsync(handle);

                // 一部のカタログの確認が失敗すると、結果に null が入る。更新のあるカタログの ID だけを返す
                var catalog_ids = new List<string>();
                var found = handle.Result;
                if (found != null)
                {
                    foreach (var catalog_id in found)
                    {
                        if (!string.IsNullOrEmpty(catalog_id))
                            catalog_ids.Add(catalog_id);
                    }
                }
                return catalog_ids;
            }
            finally
            {
                ReleaseHandle(handle);
            }
        }

#if ADDRESSABLE_UNITASK_SUPPORT
        public async UniTask UpdateCatalogsAsync(IReadOnlyList<string> catalog_ids, bool auto_clean_bundle_cache)
#else
        public async Task UpdateCatalogsAsync(IReadOnlyList<string> catalog_ids, bool auto_clean_bundle_cache)
#endif
        {
            if (catalog_ids == null || catalog_ids.Count == 0)
                throw new ArgumentException("更新するカタログの ID を明示してください（引数なしの更新は、別の確認で立ったフラグの分まで更新するため）。", nameof(catalog_ids));

            var handle = Addressables.UpdateCatalogs(auto_clean_bundle_cache, catalog_ids, false);
            try
            {
                await WaitAsync(handle);
            }
            finally
            {
                ReleaseHandle(handle);
            }
        }

#if ADDRESSABLE_UNITASK_SUPPORT
        public async UniTask<DownloadSizeInfo> CalculateDownloadSizeAsync(IReadOnlyList<string> labels)
#else
        public async Task<DownloadSizeInfo> CalculateDownloadSizeAsync(IReadOnlyList<string> labels)
#endif
        {
            ThrowIfNoLabels(labels);

            var handle = Addressables.LoadResourceLocationsAsync(labels, Addressables.MergeMode.Union);
            try
            {
                await WaitAsync(handle);

                var locations = handle.Result;
                if (locations == null || locations.Count == 0)
                {
                    AddressableLogger.LogWarning($"Label「{string.Join(", ", labels)}」に該当するアセットがありません。ダウンロードするものは 0 件として扱います。");
                    return new DownloadSizeInfo(0, 0);
                }

                // Addressables.GetDownloadSizeAsync と同じ判定：各ロケーションの依存を、重複なく集めて、サイズを計算する。
                // GetDownloadSizeAsync は合計バイトしか返さないため、サイズが 0 より大きい（キャッシュに無い）バンドルの数もあわせて数える
                var bundles = new HashSet<IResourceLocation>(new ResourceLocationComparer());
                var bundle_count = 0;
                long total_bytes = 0;
                foreach (var location in locations)
                {
                    if (!location.HasDependencies) continue;

                    foreach (var dependency in location.Dependencies)
                    {
                        if (!bundles.Add(dependency)) continue;

                        var size_data = dependency.Data as ILocationSizeData;
                        if (size_data == null) continue;

                        var size = size_data.ComputeSize(dependency, Addressables.ResourceManager);
                        if (size <= 0) continue;

                        bundle_count++;
                        total_bytes += size;
                    }
                }
                return new DownloadSizeInfo(bundle_count, total_bytes);
            }
            finally
            {
                ReleaseHandle(handle);
            }
        }

#if ADDRESSABLE_UNITASK_SUPPORT
        public async UniTask StartDownloadAsync(IReadOnlyList<string> labels)
#else
        public async Task StartDownloadAsync(IReadOnlyList<string> labels)
#endif
        {
            ThrowIfNoLabels(labels);
            if (_downloadHandle.IsValid())
                throw new InvalidOperationException("ダウンロードはすでに実行中です。同時に始められるのは1つだけです。");

            _lastDownloadedBytes = 0;
            _lastTotalBytes = 0;

            // autoReleaseHandle は false：Tick でバイト数を読めるよう、完了するまで handle を保持し、自分で解放する
            var handle = Addressables.DownloadDependenciesAsync(labels, Addressables.MergeMode.Union, false);
            _downloadHandle = handle;
            try
            {
                await WaitAsync(handle);

                // 完了後も、最後に読んだ値を合計と同じにしておく（Tick が完了の処理より先に走っても、値が戻らないようにするため）。
                // 待機をやめて ReleaseDownloadHandle 済みの古い呼び出しは、無効な handle を読まず、新しいダウンロードの値も書き換えない
                if (handle.IsValid() && _downloadHandle.Equals(handle))
                {
                    _lastTotalBytes = handle.GetDownloadStatus().TotalBytes;
                    _lastDownloadedBytes = _lastTotalBytes;
                }
            }
            finally
            {
                // 呼び出し元が待つのをやめて ReleaseDownloadHandle 済みの場合は、handle が無効のため何もしない
                ReleaseHandle(handle);
            }
        }

        public void GetDownloadBytes(out long downloaded_bytes, out long total_bytes)
        {
            if (_downloadHandle.IsValid())
            {
                var status = _downloadHandle.GetDownloadStatus();
                _lastDownloadedBytes = status.DownloadedBytes;
                _lastTotalBytes = status.TotalBytes;
            }
            downloaded_bytes = _lastDownloadedBytes;
            total_bytes = _lastTotalBytes;
        }

        public void ReleaseDownloadHandle() => ReleaseHandle(_downloadHandle);

        // 保持しているダウンロードの handle なら、保持も外す。待機をやめた古い呼び出しが後から再開しても、新しい handle を解放しないよう、
        // 渡された handle と一致するときだけ外す
        private void ReleaseHandle(AsyncOperationHandle handle)
        {
            if (handle.IsValid())
                Addressables.Release(handle);
            if (_downloadHandle.Equals(handle))
                _downloadHandle = default;
        }

        private static void ThrowIfNoLabels(IReadOnlyList<string> labels)
        {
            if (labels == null || labels.Count == 0)
                throw new ArgumentException("対象の Label が空です。", nameof(labels));
        }

        // handle の完了を Task に変える。結果は Completed の中で読み、失敗なら handle の OperationException で失敗した Task にする。
        // 呼んだ時点で完了済み（IsDone）なら、Completed を待たずにその場で読む。ラムダは確認・更新・サイズ計算・ダウンロードの呼び出しごとに1回だけで、毎フレームではない
        private static Task WaitAsync(AsyncOperationHandle handle)
        {
            var source = new TaskCompletionSource<bool>();
            if (handle.IsDone)
                Complete(source, handle);
            else
                handle.Completed += completed_handle => Complete(source, completed_handle);
            return source.Task;
        }

        private static void Complete(TaskCompletionSource<bool> source, AsyncOperationHandle handle)
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
                source.TrySetResult(true);
            else
                source.TrySetException(handle.OperationException ?? new Exception("Addressables の操作に失敗しました。"));
        }
    }
}
