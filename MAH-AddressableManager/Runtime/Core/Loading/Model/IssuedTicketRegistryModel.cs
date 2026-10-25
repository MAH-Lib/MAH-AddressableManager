using System.Collections.Generic;
using MAH.AddressableManager;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// 発行済みで、まだ解放されていないチケットの状態データを保持するModel。
    /// 寿命スコープ単位の索引（_ticketsByScope）も合わせて持つ（ReleaseLifetimeがkeyではなくスコープ起点で探すため）。
    /// </summary>
    internal sealed class IssuedTicketRegistryModel
    {
        private readonly Dictionary<long, IssuedTicketRecord> _issuedTickets = new();
        private readonly Dictionary<LifetimeScopeId, List<long>> _ticketsByScope = new();
        private readonly List<long> _scopeSnapshotBuffer = new(); // SnapshotSerialsForScope の再利用バッファ
        private long _nextSerial = 1; // 0 は default のチケット用に空けておく

        internal long IssueSerial() => _nextSerial++;

        internal bool Contains(long serial) => _issuedTickets.ContainsKey(serial);

        /// <summary> チケットを発行済みとして登録する。group は Label 一括ロード由来のときだけ渡す（単体の Load では null） </summary>
        internal void Register(long serial, AssetEntryKey entry_key, AssetEntryModel entry, LifetimeScopeId scope_id, AssetGroupTicket group)
        {
            _issuedTickets[serial] = new IssuedTicketRecord(entry_key, entry, scope_id, group);

            if (!_ticketsByScope.TryGetValue(scope_id, out var serials))
            {
                serials = new List<long>();
                _ticketsByScope[scope_id] = serials;
            }
            serials.Add(serial);
        }

        /// <summary> 呼び出し元にチケットを返し終えた印を立てる。未登録（すでに回収・解放済み）のシリアルは何もしない </summary>
        internal void MarkDelivered(long serial)
        {
            if (_issuedTickets.TryGetValue(serial, out var record))
                _issuedTickets[serial] = record.AsDelivered();
        }

        /// <summary> 指定シリアルのチケットを取り除く。そのスコープのチケットが尽きたら scope_exhausted に true を返す </summary>
        internal bool TryRemove(long serial, out IssuedTicketRecord record, out bool scope_exhausted)
        {
            scope_exhausted = false;
            if (!_issuedTickets.TryGetValue(serial, out record))
                return false;
            _issuedTickets.Remove(serial);

            if (_ticketsByScope.TryGetValue(record.AccScopeId, out var serials))
            {
                serials.Remove(serial);
                if (serials.Count == 0)
                {
                    _ticketsByScope.Remove(record.AccScopeId);
                    scope_exhausted = true;
                }
            }
            return true;
        }

        /// <summary> 指定スコープに残っているチケットのシリアル一覧（再利用バッファのスナップショット）。列挙中にTryRemoveで本体を変更しても安全 </summary>
        internal IReadOnlyList<long> SnapshotSerialsForScope(LifetimeScopeId scope_id)
        {
            _scopeSnapshotBuffer.Clear();
            if (_ticketsByScope.TryGetValue(scope_id, out var serials))
                _scopeSnapshotBuffer.AddRange(serials);
            return _scopeSnapshotBuffer;
        }

#if UNITY_EDITOR
        // Owner の回収の記録用。呼ぶのは LoadingController のみ
        /// <summary> 指定スコープに残っているチケットの件数。索引の件数を返すだけで、SnapshotSerialsForScope と違い再利用バッファに触れない </summary>
        internal int CountForScope(LifetimeScopeId scope_id)
            => _ticketsByScope.TryGetValue(scope_id, out var serials) ? serials.Count : 0;

        // 2026-10-14 Editor のデバッグウィンドウ用。呼ぶのは AddressableDebugReader のみ
        /// <summary> 発行済みのチケットを tickets へ書き出す（Clear してから）。並びはシリアルの順とは限らない </summary>
        internal void CopyTicketsTo(List<IssuedTicketSnapshot> tickets)
        {
            tickets.Clear();
            foreach (var pair in _issuedTickets) // Dictionary は具体型のまま foreach する（ボックス化を避ける）
            {
                var record = pair.Value;
                tickets.Add(new IssuedTicketSnapshot(
                    pair.Key,
                    record.AccKey,
                    record.AccEntryKey.AccAssetType,
                    record.AccScopeId.AccKind,
                    record.AccScopeId.AccValue,
                    record.AccIsDelivered,
                    record.AccGroup != null,
                    record.AccStackTrace));
            }
        }
#endif

        internal void Clear()
        {
            _issuedTickets.Clear();
            _ticketsByScope.Clear();
        }
    }
}
