using System;
using System.Collections.Generic;
using MAH.AddressableManager.Editor.Localization;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// 拡張子の選択（仕様 3.1）。検索ボックス付きの AdvancedDropdown で、AddressablePathDropdown と同じ作り。
    /// 並び：対象フォルダに実際にある拡張子（数つき。いちばん上）→ 種類別の定番。選ぶたびに on_toggle が呼ばれ、
    /// すでに選んでいる拡張子を選ぶと外れる（チェックの印つき）。複数選ぶときは、開き直す。手入力は Inspector 側の入力欄で行う。
    /// </summary>
    internal sealed class RegistrationExtensionDropdown : AdvancedDropdown
    {
        private const string CHECK_MARK = "✔ ";
        private static readonly Vector2 MINIMUM_SIZE = new(260f, 320f);

        // よく使う拡張子を種類別に（仕様 3.1）
        private static readonly KeyValuePair<string, string[]>[] CATEGORIES =
        {
            new("Prefab", new[] { ".prefab" }),
            new("Scene", new[] { ".unity" }),
            new("Material", new[] { ".mat" }),
            new("Texture", new[] { ".png", ".jpg", ".psd", ".tga", ".exr" }),
            new("Audio", new[] { ".wav", ".mp3", ".ogg" }),
            new("Model", new[] { ".fbx", ".obj" }),
            new("Animation", new[] { ".anim", ".controller" }),
            new("ScriptableObject", new[] { ".asset" }),
            new("Font", new[] { ".ttf", ".otf" }),
            new("Text", new[] { ".txt", ".json", ".bytes" }),
            new("Shader", new[] { ".shader" }),
            new("Video", new[] { ".mp4" }),
            new("SpriteAtlas", new[] { ".spriteatlas" }),
        };

        private readonly IReadOnlyList<KeyValuePair<string, int>> _folderExtensions;
        private readonly HashSet<string> _selected;
        private readonly Action<string> _onToggle;
        private readonly Dictionary<AdvancedDropdownItem, string> _extensionByItem = new();

        /// <param name="folder_extensions"> 対象フォルダにある拡張子と数（RegistrationRuleScanner.CollectExtensionCounts の結果）</param>
        /// <param name="selected"> すでに選んでいる拡張子（チェックの印を付ける。正規化して比べる）</param>
        /// <param name="on_toggle"> 選ばれた拡張子（例：.png）を受け取る。選択と解除の切り替えは呼び出し側で行う</param>
        internal RegistrationExtensionDropdown(
            IReadOnlyList<KeyValuePair<string, int>> folder_extensions,
            IEnumerable<string> selected,
            Action<string> on_toggle)
            : base(new AdvancedDropdownState())
        {
            _folderExtensions = folder_extensions;
            _onToggle = on_toggle;
            _selected = new HashSet<string>();
            foreach (var extension in selected) _selected.Add(RegistrationRuleInput.NormalizeExtension(extension));
            minimumSize = MINIMUM_SIZE;
        }

        // 開くたびに作り直す（キャッシュしない）。選ぶたびに閉じるので、開き直したときの状態が反映される
        protected override AdvancedDropdownItem BuildRoot()
        {
            _extensionByItem.Clear();
            var root = new AdvancedDropdownItem(EditorText.Get("dropdown.extensions.root"));

            var folder_group = new AdvancedDropdownItem(EditorText.Get("dropdown.extensions.folder"));
            if (_folderExtensions.Count == 0)
            {
                // 選べない目印（選んでも何も起きない）
                folder_group.AddChild(new AdvancedDropdownItem(EditorText.Get("dropdown.extensions.none")) { enabled = false });
            }
            for (var i = 0; i < _folderExtensions.Count; i++)
            {
                var pair = _folderExtensions[i];
                AddExtensionItem(folder_group, pair.Key, $"{pair.Key} ({pair.Value})");
            }
            root.AddChild(folder_group);

            for (var i = 0; i < CATEGORIES.Length; i++)
            {
                var category_item = new AdvancedDropdownItem(CATEGORIES[i].Key);
                foreach (var extension in CATEGORIES[i].Value) AddExtensionItem(category_item, extension, extension);
                root.AddChild(category_item);
            }
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (_extensionByItem.TryGetValue(item, out var extension)) _onToggle(extension);
        }

        // 選択済みの拡張子には、チェックの印を付ける
        private void AddExtensionItem(AdvancedDropdownItem parent, string extension, string label)
        {
            var is_selected = _selected.Contains(RegistrationRuleInput.NormalizeExtension(extension));
            var item = new AdvancedDropdownItem(is_selected ? CHECK_MARK + label : label);
            _extensionByItem[item] = extension;
            parent.AddChild(item);
        }
    }
}
