#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// Owner 寿命の回収を、回収経路（OnDestroy / ポーリング）と Owner 名ごとに集約して溜める Model。Editor 限定。
    /// 破棄検知の多重の安全網が期待通り働いているかを、デバッグウィンドウの「Owner」タブで確かめるために使う。
    /// 同じ経路・同じ名前の回収は1行にまとまるため、件数は Owner 名の種類で頭打ちになる（上限は設けない）。
    /// 書き込むのは LoadingController だけで、Window は窓口（AddressableDebugReader）越しに Snapshot を読む。
    /// </summary>
    internal sealed class OwnerReclaimRegistryModel
    {
        private readonly Dictionary<ReclaimKey, ReclaimRecord> _reclaims = new();
        private int _nextId = 1;
        private long _sequence;

        /// <summary> 集約の判定キー。InstanceID は含めない（同じ Prefab を生成・破棄するたびに ID が変わり、集約の意味がなくなるため） </summary>
        private readonly struct ReclaimKey : IEquatable<ReclaimKey>
        {
            private readonly OwnerReclaimRoute _route;
            private readonly string _ownerName;

            internal ReclaimKey(OwnerReclaimRoute route, string owner_name)
            {
                _route = route;
                _ownerName = owner_name;
            }

            public bool Equals(ReclaimKey other)
                => _route == other._route && string.Equals(_ownerName, other._ownerName, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is ReclaimKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(_route, _ownerName);
        }

        /// <summary> 集約した1行分の可変の状態。最初の発生で作り、同じ経路・名前の発生では更新する </summary>
        private sealed class ReclaimRecord
        {
            // 最初の発生で決まり、以降は変わらない値
            private readonly int _id;
            private readonly OwnerReclaimRoute _route;
            private readonly string _ownerName;
            // 発生のたびに更新する値
            private long _sequence;
            private int _lastInstanceId;
            private int _count;
            private int _ticketCount;
            private DateTime _lastTime;

            internal ReclaimRecord(int id, OwnerReclaimRoute route, string owner_name)
            {
                _id = id;
                _route = route;
                _ownerName = owner_name;
            }

            /// <summary> 発生を1回分記録する。回数とチケット累計を加算し、最後に発生した分の情報で上書きする </summary>
            internal void RecordOccurrence(long sequence, int instance_id, int ticket_count, DateTime now)
            {
                _sequence = sequence;
                _lastInstanceId = instance_id;
                _count++;
                _ticketCount += ticket_count;
                _lastTime = now;
            }

            internal OwnerReclaimSnapshot CreateSnapshot()
                => new OwnerReclaimSnapshot(_id, _sequence, _route, _ownerName, _lastInstanceId, _count, _ticketCount, _lastTime);
        }

        /// <summary>
        /// 回収を1回分記録する。owner_name は登録時の名前（取れなかったときは null または空文字）、
        /// ticket_count は回収前に残っていたチケット数。
        /// </summary>
        internal void Record(OwnerReclaimRoute route, int instance_id, string owner_name, int ticket_count)
        {
            owner_name ??= string.Empty;
            var key = new ReclaimKey(route, owner_name);
            if (!_reclaims.TryGetValue(key, out var record))
            {
                record = new ReclaimRecord(_nextId++, route, owner_name);
                _reclaims[key] = record;
            }

            record.RecordOccurrence(++_sequence, instance_id, ticket_count, DateTime.Now);
        }

        // 呼ぶのは AddressableDebugReader のみ
        /// <summary> 集約した行を reclaims へ書き出す（Clear してから）。並びは発生の順とは限らない（Window 側で並べ替える） </summary>
        internal void CopyReclaimsTo(List<OwnerReclaimSnapshot> reclaims)
        {
            reclaims.Clear();
            foreach (var pair in _reclaims) // Dictionary は具体型のまま foreach する（ボックス化を避ける）
                reclaims.Add(pair.Value.CreateSnapshot());
        }

        internal void Clear() => _reclaims.Clear();
    }
}
#endif
