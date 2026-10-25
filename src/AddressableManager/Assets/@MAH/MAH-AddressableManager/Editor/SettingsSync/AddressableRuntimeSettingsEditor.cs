using MAH.AddressableManager.Core;
using MAH.AddressableManager.Editor.Localization;
using UnityEditor;

namespace MAH.AddressableManager.Editor.SettingsSync
{
    /// <summary>
    /// AddressableRuntimeSettingsSO（Runtime の設定 SO）の Inspector。見出し・ツールチップを、表示言語に合わせて描く（LocalizedPropertyView）。
    /// 文言は Editor 側の言語ファイルに持つため、Runtime のクラスには [Header] / [Tooltip] を書かない（ビルドにも日本語の文字列が載らない）。
    /// </summary>
    [CustomEditor(typeof(AddressableRuntimeSettingsSO))]
    internal sealed class AddressableRuntimeSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI() => LocalizedPropertyView.DrawAll(serializedObject);
    }
}
