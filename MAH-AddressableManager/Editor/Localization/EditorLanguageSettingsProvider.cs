using System.Collections.Generic;
using UnityEditor;

namespace MAH.AddressableManager.Editor.Localization
{
    /// <summary>
    /// Edit > Preferences > MAH Addressable。表示言語を選ぶ（個人ごとの設定。EditorPrefs に保存）。
    /// 選択肢は、読み込めた言語ファイルの languageName（利用者が追加した言語も並ぶ）。
    /// </summary>
    internal static class EditorLanguageSettingsProvider
    {
        private const string PREFERENCES_PATH = "Preferences/MAH Addressable";

        private static readonly List<string> CodeBuffer = new();
        private static string[] _labels = System.Array.Empty<string>();

        [SettingsProvider]
        private static SettingsProvider Create()
        {
            var provider = new SettingsProvider(PREFERENCES_PATH, SettingsScope.User)
            {
                label = "MAH Addressable",
                guiHandler = _ => DrawGui(),
                // 検索の語は、選んでいる表示言語に関係なく当たるよう、日英を並べる（画面には出ないため、EditorText を通さない）
                keywords = new HashSet<string> { "MAH", "Addressable", "Language", "言語" },
            };
            return provider;
        }

        private static void DrawGui()
        {
            var codes = EditorText.AccLanguageCodes;
            CodeBuffer.Clear();
            CodeBuffer.AddRange(codes);

            if (_labels.Length != CodeBuffer.Count) _labels = new string[CodeBuffer.Count];
            for (var i = 0; i < CodeBuffer.Count; i++)
                _labels[i] = EditorText.GetLanguageName(CodeBuffer[i]);

            var current_index = CodeBuffer.IndexOf(EditorText.AccCurrentLanguageCode);
            if (current_index < 0) current_index = 0;

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            var new_index = EditorGUILayout.Popup(EditorText.Get("preferences.language.label"), current_index, _labels);
            if (EditorGUI.EndChangeCheck() && new_index >= 0 && new_index < CodeBuffer.Count)
                EditorText.SetLanguage(CodeBuffer[new_index]);

            EditorGUILayout.HelpBox(EditorText.Get("preferences.language.help"), MessageType.Info);
        }
    }
}
