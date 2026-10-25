using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Localization
{
    /// <summary> 見つけた言語ファイル（JSON）1つ分の、パスと中身 </summary>
    internal readonly struct EditorLanguageSource
    {
        internal readonly string AccPath;
        internal readonly string AccJson;
        internal readonly bool AccIsBundled; // true：パッケージ同梱。false：利用者が Assets 配下に置いたもの

        internal EditorLanguageSource(string path, string json, bool is_bundled)
        {
            AccPath = path;
            AccJson = json;
            AccIsBundled = is_bundled;
        }
    }

    /// <summary>
    /// 言語ファイル（MAH.AddressableManager.Editor.&lt;言語コード&gt;.json）を探して読む。
    /// 同梱分は Editor asmdef のフォルダ起点（開発フォルダの Assets 配下でも、配布先の Packages 配下でも同じコードで動く）、
    /// 利用者の追加分は Assets 配下の同じ名前のファイル（置き場所は自由）。
    /// </summary>
    internal static class EditorLanguageFileLocator
    {
        internal const string FILE_PREFIX = "MAH.AddressableManager.Editor.";
        internal const string FILE_EXTENSION = ".json";

        private const string EDITOR_ASSEMBLY_NAME = "MAH.AddressableManager.Editor";
        private const string BUNDLED_FOLDER_NAME = "Localization";
        private const string LOG_PREFIX = "[AddressableManager] ";

        private static readonly Regex FileNamePattern = new(@"^MAH\.AddressableManager\.Editor\.([A-Za-z0-9_\-]+)\.json$", RegexOptions.Compiled);

        /// <summary> アセットのパスが、言語ファイルの名前（MAH.AddressableManager.Editor.&lt;言語コード&gt;.json）かどうか </summary>
        internal static bool IsLanguageFilePath(string asset_path)
        {
            return !string.IsNullOrEmpty(asset_path) && FileNamePattern.IsMatch(Path.GetFileName(asset_path));
        }

        /// <summary> ファイル名から言語コードを取り出す。名前の規則に合わないときは false </summary>
        internal static bool TryGetLanguageCodeFromFileName(string file_name, out string language_code)
        {
            var match = FileNamePattern.Match(file_name ?? string.Empty);
            language_code = match.Success ? match.Groups[1].Value : null;
            return match.Success;
        }

        /// <summary> 同梱の言語ファイルのフォルダ（"Assets/…/Editor/Localization" または "Packages/…/Editor/Localization"）。見つからなければ null </summary>
        internal static string FindBundledFolderAssetPath()
        {
            var asmdef_path = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(EDITOR_ASSEMBLY_NAME);
            if (string.IsNullOrEmpty(asmdef_path)) return null;

            var folder = Path.GetDirectoryName(asmdef_path).Replace('\\', '/');
            return folder + "/" + BUNDLED_FOLDER_NAME;
        }

        /// <summary> 同梱分と利用者分の言語ファイルを、同梱 → 利用者の順に集める（後に足したものがキー単位で優先される） </summary>
        internal static void Collect(List<EditorLanguageSource> sources)
        {
            sources.Clear();

            var bundled_folder = FindBundledFolderAssetPath();
            if (bundled_folder != null) CollectBundled(bundled_folder, sources);
            CollectUser(bundled_folder, sources);
        }

        // 同梱分は AssetDatabase が使えないタイミング（起動直後）でも読めるよう、ファイルを直接読む
        private static void CollectBundled(string bundled_folder, List<EditorLanguageSource> sources)
        {
            var full_folder = ToFullPath(bundled_folder);
            if (full_folder == null || !Directory.Exists(full_folder)) return;

            var files = Directory.GetFiles(full_folder, FILE_PREFIX + "*" + FILE_EXTENSION);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var file in files)
            {
                if (!FileNamePattern.IsMatch(Path.GetFileName(file))) continue;
                sources.Add(new EditorLanguageSource(bundled_folder + "/" + Path.GetFileName(file), File.ReadAllText(file), true));
            }
        }

        private static void CollectUser(string bundled_folder, List<EditorLanguageSource> sources)
        {
            // 起動直後や Refresh の最中など AssetDatabase が使えないときは、利用者分は読まない
            // （EditorLanguagePostprocessor が、Refresh の完了後に読み直しを予約する）
            if (EditorApplication.isUpdating) return;

            var guids = AssetDatabase.FindAssets("t:TextAsset " + FILE_PREFIX);
            var paths = new List<string>(guids.Length);
            foreach (var guid in guids)
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(StringComparer.Ordinal);

            foreach (var path in paths)
            {
                if (!IsLanguageFilePath(path)) continue;
                if (bundled_folder != null && path.StartsWith(bundled_folder + "/", StringComparison.Ordinal)) continue; // 同梱分は読み込み済み
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue; // 他のパッケージ内のファイルは対象にしない

                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset == null) continue;
                sources.Add(new EditorLanguageSource(path, asset.text, false));
            }
        }

        // "Assets/…" はプロジェクト直下からの相対、"Packages/…" は Package Manager が解決した実際の場所に変換する
        private static string ToFullPath(string asset_path)
        {
            if (asset_path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var package_info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(asset_path);
                if (package_info == null)
                {
                    Debug.LogWarning($"{LOG_PREFIX}Could not resolve the package folder of {asset_path}.");
                    return null;
                }
                return package_info.resolvedPath + asset_path.Substring(package_info.assetPath.Length);
            }

            return Path.GetFullPath(asset_path);
        }
    }
}
