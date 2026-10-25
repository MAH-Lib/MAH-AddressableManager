using System;
using MAH.AddressableManager;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// Registry の判定キー。(AssetKey, 型) の組が一致した場合のみ、同じエントリとみなす。
    /// AssetKey 自体は型を含めない。型違いのロードを別エントリとして扱う（例外にしない）ために、Registry 側のキーに型を加えている。
    /// </summary>
    internal readonly struct AssetEntryKey : IEquatable<AssetEntryKey>
    {
        private readonly AssetKey _key;
        private readonly Type _assetType;

        internal AssetKey AccKey => _key;
        internal Type AccAssetType => _assetType;

        internal AssetEntryKey(AssetKey key, Type asset_type)
        {
            _key = key;
            _assetType = asset_type ?? throw new ArgumentNullException(nameof(asset_type));
        }

        public bool Equals(AssetEntryKey other) => _key == other._key && _assetType == other._assetType;

        public override bool Equals(object obj) => obj is AssetEntryKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_key, _assetType);

        public override string ToString() => $"{_key}<{_assetType?.Name}>";

        public static bool operator ==(AssetEntryKey left, AssetEntryKey right) => left.Equals(right);

        public static bool operator !=(AssetEntryKey left, AssetEntryKey right) => !left.Equals(right);
    }
}
