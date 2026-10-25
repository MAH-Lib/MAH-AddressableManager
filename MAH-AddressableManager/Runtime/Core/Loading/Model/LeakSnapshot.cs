#if UNITY_EDITOR
using System;
using MAH.AddressableManager;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// 確保元ごとに集約したリーク1行分の、取得した時点の値のコピー。
    /// リーク＝明示 Release されずに寿命回収された、呼び出し元にチケットを返し終えたチケット。
    /// 同じ確保元（単体：AssetKey・型・寿命の種類・スタックトレース／Label 一括：Label・寿命の種類・スタックトレース）の発生は1行にまとまり、回数が加算される。
    /// スコープの名前は、発生時に Runtime が記録した文字列（寿命が終わった後は Editor 側で引けないため）。
    /// </summary>
    internal readonly struct LeakSnapshot
    {
        private readonly int _id;
        private readonly long _sequence;
        private readonly bool _isGroup;
        private readonly AssetKey _key;
        private readonly Type _assetType;
        private readonly string _label;
        private readonly int _groupCount;
        private readonly LifetimeKind _scopeKind;
        private readonly int _scopeValue;
        private readonly string _scopeName;
        private readonly int _count;
        private readonly DateTime _lastTime;
        private readonly string _stackTrace;

        /// <summary> 集約した行の ID。最初の発生で採番し、行が増えるまで変わらない（クリア時点の回数を覚えるための識別に使う） </summary>
        internal int AccId => _id;
        /// <summary> 最後に発生したときの通し番号。Model 全体で単調に増える </summary>
        internal long AccSequence => _sequence;
        /// <summary> Label 一括ロードのまとめチケットか（false は単体の Load） </summary>
        internal bool AccIsGroup => _isGroup;
        /// <summary> 単体のときのキー。Label 一括では default </summary>
        internal AssetKey AccKey => _key;
        /// <summary> 単体のときのアセットの型。Label 一括では null </summary>
        internal Type AccAssetType => _assetType;
        /// <summary> Label 一括のときの Label。単体では空文字 </summary>
        internal string AccLabel => _label;
        /// <summary> Label 一括のときの、まとめチケットの件数（最後に発生した分）。単体では 0 </summary>
        internal int AccGroupCount => _groupCount;
        /// <summary> 寿命の種類（Scene / Owner） </summary>
        internal LifetimeKind AccScopeKind => _scopeKind;
        /// <summary> 最後に発生したスコープの値。Owner は GameObject の InstanceID、Scene はシーンの handle </summary>
        internal int AccScopeValue => _scopeValue;
        /// <summary> 最後に発生したスコープの名前（Owner は GameObject 名、Scene はシーン名）。取れなかったときは空文字 </summary>
        internal string AccScopeName => _scopeName;
        /// <summary> 同じ確保元のリークの発生回数 </summary>
        internal int AccCount => _count;
        /// <summary> 最後に発生した時刻 </summary>
        internal DateTime AccLastTime => _lastTime;
        /// <summary> 確保時のスタックトレース </summary>
        internal string AccStackTrace => _stackTrace;

        internal LeakSnapshot(
            int id,
            long sequence,
            bool is_group,
            AssetKey key,
            Type asset_type,
            string label,
            int group_count,
            LifetimeKind scope_kind,
            int scope_value,
            string scope_name,
            int count,
            DateTime last_time,
            string stack_trace)
        {
            _id = id;
            _sequence = sequence;
            _isGroup = is_group;
            _key = key;
            _assetType = asset_type;
            _label = label;
            _groupCount = group_count;
            _scopeKind = scope_kind;
            _scopeValue = scope_value;
            _scopeName = scope_name;
            _count = count;
            _lastTime = last_time;
            _stackTrace = stack_trace;
        }
    }
}
#endif
