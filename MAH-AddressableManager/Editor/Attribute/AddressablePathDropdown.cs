using System;
using System.Collections.Generic;
using MAH.AddressableManager.Editor.Localization;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Attribute
{
    /// <summary>
    /// AddressablePathAttribute 用の検索ボックス付きプルダウン。グループごとにアドレスを表示する。
    /// アドレス数が数百件規模になっても、AdvancedDropdown 標準の検索で絞り込める。
    /// </summary>
    internal sealed class AddressablePathDropdown : AdvancedDropdown
    {
        private const string ROOT_TITLE = "Addressable Path";

        private readonly AddressableAssetSettings _settings;
        private readonly Action<string> _onSelected;
        private readonly Dictionary<AdvancedDropdownItem, string> _addressByItem = new();

        internal AddressablePathDropdown(AddressableAssetSettings settings, Action<string> on_selected)
            : base(new AdvancedDropdownState())
        {
            _settings = settings;
            _onSelected = on_selected;
            minimumSize = new Vector2(300f, 300f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            // 開くたびに集め直す（キャッシュしない。件数は数百件規模、開く操作自体が頻繁ではないため）
            _addressByItem.Clear();
            var root = new AdvancedDropdownItem(ROOT_TITLE);

            var none_item = new AdvancedDropdownItem(EditorText.Get("attr.notSet"));
            root.AddChild(none_item);
            _addressByItem[none_item] = string.Empty;

            foreach (var group in _settings.groups)
            {
                if (group == null) continue;

                var group_item = new AdvancedDropdownItem(group.Name);
                var has_entry = false;

                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;

                    var entry_item = new AdvancedDropdownItem(entry.address);
                    group_item.AddChild(entry_item);
                    _addressByItem[entry_item] = entry.address;
                    has_entry = true;
                }

                if (has_entry)
                    root.AddChild(group_item);
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (_addressByItem.TryGetValue(item, out var address))
                _onSelected?.Invoke(address);
        }
    }
}
