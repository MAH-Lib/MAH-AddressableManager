#if UNITY_EDITOR
using System;
using MAH.AddressableManager;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// 発行済みで、まだ解放されていないチケット1件分の、取得した時点の値のコピー。
    /// Entry との対応は、AssetKey・型・スコープの一致で Editor 側が付ける（internal の AssetEntryKey を Editor に見せないため）。
    /// </summary>
    internal readonly struct IssuedTicketSnapshot
    {
        private readonly long _serial;
        private readonly AssetKey _key;
        private readonly Type _assetType;
        private readonly LifetimeKind _scopeKind;
        private readonly int _scopeValue;
        private readonly bool _isDelivered;
        private readonly bool _isGroup;
        private readonly string _stackTrace;

        internal long AccSerial => _serial;
        internal AssetKey AccKey => _key;
        internal Type AccAssetType => _assetType;
        internal LifetimeKind AccScopeKind => _scopeKind;
        /// <summary> スコープの値。Owner は GameObject の InstanceID、Scene はシーンの handle、Manual は 0 </summary>
        internal int AccScopeValue => _scopeValue;
        /// <summary> 呼び出し元にチケット（結果）を返し終えたか </summary>
        internal bool AccIsDelivered => _isDelivered;
        /// <summary> Label 一括ロード由来か（まとめチケットに属するか） </summary>
        internal bool AccIsGroup => _isGroup;
        /// <summary> 確保時のスタックトレース </summary>
        internal string AccStackTrace => _stackTrace;

        internal IssuedTicketSnapshot(
            long serial,
            AssetKey key,
            Type asset_type,
            LifetimeKind scope_kind,
            int scope_value,
            bool is_delivered,
            bool is_group,
            string stack_trace)
        {
            _serial = serial;
            _key = key;
            _assetType = asset_type;
            _scopeKind = scope_kind;
            _scopeValue = scope_value;
            _isDelivered = is_delivered;
            _isGroup = is_group;
            _stackTrace = stack_trace;
        }
    }
}
#endif
