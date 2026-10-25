using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// アセットのパスと、ルールの入力から、当たるルールを求める。競合（ルール同士の重なり）の判定も行う。
    /// Unity に依存しない（偽の入力で EditMode テストするため）。
    /// </summary>
    internal static class RegistrationRuleMatcher
    {
        private const string ASSETS_PREFIX = "Assets/";
        private const string RESOURCES_SEGMENT = "/Resources/";
        private const string EDITOR_SEGMENT = "/Editor/";

        // 登録しない拡張子。Addressables 自身が登録を拒む拡張子（AddressableAssetUtility.IsPathValidForEntry は internal で呼べないため、
        // 同じ条件を持つ）に、asmref を足したもの。小文字・ドットなし
        // （パスの判定のたびに文字列を確保しないよう、HasExtension で大文字小文字を区別せずに比べる）
        private static readonly string[] IGNORED_EXTENSIONS =
        {
            "cs", "js", "boo", "exe", "dll", "meta", "preset", "asmdef", "asmref",
        };

        // glob から作った Regex のキャッシュ（同じ除外パターンを大量のアセットで評価するため、毎回作らない）
        private static readonly Dictionary<string, Regex> GLOB_CACHE = new();

        /// <summary> パスだけで判定できる「いつも登録しないもの」かを返す（仕様 7.2。型で判定するもの・フォルダは Scanner 側） </summary>
        internal static RegistrationExclusionKind GetExclusionKind(string asset_path)
        {
            if (string.IsNullOrEmpty(asset_path)) return RegistrationExclusionKind.Ignored;

            var path = NormalizeSeparator(asset_path);
            // Packages/ と Assets の外は対象にしない
            if (!path.StartsWith(ASSETS_PREFIX, StringComparison.OrdinalIgnoreCase)) return RegistrationExclusionKind.Ignored;
            if (HasAnyExtension(IGNORED_EXTENSIONS, path)) return RegistrationExclusionKind.Ignored;
            // パスは必ず Assets/ で始まるため、Assets 直下の Resources / Editor も / 付きの区切りで見つかる
            // Editor フォルダの配下は、Addressables 自身が登録を拒む
            if (path.IndexOf(EDITOR_SEGMENT, StringComparison.OrdinalIgnoreCase) >= 0) return RegistrationExclusionKind.Ignored;
            if (path.IndexOf(RESOURCES_SEGMENT, StringComparison.OrdinalIgnoreCase) >= 0) return RegistrationExclusionKind.Resources;
            return RegistrationExclusionKind.None;
        }

        /// <summary>
        /// 有効なルールのうち、いずれかの対象フォルダの配下にあるパスかを返す。
        /// インポートされたアセットを、AssetDatabase に触る前に文字列だけで絞り込むために使う（自動登録と無関係なインポートを早く捨てる）。
        /// </summary>
        internal static bool IsUnderAnyEnabledRuleFolder(IReadOnlyList<RegistrationRuleInput> rules, string asset_path)
        {
            if (rules == null || string.IsNullOrEmpty(asset_path)) return false;

            var path = NormalizeSeparator(asset_path);
            for (var i = 0; i < rules.Count; i++)
            {
                if (rules[i].AccEnabled && IsUnderFolder(path, rules[i].AccFolderPath)) return true;
            }
            return false;
        }

        /// <summary>
        /// アセットが、ルールに当たるかを返す。Enabled がオフ・対象フォルダの外・拡張子が合わない・除外パターンに当たる場合は false。
        /// 「いつも登録しないもの」の判定は含まない（GetExclusionKind で別に行う）。
        /// </summary>
        internal static bool IsMatch(RegistrationRuleInput rule, string asset_path)
        {
            if (rule == null || !rule.AccEnabled || string.IsNullOrEmpty(asset_path)) return false;

            var path = NormalizeSeparator(asset_path);
            if (!IsUnderFolder(path, rule.AccFolderPath)) return false;
            if (!IsExtensionTarget(rule, path)) return false;

            var patterns = rule.AccExcludePatterns;
            for (var i = 0; i < patterns.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(patterns[i]) && IsGlobMatch(patterns[i], path)) return false;
            }
            return true;
        }

        /// <summary>
        /// アセットに当たるルールをすべて集める。result は最初に Clear する。
        /// 「いつも登録しないもの」のときは何も入れない。2件以上入ったら競合。
        /// </summary>
        internal static void FindMatches(IReadOnlyList<RegistrationRuleInput> rules, string asset_path, List<RegistrationRuleInput> result)
        {
            result.Clear();
            if (rules == null || GetExclusionKind(asset_path) != RegistrationExclusionKind.None) return;

            for (var i = 0; i < rules.Count; i++)
            {
                if (IsMatch(rules[i], asset_path)) result.Add(rules[i]);
            }
        }

        /// <summary>
        /// 2つのルールが競合しそうか（パスの範囲と拡張子の両方が重なるか）を返す。ルールの SO の保存時の警告・ルール一覧の表示に使う。
        /// 除外パターンは見ない（実際に競合するアセットがあるかは、アセットを集めないと分からないため、重なりの可能性で警告する）。
        /// </summary>
        internal static bool AreOverlapping(RegistrationRuleInput a, RegistrationRuleInput b)
        {
            if (a == null || b == null || ReferenceEquals(a, b)) return false;
            if (!a.AccEnabled || !b.AccEnabled) return false;
            if (a.AccFolderPath.Length == 0 || b.AccFolderPath.Length == 0) return false;

            var same_or_nested = string.Equals(a.AccFolderPath, b.AccFolderPath, StringComparison.OrdinalIgnoreCase)
                                 || IsUnderFolder(a.AccFolderPath, b.AccFolderPath)
                                 || IsUnderFolder(b.AccFolderPath, a.AccFolderPath);
            return same_or_nested && AreExtensionsOverlapping(a, b);
        }

        /// <summary> glob（** は / を含む任意の文字列、* は / 以外の任意の文字列、? は / 以外の1文字）がパス全体に当たるか。大文字小文字は区別しない </summary>
        internal static bool IsGlobMatch(string pattern, string path)
        {
            if (!GLOB_CACHE.TryGetValue(pattern, out var regex))
            {
                regex = new Regex(ConvertGlobToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                GLOB_CACHE[pattern] = regex;
            }
            return regex.IsMatch(path);
        }

        // フォルダの配下か（フォルダ自身は含まない。Assets/App は Assets/AppX/ の親ではない）
        private static bool IsUnderFolder(string path, string folder_path)
        {
            if (folder_path.Length == 0) return false;
            return path.Length > folder_path.Length + 1
                   && path[folder_path.Length] == '/'
                   && path.StartsWith(folder_path, StringComparison.OrdinalIgnoreCase);
        }

        // 区切りが \ のときだけ / にそろえる（\ が無ければ、文字列を確保しない）
        private static string NormalizeSeparator(string path)
        {
            return path.IndexOf('\\') >= 0 ? path.Replace('\\', '/') : path;
        }

        // パスの拡張子が、extension（小文字・ドットなし）と同じか。大文字小文字を区別せず、文字列を確保しない。
        // 拡張子が無い（ドットが無い・最後の / より前にある・ドットで終わる）パスは、どの拡張子にも当たらない
        private static bool HasExtension(string path, string extension)
        {
            var dot = path.LastIndexOf('.');
            if (dot < 0 || path.LastIndexOf('/') > dot) return false;

            var length = path.Length - dot - 1;
            return length == extension.Length
                   && string.Compare(path, dot + 1, extension, 0, length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        // パスの拡張子が、extensions（小文字・ドットなしに正規化済み）のどれかに当たるか
        private static bool HasAnyExtension(IReadOnlyList<string> extensions, string path)
        {
            for (var i = 0; i < extensions.Count; i++)
            {
                if (HasExtension(path, extensions[i])) return true;
            }
            return false;
        }

        private static bool IsExtensionTarget(RegistrationRuleInput rule, string path)
        {
            if (rule.AccTargetMode == RegistrationTargetMode.All)
                return !HasAnyExtension(rule.AccExcludedExtensions, path);

            // 拡張子の指定が空のときは、何も対象にしない（うっかり全部を登録する事故を防ぐ）
            return HasAnyExtension(rule.AccExtensions, path);
        }

        private static bool AreExtensionsOverlapping(RegistrationRuleInput a, RegistrationRuleInput b)
        {
            var a_all = a.AccTargetMode == RegistrationTargetMode.All;
            var b_all = b.AccTargetMode == RegistrationTargetMode.All;
            // All 同士は、必ず何かが重なる（特別扱いして優先させない）
            if (a_all && b_all) return true;
            if (a_all) return HasExtensionNotIn(b.AccExtensions, a.AccExcludedExtensions);
            if (b_all) return HasExtensionNotIn(a.AccExtensions, b.AccExcludedExtensions);

            for (var i = 0; i < a.AccExtensions.Count; i++)
            {
                if (Contains(b.AccExtensions, a.AccExtensions[i])) return true;
            }
            return false;
        }

        // extensions のうち、excluded に含まれない拡張子が1つでもあるか
        private static bool HasExtensionNotIn(IReadOnlyList<string> extensions, IReadOnlyList<string> excluded)
        {
            for (var i = 0; i < extensions.Count; i++)
            {
                if (!Contains(excluded, extensions[i])) return true;
            }
            return false;
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }
            return false;
        }

        // "**/" は 0 個以上のフォルダ、"**" は任意の文字列、"*" は / 以外の任意の文字列、"?" は / 以外の1文字
        private static string ConvertGlobToRegex(string pattern)
        {
            var builder = new StringBuilder("^");
            var i = 0;
            while (i < pattern.Length)
            {
                var c = pattern[i];
                if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                    {
                        builder.Append("(?:.*/)?");
                        i += 3;
                    }
                    else
                    {
                        builder.Append(".*");
                        i += 2;
                    }
                }
                else if (c == '*')
                {
                    builder.Append("[^/]*");
                    i++;
                }
                else if (c == '?')
                {
                    builder.Append("[^/]");
                    i++;
                }
                else
                {
                    builder.Append(Regex.Escape(c.ToString()));
                    i++;
                }
            }
            return builder.Append('$').ToString();
        }
    }
}
