using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MAH.AddressableManager.Editor.Localization;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// アドレス・グループ名・Label のテンプレートに含まれるマクロ（{Asset.Name} など）を展開する。
    /// 自動登録・当て直し・手動一括登録・Inspector のプレビューで共通に使う。Unity に依存しない（偽の入力で EditMode テストするため）。
    /// 知らないマクロ・知らない変換は、書かれたままの文字列で残し、警告を返す（書き間違いで登録そのものが止まらないようにするため）。
    /// </summary>
    internal static class RegistrationMacroExpander
    {
        /// <summary> 標準の値として扱うマクロ名（仕様 6.3）。Scanner はこの名前で値の表を作る </summary>
        internal static readonly string[] STANDARD_MACRO_NAMES =
        {
            "UnityEngine.Application.version",
            "UnityEngine.Application.productName",
            "UnityEngine.Application.companyName",
            "UnityEngine.Application.identifier",
            "UnityEngine.Application.unityVersion",
            "UnityEngine.Application.platform",
            "UnityEditor.PlayerSettings.bundleVersion",
            "UnityEditor.EditorUserBuildSettings.activeBuildTarget",
        };

        private const char MACRO_BEGIN = '{';
        private const char MACRO_END = '}';
        private const char TRANSFORM_SEPARATOR = ':';
        private const string TRANSFORM_LOWER = "lower";
        private const string TRANSFORM_UPPER = "upper";

        // 展開のたびに確保しないよう使い回す（Editor のメインスレッドでのみ使う。Expand は再入しない）
        private static readonly StringBuilder BUILDER = new();

        /// <summary>
        /// テンプレートを展開して返す。展開した結果が空になることもある（空のときの扱いは呼び出し側。仕様 3.5・6.5）。
        /// 知らないマクロ・変換は書かれたまま残し、warnings（null 可）に警告を足す。
        /// </summary>
        internal static string Expand(string template, RegistrationMacroContext context, List<string> warnings)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            if (template.IndexOf(MACRO_BEGIN) < 0) return template;

            BUILDER.Clear();
            var i = 0;
            while (i < template.Length)
            {
                var begin = template.IndexOf(MACRO_BEGIN, i);
                if (begin < 0)
                {
                    BUILDER.Append(template, i, template.Length - i);
                    break;
                }

                var end = template.IndexOf(MACRO_END, begin + 1);
                if (end < 0)
                {
                    // 閉じていない { は、マクロとして扱わない
                    BUILDER.Append(template, i, template.Length - i);
                    break;
                }

                BUILDER.Append(template, i, begin - i);
                var written = template.Substring(begin, end - begin + 1);
                var body = template.Substring(begin + 1, end - begin - 1);
                BUILDER.Append(ExpandMacro(body, written, context, warnings));
                i = end + 1;
            }
            return BUILDER.ToString();
        }

        /// <summary>
        /// グループ名として使えるかを返す。Addressables はグループ名をファイル名にするため、ファイル名に使えない文字（/ など）を含むと不可。
        /// 空の名前は、呼び出し側で DefaultGroup に置き換えるため、ここでは扱わない（不可として返す）。
        /// </summary>
        internal static bool IsValidGroupName(string group_name)
        {
            if (string.IsNullOrWhiteSpace(group_name)) return false;
            return group_name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        // body は {} の中（例：Asset.Name:lower）。written は書かれたままの文字列（例：{Asset.Name:lower}）
        private static string ExpandMacro(string body, string written, RegistrationMacroContext context, List<string> warnings)
        {
            var name = body;
            string transform = null;
            var separator = body.IndexOf(TRANSFORM_SEPARATOR);
            if (separator >= 0)
            {
                name = body.Substring(0, separator);
                transform = body.Substring(separator + 1);
            }

            if (!TryGetValue(name, context, out var value))
            {
                warnings?.Add(EditorText.Format("registration.macro.unknownMacro", written));
                return written;
            }

            if (transform == null) return value;

            // マクロの名前と同じく、変換の名前も大文字小文字を区別する
            if (transform == TRANSFORM_LOWER) return value.ToLowerInvariant();
            if (transform == TRANSFORM_UPPER) return value.ToUpperInvariant();

            warnings?.Add(EditorText.Format("registration.macro.unknownTransform", written));
            return written;
        }

        // アセット・フォルダのマクロ（仕様 6.2）と標準の値（仕様 6.3）の値を引く。知らない名前なら false
        private static bool TryGetValue(string name, RegistrationMacroContext context, out string value)
        {
            var asset_path = context.AccAssetPath;
            switch (name)
            {
                case "Asset.Path":
                    value = asset_path;
                    return true;
                case "Asset.Name":
                    value = Path.GetFileNameWithoutExtension(asset_path);
                    return true;
                case "Asset.Extension":
                    value = Path.GetExtension(asset_path).TrimStart('.');
                    return true;
                case "Asset.Type":
                    value = context.AccAssetTypeName;
                    return true;
                case "Asset.Guid":
                    value = context.AccAssetGuid;
                    return true;
                case "Folder.Name":
                    value = GetFolderName(asset_path);
                    return true;
                case "Folder.Top":
                    value = GetTopFolder(GetRelativeFolder(asset_path, context.AccRoot));
                    return true;
                case "Folder.Relative":
                    value = GetRelativeFolder(asset_path, context.AccRoot);
                    return true;
                case "Root":
                    value = context.AccRoot;
                    return true;
                case "Path.Relative":
                    value = GetRelativePathWithoutExtension(asset_path, context.AccRoot);
                    return true;
                case "Rule.Name":
                    value = context.AccRuleName;
                    return true;
            }

            if (Array.IndexOf(STANDARD_MACRO_NAMES, name) >= 0)
            {
                // 標準の値は、表に無いとき（取得できなかったとき）は空として扱う
                value = context.AccStandardValues.TryGetValue(name, out var standard_value) ? standard_value : string.Empty;
                return true;
            }

            value = null;
            return false;
        }

        // 直属の親フォルダの名前（アセットが Assets 直下なら "Assets"）
        private static string GetFolderName(string asset_path)
        {
            var directory = Path.GetDirectoryName(asset_path);
            return string.IsNullOrEmpty(directory) ? string.Empty : Path.GetFileName(directory.Replace('\\', '/'));
        }

        // 対象フォルダから親フォルダまでのパス。対象フォルダ直下のアセットなら空。対象フォルダの外なら、パス全体の親フォルダ
        private static string GetRelativeFolder(string asset_path, string root)
        {
            var directory = (Path.GetDirectoryName(asset_path) ?? string.Empty).Replace('\\', '/');
            if (IsUnderRoot(asset_path, root))
                return directory.Length > root.Length ? directory.Substring(root.Length + 1) : string.Empty;
            return directory;
        }

        // 対象フォルダの直下のフォルダの名前（相対フォルダの先頭の1階層）
        private static string GetTopFolder(string relative_folder)
        {
            var slash = relative_folder.IndexOf('/');
            return slash < 0 ? relative_folder : relative_folder.Substring(0, slash);
        }

        // 対象フォルダからの相対パス（拡張子なし）。対象フォルダの外なら、アセットパス全体（拡張子なし）
        private static string GetRelativePathWithoutExtension(string asset_path, string root)
        {
            var path = IsUnderRoot(asset_path, root) ? asset_path.Substring(root.Length + 1) : asset_path;
            var extension = Path.GetExtension(path);
            return extension.Length > 0 ? path.Substring(0, path.Length - extension.Length) : path;
        }

        private static bool IsUnderRoot(string asset_path, string root)
        {
            return root.Length > 0
                   && asset_path.Length > root.Length + 1
                   && asset_path[root.Length] == '/'
                   && asset_path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }
}
