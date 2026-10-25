using System;
using System.Collections.Generic;
using MAH.AddressableManager.Editor.Localization;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// 登録先のグループの選択。Addressables にすでにあるグループを、検索ボックス付きの AdvancedDropdown で一覧し、1つ選ぶ。
    /// 先頭の「空にする」を選ぶと、空文字を返す（グループ名のテンプレートが空のときは、Addressables の DefaultGroup になる）。
    /// Built In Data など、エントリを入れられない読み取り専用のグループは出さない。
    /// マクロ（UI_{Folder.Top} など）を含む名前や、まだ無いグループは、呼び出し側の入力欄で書く。
    /// </summary>
    internal sealed class RegistrationGroupDropdown : AdvancedDropdown
    {
        private const string CHECK_MARK = "✔ ";
        private const string ROOT_TITLE = "Addressable Group";
        private static readonly Vector2 MINIMUM_SIZE = new(300f, 240f);

        private readonly AddressableAssetSettings _settings;
        private readonly string _current;
        private readonly Action<string> _onSelected;
        private readonly Dictionary<AdvancedDropdownItem, string> _groupNameByItem = new();

        /// <param name="settings"> グループを読む Addressables の設定</param>
        /// <param name="current"> いま指定している値（一致するグループにチェックの印を付ける）</param>
        /// <param name="on_selected"> 選ばれたグループ名（「空にする」なら空文字）を受け取る</param>
        internal RegistrationGroupDropdown(AddressableAssetSettings settings, string current, Action<string> on_selected)
            : base(new AdvancedDropdownState())
        {
            _settings = settings;
            _current = (current ?? string.Empty).Trim();
            _onSelected = on_selected;
            minimumSize = MINIMUM_SIZE;
        }

        // 開くたびに集め直す（キャッシュしない。開く操作自体が頻繁ではないため）
        protected override AdvancedDropdownItem BuildRoot()
        {
            _groupNameByItem.Clear();
            var root = new AdvancedDropdownItem(ROOT_TITLE);

            var empty_label = EditorText.Get("dropdown.group.empty");
            var empty_item = new AdvancedDropdownItem(_current.Length == 0 ? CHECK_MARK + empty_label : empty_label);
            root.AddChild(empty_item);
            _groupNameByItem[empty_item] = string.Empty;

            foreach (var group in _settings.groups)
            {
                if (group == null || group.ReadOnly || group.HasSchema<PlayerDataGroupSchema>()) continue;

                var item = new AdvancedDropdownItem(group.Name == _current ? CHECK_MARK + group.Name : group.Name);
                root.AddChild(item);
                _groupNameByItem[item] = group.Name;
            }
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (_groupNameByItem.TryGetValue(item, out var group_name)) _onSelected(group_name);
        }
    }
}
