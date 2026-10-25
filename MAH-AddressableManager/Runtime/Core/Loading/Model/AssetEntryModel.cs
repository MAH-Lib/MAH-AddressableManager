using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MAH.AddressableManager;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// 1件分の状態（Worker, LoadTask, RefCount, CountByScope, AssetType）を持つエントリ。判定キーは (AssetKey, 型) の AssetEntryKey。
    /// public フィールドは使わず、Acc 付きのアクセサで公開する。
    /// </summary>
    internal sealed class AssetEntryModel
    {
        private readonly Dictionary<LifetimeScopeId, int> _countByScope = new();
        private readonly AssetEntryKey _entryKey;
        private readonly IAssetLoaderWorker _worker;
        // 複数の呼び出し元（重複ロード時のawait元・DisposeWorkerWhenCompleted）が同時にawaitするため、
        // 安全性を優先し、UniTask使用時も内部の共有ロードタスクは常にTaskで持つ。
        // Entryの存続中は完了後にも後から来た呼び出し元がawaitするため、何度awaitしても安全なTaskを使う
        // （UniTaskは完了後に内部オブジェクトがプールへ戻り、2回目のawaitが例外や別処理の状態の読み取りになり得る。設計仕様書3.5.1）
        private Task _loadTask;
        private int _refCount;

        /// <summary> Registry の判定キー。(AssetKey, 型) の組 </summary>
        internal AssetEntryKey AccEntryKey => _entryKey;
        internal AssetKey AccKey => _entryKey.AccKey;
        internal IAssetLoaderWorker AccWorker => _worker;
        /// <summary> このエントリがロードした型。判定キー AssetEntryKey の一部 </summary>
        internal Type AccAssetType => _entryKey.AccAssetType;
        internal Task AccLoadTask => _loadTask;
        internal int AccRefCount => _refCount;

        internal AssetEntryModel(AssetEntryKey entry_key, IAssetLoaderWorker worker)
        {
            _entryKey = entry_key;
            _worker = worker;
        }

        /// <summary> 実ロードを1回だけ起動する。失敗したら on_failed を呼んでから例外を再送出する </summary>
        internal void StartLoad(Action<AssetEntryModel> on_failed)
        {
            _loadTask = LoadInternalAsync(on_failed);
        }

        // Worker.LoadAsync はシンボルに応じて UniTask/Task を返すが、
        // async Task の内部からどちらもそのまま await できるためここでの分岐は不要
        private async Task LoadInternalAsync(Action<AssetEntryModel> on_failed)
        {
            try
            {
                await _worker.LoadAsync(CancellationToken.None); // 実ロードは呼び出し元のctでは止めない
            }
            catch (Exception)
            {
                on_failed?.Invoke(this);
                throw; // AccLoadTask を待っている全員に同じ例外を伝える
            }
        }

        /// <summary> await前に加算する。寿命スコープごとの確保数も同時に記録する </summary>
        internal void AddCount(LifetimeScopeId scope_id)
        {
            _refCount++;
            _countByScope.TryGetValue(scope_id, out var count);
            _countByScope[scope_id] = count + 1;
        }

        /// <summary> 指定スコープの確保を1つ減算する。既に0（未確保）なら二重解放としてfalseを返す </summary>
        internal bool TryDecrementScope(LifetimeScopeId scope_id)
        {
            if (!_countByScope.TryGetValue(scope_id, out var count) || count <= 0)
                return false;

            if (count == 1)
                _countByScope.Remove(scope_id);
            else
                _countByScope[scope_id] = count - 1;

            _refCount--;
            return true;
        }

        /// <summary> 指定スコープからの確保が残っているか </summary>
        internal bool ContainsScope(LifetimeScopeId scope_id) => _countByScope.ContainsKey(scope_id);

#if UNITY_EDITOR
        // 2026-10-14 Editor のデバッグウィンドウ用。呼ぶのは AssetRegistryModel.CopyEntriesTo のみ
        /// <summary> 寿命スコープごとの確保数を、scope_counts の末尾へ追記する（Clear しない。複数 Entry 分を連結するため） </summary>
        internal void CopyScopeCountsTo(List<ScopeCountSnapshot> scope_counts)
        {
            foreach (var pair in _countByScope)
                scope_counts.Add(new ScopeCountSnapshot(pair.Key.AccKind, pair.Key.AccValue, pair.Value));
        }
#endif

        /// <summary> ロード中ならロード完了を待ってから Worker を解放する（完了前の handle は解放しない） </summary>
        internal async void DisposeWorkerWhenCompleted()
        {
            try
            {
                await _loadTask;
            }
            catch
            {
                // 失敗自体は呼び出し元へ既に伝わっているため、ここでは握りつぶして良い
            }
            _worker.Dispose();
        }
    }
}
