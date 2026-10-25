using System.IO;
using UnityEditor;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Localization
{
    /// <summary>
    /// メニュー MAH/Addressable/Localization/Export Translation Template。
    /// 英語の全キーを入れた言語ファイル（JSON）の雛形を、利用者が選んだ Assets 配下の場所に書き出す。
    /// 利用者がパッケージのキャッシュの中を探さずに、新しい言語を作れるようにするため。
    /// 書き込むのは利用者が選んだ新しいファイルだけで、Addressables の設定には触れない。
    /// </summary>
    internal static class EditorLanguageTemplateCreator
    {
        private const string MENU_PATH = "MAH/Addressable/Localization/Export Translation Template";
        private const string DEFAULT_LANGUAGE_CODE = "xx";

        [MenuItem(MENU_PATH)]
        private static void Export()
        {
            var default_name = EditorLanguageFileLocator.FILE_PREFIX + DEFAULT_LANGUAGE_CODE + EditorLanguageFileLocator.FILE_EXTENSION;
            var path = EditorUtility.SaveFilePanelInProject(
                EditorText.Get("template.dialog.title"),
                default_name,
                "json",
                EditorText.Get("template.dialog.message"),
                "Assets");
            if (string.IsNullOrEmpty(path)) return;

            if (!EditorLanguageFileLocator.TryGetLanguageCodeFromFileName(Path.GetFileName(path), out var language_code))
            {
                EditorUtility.DisplayDialog(
                    EditorText.Get("template.dialog.title"),
                    EditorText.Format("template.invalidFileName", EditorLanguageFileLocator.FILE_PREFIX + "<code>" + EditorLanguageFileLocator.FILE_EXTENSION),
                    EditorText.Get("common.ok"));
                return;
            }

            File.WriteAllText(path, EditorText.BuildTemplateJson(language_code, language_code));
            AssetDatabase.ImportAsset(path);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<TextAsset>(path));
        }
    }
}
