#if UNITY_EDITOR
using MAH.AddressableManager;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// 1つの Entry における、寿命スコープ1件分の確保数（CountByScope の1行）の、取得した時点の値のコピー。
    /// どの Entry の分かは、AssetEntrySnapshot の開始位置と件数で決まる。
    /// スコープの値（Owner の InstanceID・Scene の handle）の名前の解決は、Editor 側が行う（Runtime の Snapshot に UnityEngine.Object を持たせないため）。
    /// </summary>
    internal readonly struct ScopeCountSnapshot
    {
        private readonly LifetimeKind _scopeKind;
        private readonly int _scopeValue;
        private readonly int _count;

        internal LifetimeKind AccScopeKind => _scopeKind;
        /// <summary> スコープの値。Owner は GameObject の InstanceID、Scene はシーンの handle、Manual は 0 </summary>
        internal int AccScopeValue => _scopeValue;
        internal int AccCount => _count;

        internal ScopeCountSnapshot(LifetimeKind scope_kind, int scope_value, int count)
        {
            _scopeKind = scope_kind;
            _scopeValue = scope_value;
            _count = count;
        }
    }
}
#endif
