using System;
using MAH.AddressableManager;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// 発行済みで、まだ解放されていないチケット1件分の記録。IssuedTicketRegistryModel の値として使う。
    /// 解放時に起点にするエントリへの参照を持つ（Registry を引き直さない）。Label 一括ロード由来の分は、まとめチケットへの参照も持つ。
    /// AccStackTrace は #if UNITY_EDITOR でのみ記録する（取得コストと文字列生成のGCアロケーションが大きいため本番ビルドには含めない）。
    /// </summary>
    internal readonly struct IssuedTicketRecord
    {
        private readonly AssetEntryKey _entryKey;
        private readonly AssetEntryModel _entry;
        private readonly LifetimeScopeId _scopeId;
        private readonly AssetGroupTicket _group;
        private readonly bool _isDelivered;
#if UNITY_EDITOR
        private readonly string _stackTrace;
#endif

        internal AssetKey AccKey => _entryKey.AccKey;
        /// <summary> Registry の判定キー（AssetKey と型の組） </summary>
        internal AssetEntryKey AccEntryKey => _entryKey;
        /// <summary> 解放時に起点にするエントリ。Registry を引き直さないことで、別のエントリを誤って減らさない </summary>
        internal AssetEntryModel AccEntry => _entry;
        internal LifetimeScopeId AccScopeId => _scopeId;
        /// <summary> Label 一括ロード由来のときだけ、まとめチケット。単体の Load では null </summary>
        internal AssetGroupTicket AccGroup => _group;
        /// <summary>
        /// 呼び出し元に、チケット（結果）を返し終えたか。false の間に寿命が終わった分は、呼び出し元が Release を呼びようがないため、
        /// リーク（Release の呼び忘れ）として警告しない
        /// </summary>
        internal bool AccIsDelivered => _isDelivered;
#if UNITY_EDITOR
        /// <summary> 確保時のスタックトレース。リーク警告用（#if UNITY_EDITOR のため本番ビルドには含まれない） </summary>
        internal string AccStackTrace => _stackTrace;
#endif

        internal IssuedTicketRecord(AssetEntryKey entry_key, AssetEntryModel entry, LifetimeScopeId scope_id, AssetGroupTicket group)
        {
            _entryKey = entry_key;
            _entry = entry;
            _scopeId = scope_id;
            _group = group;
            _isDelivered = false; // 発行の時点では、まだ呼び出し元に返していない
#if UNITY_EDITOR
            // 確保元を特定するため、発行のたびに記録する（エディタ限定）。
            // まとめチケットに属する分は、まとめチケットが確保時に1回だけ取得したものを共有する（件数分の取得を避ける）
            _stackTrace = group != null ? group.AccStackTrace : Environment.StackTrace;
#endif
        }

        // AsDelivered 用。受け渡し済みの印だけを変えた複製を作る（スタックトレースは取得し直さず、引き継ぐ）
        private IssuedTicketRecord(IssuedTicketRecord source, bool is_delivered)
        {
            _entryKey = source._entryKey;
            _entry = source._entry;
            _scopeId = source._scopeId;
            _group = source._group;
            _isDelivered = is_delivered;
#if UNITY_EDITOR
            _stackTrace = source._stackTrace;
#endif
        }

        /// <summary> 受け渡し済みの印を立てた複製を返す（readonly struct のため、呼び出し側が差し替える） </summary>
        internal IssuedTicketRecord AsDelivered() => new IssuedTicketRecord(this, true);
    }
}
