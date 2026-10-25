using System.Collections.Generic;
using MAH.AddressableManager.Editor.Localization;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// ルール1つ分の Inspector のプレビューを作る（仕様 3.6）。PlanBuilder と同じ Matcher・Expander を使うため、実際の登録と同じ結果が見える。
    /// Unity に依存しない（偽の入力で EditMode テストするため）。対象のアセットの収集は呼び出し側（Scanner）が行う。
    /// </summary>
    internal static class RegistrationRulePreviewBuilder
    {
        /// <summary> 展開結果を出す件数 </summary>
        internal const int SAMPLE_COUNT = 3;

        private static readonly List<string> MACRO_WARNINGS = new();

        /// <summary>
        /// プレビューを作る。folder_assets は対象フォルダの配下のアセット（このルールに当たるかは、ここで判定する）。
        /// all_rules は、重なりを調べるための全ルール（このルール自身を含んでよい）。
        /// </summary>
        internal static RegistrationRulePreview Build(
            RegistrationRuleInput rule,
            IReadOnlyList<RegistrationAssetInput> folder_assets,
            IReadOnlyList<RegistrationRuleInput> all_rules,
            RegistrationEnvironment env)
        {
            var preview = new RegistrationRulePreview();
            AddRuleWarnings(preview, rule, all_rules);
            if (rule.AccFolderPath.Length == 0) return preview;

            var matched = new List<RegistrationAssetInput>();
            for (var i = 0; i < folder_assets.Count; i++)
            {
                var asset = folder_assets[i];
                if (RegistrationRuleMatcher.GetExclusionKind(asset.AccPath) != RegistrationExclusionKind.None) continue;
                if (RegistrationRuleMatcher.IsMatch(rule, asset.AccPath)) matched.Add(asset);
            }
            matched.Sort((a, b) => string.CompareOrdinal(a.AccPath, b.AccPath));
            preview.AccTargetCount = matched.Count;

            var sample_count = matched.Count < SAMPLE_COUNT ? matched.Count : SAMPLE_COUNT;
            for (var i = 0; i < sample_count; i++) preview.AccSamples.Add(CreateSample(rule, matched[i], env, preview.AccWarnings));
            return preview;
        }

        // ルールそのものの注意（アセットを集めなくても分かるもの）
        private static void AddRuleWarnings(RegistrationRulePreview preview, RegistrationRuleInput rule, IReadOnlyList<RegistrationRuleInput> all_rules)
        {
            if (!rule.AccEnabled)
                preview.AccWarnings.Add(EditorText.Get("rulePreview.warn.disabled"));

            if (rule.AccFolderPath.Length == 0)
                preview.AccWarnings.Add(EditorText.Get("rulePreview.warn.noFolder"));

            if (rule.AccTargetMode == RegistrationTargetMode.Extensions && rule.AccExtensions.Count == 0)
                preview.AccWarnings.Add(EditorText.Get("rulePreview.warn.noExtensions"));

            if (rule.AccTargetMode == RegistrationTargetMode.All)
                preview.AccWarnings.Add(EditorText.Get("rulePreview.warn.all"));

            for (var i = 0; i < all_rules.Count; i++)
            {
                var other = all_rules[i];
                if (other.AccGuid.Length > 0 && other.AccGuid == rule.AccGuid) continue;
                if (RegistrationRuleMatcher.AreOverlapping(rule, other))
                    preview.AccWarnings.Add(EditorText.Format("rulePreview.warn.overlap", other.AccName));
            }
        }

        // アセット1件分の展開結果。知らないマクロ・使えないグループ名は、警告に足す（同じ警告は1回だけ）
        private static RegistrationPreviewSample CreateSample(
            RegistrationRuleInput rule,
            RegistrationAssetInput asset,
            RegistrationEnvironment env,
            List<string> warnings)
        {
            var context = new RegistrationMacroContext(asset.AccPath, asset.AccGuid, asset.AccTypeName, rule.AccFolderPath, rule.AccName, env.AccStandardValues);

            var address = Expand(rule.AccAddressTemplate, context, asset.AccPath, warnings);
            if (address.Length == 0) address = asset.AccPath;

            var group = Expand(rule.AccTargetGroup, context, asset.AccPath, warnings);
            if (group.Length == 0)
                group = env.AccDefaultGroupName;
            else if (!RegistrationMacroExpander.IsValidGroupName(group))
                AddOnce(warnings, EditorText.Format("registration.pathMessage", asset.AccPath, EditorText.Format("rulePreview.warn.invalidGroup", group)));

            var labels = new List<string>(rule.AccLabels.Count);
            for (var i = 0; i < rule.AccLabels.Count; i++)
            {
                var label = Expand(rule.AccLabels[i], context, asset.AccPath, warnings);
                if (label.Length > 0 && !labels.Contains(label)) labels.Add(label);
            }
            return new RegistrationPreviewSample(asset.AccPath, address, group, labels);
        }

        private static string Expand(string template, RegistrationMacroContext context, string asset_path, List<string> warnings)
        {
            MACRO_WARNINGS.Clear();
            var expanded = RegistrationMacroExpander.Expand(template, context, MACRO_WARNINGS);
            for (var i = 0; i < MACRO_WARNINGS.Count; i++) AddOnce(warnings, EditorText.Format("registration.pathMessage", asset_path, MACRO_WARNINGS[i]));
            return expanded;
        }

        private static void AddOnce(List<string> list, string message)
        {
            if (!list.Contains(message)) list.Add(message);
        }
    }
}
