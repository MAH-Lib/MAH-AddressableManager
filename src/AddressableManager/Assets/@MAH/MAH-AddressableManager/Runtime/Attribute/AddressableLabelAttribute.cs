using System;
using UnityEngine;

namespace MAH.AddressableManager.Attribute
{
    /// <summary>
    /// Addressable の Label を Inspector のプルダウンから選択できるようにする属性。
    /// string フィールドに付ける。Addressables の Editor API はこちらからは参照しない
    /// （PropertyDrawer 側（Editor/Attribute/AddressableLabelDrawer.cs）でのみ参照する）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class AddressableLabelAttribute : PropertyAttribute
    {
    }
}
