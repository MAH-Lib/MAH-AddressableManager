using MAH.AddressableManager.Editor.Localization;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using AddressableLabelAttribute = MAH.AddressableManager.Attribute.AddressableLabelAttribute;

namespace MAH.AddressableManager.Editor.Attribute
{
    /// <summary>
    /// AddressableLabelAttribute の PropertyDrawer。
    /// Label 一覧は AddressableAssetSettings から取得するが、これはエディタ専用アセンブリからしか
    /// 参照できないため、属性クラス本体（Runtime 側）とはここで分離している。
    /// </summary>
    [CustomPropertyDrawer(typeof(AddressableLabelAttribute))]
    internal sealed class AddressableLabelDrawer : PropertyDrawer
    {
        private const float BUTTON_WIDTH = 24f;
        private const float BUTTON_MARGIN = 2f;
        private const float HELP_BOX_HEIGHT = 32f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.HelpBox(position, EditorText.Format("attr.stringOnly", nameof(AddressableLabelAttribute)), MessageType.Error);
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
                var dropdown = new AddressableLabelDropdown(settings, selected_label =>
                {
                    property.stringValue = selected_label;
                    property.serializedObject.ApplyModifiedProperties();
                });
                dropdown.Show(button_rect);
            }

            if (!string.IsNullOrEmpty(property.stringValue) && !LabelExists(settings, property.stringValue))
            {
                var warn_rect = new Rect(position.x, field_rect.yMax + BUTTON_MARGIN, position.width, HELP_BOX_HEIGHT);
                EditorGUI.HelpBox(warn_rect, EditorText.Format("attr.labelMissing", property.stringValue), MessageType.Warning);
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

            var is_missing = !string.IsNullOrEmpty(property.stringValue) && !LabelExists(settings, property.stringValue);
            return EditorGUIUtility.singleLineHeight + (is_missing ? BUTTON_MARGIN + HELP_BOX_HEIGHT : 0f);
        }

        private static bool LabelExists(AddressableAssetSettings settings, string label)
        {
            // GetLabels は Addressables の Labels 設定に登録された Label を返す
            return settings.GetLabels().Contains(label);
        }
    }
}
