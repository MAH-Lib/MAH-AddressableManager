using System;
using System.Collections.Generic;
using MAH.AddressableManager.Editor.Localization;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// ルールの Label の選択。Addressables にすでにある Label を、検索ボックス付きの AdvancedDropdown で一覧する。
    /// RegistrationExtensionDropdown と同じく、選ぶたびに on_toggle が呼ばれ、選択済みの Label を選ぶと外れる（チェックの印つき）。
    /// マクロ（{Folder.Top} など）を含む Label や、まだ無い Label は、Inspector 側の入力欄で足す。
    /// </summary>
    internal sealed class RegistrationLabelDropdown : AdvancedDropdown
    {
        private const string CHECK_MARK = "✔ ";
        private const string ROOT_TITLE = "Addressable Label";
        private static readonly Vector2 MINIMUM_SIZE = new(300f, 240f);

        private readonly AddressableAssetSettings _settings;
        private readonly HashSet<string> _selected;
        private readonly Action<string> _onToggle;
        private readonly Dictionary<AdvancedDropdownItem, string> _labelByItem = new();

        /// <param name="settings"> Label を読む Addressables の設定</param>
        /// <param name="selected"> すでに選んでいる Label（チェックの印を付ける）</param>
        /// <param name="on_toggle"> 選ばれた Label を受け取る。選択と解除の切り替えは呼び出し側で行う</param>
        internal RegistrationLabelDropdown(AddressableAssetSettings settings, IEnumerable<string> selected, Action<string> on_toggle)
            : base(new AdvancedDropdownState())
        {
            _settings = settings;
            _onToggle = on_toggle;
            _selected = new HashSet<string>();
            foreach (var label in selected) _selected.Add(label.Trim());
            minimumSize = MINIMUM_SIZE;
        }

        // 開くたびに集め直す（キャッシュしない。開く操作自体が頻繁ではないため）
        protected override AdvancedDropdownItem BuildRoot()
        {
            _labelByItem.Clear();
            var root = new AdvancedDropdownItem(ROOT_TITLE);

            var labels = _settings.GetLabels();
            if (labels.Count == 0)
            {
                // 選べない目印（選んでも何も起きない）
                root.AddChild(new AdvancedDropdownItem(EditorText.Get("dropdown.label.none")) { enabled = false });
            }
            foreach (var label in labels)
            {
                var item = new AdvancedDropdownItem(_selected.Contains(label) ? CHECK_MARK + label : label);
                root.AddChild(item);
                _labelByItem[item] = label;
            }
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (_labelByItem.TryGetValue(item, out var label)) _onToggle(label);
        }
    }
}
