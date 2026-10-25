using System;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// Label から取得したロケーション1件分。(PrimaryKey, ResourceType) の組。
    /// 画像（Sprite 設定のテクスチャ）は、同じ PrimaryKey で Texture2D と Sprite の2件になる。
    /// </summary>
    internal readonly struct LabelLocation
    {
        private readonly string _primaryKey;
        private readonly Type _resourceType;

        /// <summary> アドレス（Include Addresses in Catalog が無効だと、アドレスにならない場合がある） </summary>
        internal string AccPrimaryKey => _primaryKey;
        /// <summary> このロケーションの型。UnityEngine.Object の派生型だけが残る </summary>
        internal Type AccResourceType => _resourceType;

        internal LabelLocation(string primary_key, Type resource_type)
        {
            _primaryKey = primary_key;
            _resourceType = resource_type;
        }

        public override string ToString() => $"{_primaryKey}<{_resourceType?.Name}>";
    }
}
