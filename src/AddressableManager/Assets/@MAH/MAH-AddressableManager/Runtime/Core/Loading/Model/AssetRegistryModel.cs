using System.Collections.Generic;
using MAH.AddressableManager;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// AssetEntryModel の集合（Dictionary のラッパー）。Editor 拡張が読み取り専用で参照する先になるため
    /// 最初から LoadingController から分割している。判定キーは (AssetKey, 型) の AssetEntryKey。
    /// </summary>
    internal sealed class AssetRegistryModel
    {
        private readonly Dictionary<AssetEntryKey, AssetEntryModel> _entries = new();

        internal bool TryGetEntry(AssetEntryKey entry_key, out AssetEntryModel entry) => _entries.TryGetValue(entry_key, out entry);

        internal AssetEntryModel AddEntry(AssetEntryKey entry_key, IAssetLoaderWorker worker)
        {
            var entry = new AssetEntryModel(entry_key, worker);
            _entries[entry_key] = entry;
            return entry;
        }

        /// <summary>
        /// Registry にあるエントリが引数と同じ場合だけ取り除き、取り除けたら true を返す。
        /// ロード失敗や解放のときに、同じキーで新しく作られた別のエントリを誤って消さないための判定。
        /// </summary>
        internal bool RemoveEntryIfSame(AssetEntryKey entry_key, AssetEntryModel entry)
        {
            if (!_entries.TryGetValue(entry_key, out var registered) || !ReferenceEquals(registered, entry))
                return false;
            _entries.Remove(entry_key);
            return true;
        }

#if UNITY_EDITOR
        // 2026-10-14 Editor のデバッグウィンドウ用。呼ぶのは AddressableDebugReader のみ
        /// <summary>
        /// 全 Entry を entries へ、寿命スコープごとの確保内訳を scope_counts へ書き出す（どちらも Clear してから）。
        /// 各 Entry の内訳は、scope_counts の中で連続して並び、Entry の Snapshot が開始位置と件数を持つ。
        /// </summary>
        internal void CopyEntriesTo(List<AssetEntrySnapshot> entries, List<ScopeCountSnapshot> scope_counts)
        {
            entries.Clear();
            scope_counts.Clear();
            foreach (var pair in _entries) // Dictionary は具体型のまま foreach する（ボックス化を避ける）
            {
                var entry = pair.Value;
                var start = scope_counts.Count;
                entry.CopyScopeCountsTo(scope_counts);
                // 名前は値（文字列）でコピーする。ロード完了前は null、破棄済みなら偽 null のため、どちらも空文字にする
                var asset = entry.AccWorker.AccAsset;
                entries.Add(new AssetEntrySnapshot(
                    entry.AccKey,
                    entry.AccAssetType,
                    asset != null ? asset.name : string.Empty,
                    entry.AccRefCount,
                    entry.AccWorker.AccStatus,
                    start,
                    scope_counts.Count - start));
            }
        }
#endif

        /// <summary> 全エントリの Worker を即座に Dispose し、Registry を空にする（Manager.Dispose 用の強制解放） </summary>
        internal void DisposeAllAndClear()
        {
            foreach (var entry in _entries.Values)
                entry.AccWorker.Dispose();
            _entries.Clear();
        }
    }
}
