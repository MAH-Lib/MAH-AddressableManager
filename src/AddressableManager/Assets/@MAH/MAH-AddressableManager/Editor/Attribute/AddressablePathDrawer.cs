using MAH.AddressableManager.Editor.Localization;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using AddressablePathAttribute = MAH.AddressableManager.Attribute.AddressablePathAttribute;

namespace MAH.AddressableManager.Editor.Attribute
{
    /// <summary>
    /// AddressablePathAttribute の PropertyDrawer。
    /// アドレス一覧は AddressableAssetSettings から取得するが、これはエディタ専用アセンブリからしか
    /// 参照できないため、属性クラス本体（Runtime 側）とはここで分離している。
    /// </summary>
    [CustomPropertyDrawer(typeof(AddressablePathAttribute))]
    internal sealed class AddressablePathDrawer : PropertyDrawer
    {
        private const float BUTTON_WIDTH = 24f;
        private const float BUTTON_MARGIN = 2f;
        private const float HELP_BOX_HEIGHT = 32f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.HelpBox(position, EditorText.Format("attr.stringOnly", nameof(AddressablePathAttribute)), MessageType.Error);
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                // Addressables の設定が無いプロジェクトでは通常の文字列入力に戻す
                var field_only_rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.PropertyField(field_only_rect, property, label);

                var notice_rect = new Rect(position.x, field_only_rect.yMax + BUTTON_MARGIN, position.width, HELP_BOX_HEIGHT);
                EditorGUI.HelpBox(notice_rect, EditorText.Get("attr.noSettings"), MessageType.Info);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            var field_rect = new Rect(position.x, position.y, position.width - BUTTON_WIDTH - BUTTON_MARGIN, EditorGUIUtility.singleLineHeight);
            var button_rect = new Rect(field_rect.xMax + BUTTON_MARGIN, position.y, BUTTON_WIDTH, EditorGUIUtility.singleLineHeight);

            EditorGUI.PropertyField(field_rect, property, label);

            if (GUI.Button(button_rect, "▼"))
            {
                var dropdown = new AddressablePathDropdown(settings, address =>
                {
                    property.stringValue = address;
                    property.serializedObject.ApplyModifiedProperties();
                });
                dropdown.Show(button_rect);
            }

            if (!string.IsNullOrEmpty(property.stringValue) && !AddressExists(settings, property.stringValue))
            {
                var warn_rect = new Rect(position.x, field_rect.yMax + BUTTON_MARGIN, position.width, HELP_BOX_HEIGHT);
                EditorGUI.HelpBox(warn_rect, EditorText.Format("attr.addressMissing", property.stringValue), MessageType.Warning);
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return EditorGUIUtility.singleLineHeight;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return EditorGUIUtility.singleLineHeight + BUTTON_MARGIN + HELP_BOX_HEIGHT;

            var is_missing = !string.IsNullOrEmpty(property.stringValue) && !AddressExists(settings, property.stringValue);
            return EditorGUIUtility.singleLineHeight + (is_missing ? BUTTON_MARGIN + HELP_BOX_HEIGHT : 0f);
        }

        private static bool AddressExists(AddressableAssetSettings settings, string address)
        {
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry != null && entry.address == address)
                        return true;
                }
            }
            return false;
        }
    }
}
