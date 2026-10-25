#if UNITY_EDITOR
using System;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// Owner 寿命の回収を（回収経路・Owner 名）ごとに集約した1行分の、取得した時点の値のコピー。
    /// Owner 名は、Owner を監視に登録したときに記録した文字列（回収時には GameObject が破棄済みで、名前を引けないことがあるため）。
    /// </summary>
    internal readonly struct OwnerReclaimSnapshot
    {
        private readonly int _id;
        private readonly long _sequence;
        private readonly OwnerReclaimRoute _route;
        private readonly string _ownerName;
        private readonly int _lastInstanceId;
        private readonly int _count;
        private readonly int _ticketCount;
        private readonly DateTime _lastTime;

        /// <summary> 集約した行の ID。最初の発生で採番し、行が増えるまで変わらない </summary>
        internal int AccId => _id;
        /// <summary> 最後に発生したときの通し番号。Model 全体で単調に増える </summary>
        internal long AccSequence => _sequence;
        internal OwnerReclaimRoute AccRoute => _route;
        /// <summary> 登録時の Owner の GameObject 名。取れなかったときは空文字 </summary>
        internal string AccOwnerName => _ownerName;
        /// <summary> 最後に回収された Owner の InstanceID </summary>
        internal int AccLastInstanceId => _lastInstanceId;
        /// <summary> 回収の回数 </summary>
        internal int AccCount => _count;
        /// <summary> 回収したチケット数の累計（回収前に残っていた件数の合計） </summary>
        internal int AccTicketCount => _ticketCount;
        /// <summary> 最後に発生した時刻 </summary>
        internal DateTime AccLastTime => _lastTime;

        internal OwnerReclaimSnapshot(
            int id,
            long sequence,
            OwnerReclaimRoute route,
            string owner_name,
            int last_instance_id,
            int count,
            int ticket_count,
            DateTime last_time)
        {
            _id = id;
            _sequence = sequence;
            _route = route;
            _ownerName = owner_name;
            _lastInstanceId = last_instance_id;
            _count = count;
            _ticketCount = ticket_count;
            _lastTime = last_time;
        }
    }
}
#endif
