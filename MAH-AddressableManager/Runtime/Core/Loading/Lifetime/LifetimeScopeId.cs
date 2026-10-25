using System;
using MAH.AddressableManager;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// 寿命の実体を一意に識別する ID。各 AssetLifetime は LoadingController.ResolveScope でこれに解決される。
    /// 外部には公開しない内部専用の型（AssetTicket からは internal フィールドとしてのみ参照される）。
    /// </summary>
    internal readonly struct LifetimeScopeId : IEquatable<LifetimeScopeId>
    {
        private readonly LifetimeKind _kind;
        private readonly int _value;

        internal LifetimeScopeId(LifetimeKind kind, int value)
        {
            _kind = kind;
            _value = value;
        }

        // 2026-10-14 Editor のデバッグウィンドウに渡す Snapshot の生成用に追加（等価判定・GetHashCode は変えない）
        internal LifetimeKind AccKind => _kind;
        /// <summary> Owner は GameObject の InstanceID、Scene はシーンの handle、Manual は 0 </summary>
        internal int AccValue => _value;

        private static readonly LifetimeScopeId _manual = new(LifetimeKind.Manual, 0);

        /// <summary> Manual 寿命のスコープ。値は固定の1つだけ </summary>
        internal static LifetimeScopeId Manual => _manual;

        public bool Equals(LifetimeScopeId other) => _kind == other._kind && _value == other._value;

        public override bool Equals(object obj) => obj is LifetimeScopeId other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_kind, _value);

        public override string ToString() => $"{_kind}:{_value}";
    }
}
