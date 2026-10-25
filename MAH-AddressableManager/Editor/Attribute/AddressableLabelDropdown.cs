using System;
using System.Collections.Generic;
using MAH.AddressableManager.Editor.Localization;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Attribute
{
    /// <summary>
    /// AddressableLabelAttribute 用の検索ボックス付きプルダウン。Label はグループに属さないため、階層を作らず一覧で表示する。
    /// </summary>
    internal sealed class AddressableLabelDropdown : AdvancedDropdown
    {
        private const string ROOT_TITLE = "Addressable Label";

        private readonly AddressableAssetSettings _settings;
        private readonly Action<string> _onSelected;
        private readonly Dictionary<AdvancedDropdownItem, string> _labelByItem = new();

        internal AddressableLabelDropdown(AddressableAssetSettings settings, Action<string> on_selected)
            : base(new AdvancedDropdownState())
        {
            _settings = settings;
            _onSelected = on_selected;
            minimumSize = new Vector2(300f, 200f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            // 開くたびに集め直す（キャッシュしない。開く操作自体が頻繁ではないため）
            _labelByItem.Clear();
            var root = new AdvancedDropdownItem(ROOT_TITLE);

            var none_item = new AdvancedDropdownItem(EditorText.Get("attr.notSet"));
            root.AddChild(none_item);
            _labelByItem[none_item] = string.Empty;

            foreach (var label in _settings.GetLabels())
            {
                var label_item = new AdvancedDropdownItem(label);
                root.AddChild(label_item);
                _labelByItem[label_item] = label;
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (_labelByItem.TryGetValue(item, out var label))
                _onSelected?.Invoke(label);
        }
    }
}
