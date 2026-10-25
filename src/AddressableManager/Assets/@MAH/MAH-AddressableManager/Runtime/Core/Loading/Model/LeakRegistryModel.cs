#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using MAH.AddressableManager;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// 明示 Release されずに寿命回収されたチケット（リーク）を、確保元ごとに集約して溜める Model。Editor 限定。
    /// ログは流れて見逃されるため、デバッグウィンドウの「リーク」タブで見返せるようにする。
    /// 同じ確保元の発生は1行にまとまるため、件数は確保元の数で頭打ちになる（上限は設けない）。
    /// 書き込むのは LoadingController だけで、Window は窓口（AddressableDebugReader）越しに Snapshot を読む。
    /// </summary>
    internal sealed class LeakRegistryModel
    {
        private readonly Dictionary<LeakKey, LeakRecord> _leaks = new();
        private int _nextId = 1;
        private long _sequence;

        /// <summary> 集約の判定キー。スコープの値（Owner の ID・Scene の handle）は含めない（別のスコープでも、同じ呼び出し元なら同じ行にまとめるため） </summary>
        private readonly struct LeakKey : IEquatable<LeakKey>
        {
            private readonly bool _isGroup;
            private readonly AssetKey _key;
            private readonly Type _assetType;
            private readonly string _label;
            private readonly LifetimeKind _scopeKind;
            private readonly string _stackTrace;

            internal LeakKey(bool is_group, AssetKey key, Type asset_type, string label, LifetimeKind scope_kind, string stack_trace)
            {
                _isGroup = is_group;
                _key = key;
                _assetType = asset_type;
                _label = label;
                _scopeKind = scope_kind;
                _stackTrace = stack_trace;
            }

            public bool Equals(LeakKey other)
                => _isGroup == other._isGroup
                   && _key == other._key
                   && _assetType == other._assetType
                   && _scopeKind == other._scopeKind
                   && string.Equals(_label, other._label, StringComparison.Ordinal)
                   && string.Equals(_stackTrace, other._stackTrace, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is LeakKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(_isGroup, _key, _assetType, _label, _scopeKind, _stackTrace);
        }

        /// <summary> 集約した1行分の可変の状態。最初の発生で作り、同じ確保元の発生では更新する </summary>
        private sealed class LeakRecord
        {
            // 最初の発生で決まり、以降は変わらない値
            private readonly int _id;
            private readonly bool _isGroup;
            private readonly AssetKey _key;
            private readonly Type _assetType;
            private readonly string _label;
            private readonly string _stackTrace;
            // 発生のたびに更新する値
            private long _sequence;
            private int _groupCount;
            private LifetimeKind _scopeKind;
            private int _scopeValue;
            private string _scopeName = string.Empty;
            private int _count;
            private DateTime _lastTime;

            internal LeakRecord(int id, bool is_group, AssetKey key, Type asset_type, string label, string stack_trace)
            {
                _id = id;
                _isGroup = is_group;
                _key = key;
                _assetType = asset_type;
                _label = label;
                _stackTrace = stack_trace;
            }

            /// <summary> 発生を1回分記録する。回数を加算し、最後に発生した分の情報で上書きする </summary>
            internal void RecordOccurrence(long sequence, int group_count, LifetimeScopeId scope_id, string scope_name, DateTime now)
            {
                _sequence = sequence;
                _groupCount = group_count;
                _scopeKind = scope_id.AccKind;
                _scopeValue = scope_id.AccValue;
                _scopeName = scope_name ?? string.Empty;
                _count++;
                _lastTime = now;
            }

            internal LeakSnapshot CreateSnapshot()
                => new LeakSnapshot(
                    _id, _sequence, _isGroup, _key, _assetType, _label, _groupCount,
                    _scopeKind, _scopeValue, _scopeName, _count, _lastTime, _stackTrace);
        }

        /// <summary> 単体の Load のリークを記録する。scope_name はスコープの名前（取れなかったときは null または空文字） </summary>
        internal void RecordTicketLeak(AssetKey key, Type asset_type, LifetimeScopeId scope_id, string scope_name, string stack_trace)
        {
            var leak_key = new LeakKey(false, key, asset_type, string.Empty, scope_id.AccKind, stack_trace);
            Record(leak_key, false, key, asset_type, string.Empty, 0, scope_id, scope_name, stack_trace);
        }

        /// <summary> Label 一括ロードのまとめチケットのリークを、1枚につき1回記録する。count は、まとめチケットの中身の件数 </summary>
        internal void RecordGroupLeak(string label, int count, LifetimeScopeId scope_id, string scope_name, string stack_trace)
        {
            label ??= string.Empty;
            var leak_key = new LeakKey(true, default, null, label, scope_id.AccKind, stack_trace);
            Record(leak_key, true, default, null, label, count, scope_id, scope_name, stack_trace);
        }

        private void Record(
            LeakKey leak_key, bool is_group, AssetKey key, Type asset_type, string label, int group_count,
            LifetimeScopeId scope_id, string scope_name, string stack_trace)
        {
            if (!_leaks.TryGetValue(leak_key, out var record))
            {
                record = new LeakRecord(_nextId++, is_group, key, asset_type, label, stack_trace);
                _leaks[leak_key] = record;
            }

            // 同じ確保元の発生では、回数を加算し、最後に発生した分の情報で上書きする
            record.RecordOccurrence(++_sequence, group_count, scope_id, scope_name, DateTime.Now);
        }

        // 呼ぶのは AddressableDebugReader のみ
        /// <summary> 集約した行を leaks へ書き出す（Clear してから）。並びは発生の順とは限らない（Window 側で並べ替える） </summary>
        internal void CopyLeaksTo(List<LeakSnapshot> leaks)
        {
            leaks.Clear();
            foreach (var pair in _leaks) // Dictionary は具体型のまま foreach する（ボックス化を避ける）
                leaks.Add(pair.Value.CreateSnapshot());
        }

        internal void Clear() => _leaks.Clear();
    }
}
#endif
