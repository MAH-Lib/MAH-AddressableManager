using MAH.AddressableManager.Editor.Localization;
using UnityEditor;

namespace MAH.AddressableManager.Editor.SettingsSync
{
    /// <summary>
    /// AddressableEditorSettingsSO の Inspector。見出し・ツールチップを、表示言語に合わせて描く（LocalizedPropertyView）。
    /// 項目の並びと折りたたみは、標準の Inspector と同じ。
    /// </summary>
    [CustomEditor(typeof(AddressableEditorSettingsSO))]
    internal sealed class AddressableEditorSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() => LocalizedPropertyView.DrawAll(serializedObject);
    }
}
