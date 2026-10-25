using System.Collections.Generic;
using System.Text;
using MAH.AddressableManager.Editor.Localization;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// 計画（RegistrationPlan）から、ログの文字列を組み立てる（仕様 8章のログの細かさ・2.5・7.1）。
    /// まとめ／1件ずつ／競合の組ごとのまとめ／アドレスの重複。競合・重複・警告は、細かさに関係なく出す。
    /// 文言は EditorText から取る（表示言語に従う）。文の単位でキーにして、語順が違う言語でも訳せるようにする。
    /// Unity のオブジェクトには依存しない（文字列の組み立てを EditMode でテストするため）。
    /// </summary>
    internal static class RegistrationLogFormatter
    {
        /// <summary> 競合のログに載せる、アセットのパスの最大数（Console があふれないよう、最初の数件だけ出す） </summary>
        internal const int MAX_CONFLICT_PATHS = 5;

        /// <summary> 「まとめだけ」のとき、警告をそのまま出す最大数。これを超えると、1件に要約する </summary>
        internal const int MAX_SUMMARY_WARNINGS = 5;

        /// <summary> 差分がないときの表示用の文言（当て直す・手動一括登録の画面で使う） </summary>
        internal static string NoChangeMessage => EditorText.Get("registration.noChange");

        /// <summary> ログの先頭に付ける「アセットの登録：」 </summary>
        internal static string Prefix => EditorText.Get("registration.log.prefix");

        private static string ListSeparator => EditorText.Get("common.listSeparator");

        /// <summary>
        /// 計画をログの文字列にする。output は最初に Clear する。
        /// 差分があれば、まとめを Info に出す（PerAsset ならアセットごとの行も出す）。
        /// 警告は Warning、エラー・競合・アドレスの重複は Error に出す。
        /// </summary>
        internal static void Format(RegistrationPlan plan, RegistrationLogDetail detail, RegistrationLogOutput output)
        {
            output.Clear();

            if (plan.AccHasChanges)
            {
                output.AccInfos.Add(FormatSummary(plan));
                if (detail == RegistrationLogDetail.PerAsset)
                {
                    for (var i = 0; i < plan.AccChanges.Count; i++) output.AccInfos.Add(FormatChange(plan.AccChanges[i]));
                }
            }

            // 「まとめだけ」のときは、警告が多すぎて Console があふれないよう、1件に要約する（「1件ずつ」は全件出す）
            if (detail == RegistrationLogDetail.Summary && plan.AccWarnings.Count > MAX_SUMMARY_WARNINGS)
            {
                output.AccWarnings.Add(FormatWarningSummary(plan.AccWarnings));
            }
            else
            {
                for (var i = 0; i < plan.AccWarnings.Count; i++) output.AccWarnings.Add(Prefix + plan.AccWarnings[i]);
            }
            for (var i = 0; i < plan.AccErrors.Count; i++) output.AccErrors.Add(Prefix + plan.AccErrors[i]);
            for (var i = 0; i < plan.AccConflicts.Count; i++) output.AccErrors.Add(FormatConflict(plan.AccConflicts[i]));
            for (var i = 0; i < plan.AccDuplicates.Count; i++) output.AccErrors.Add(FormatDuplicate(plan.AccDuplicates[i]));
        }

        /// <summary> 1回の処理のまとめ。件数が 0 の項目は省く（例：アセットの登録：3 件を登録、1 件のグループを移動） </summary>
        internal static string FormatSummary(RegistrationPlan plan)
        {
            if (!plan.AccHasChanges) return NoChangeMessage;

            var parts = new List<string>(4);
            AddCount(parts, plan.CountAdded(), "registration.summary.added");
            AddCount(parts, plan.CountMoved(), "registration.summary.moved");
            AddCount(parts, plan.CountReaddressed(), "registration.summary.readdressed");
            AddCount(parts, plan.CountLabelChanged(), "registration.summary.labelChanged");
            return Prefix + string.Join(ListSeparator, parts);
        }

        /// <summary> 書き込まずにスキップした件数の知らせ（計画を作った時点から、エントリの状態が変わっていたもの） </summary>
        internal static string FormatSkipped(int skipped_count)
        {
            return Prefix + EditorText.Format("registration.skipped", skipped_count);
        }

        /// <summary>
        /// 手動一括登録の「登録する」を押したときの確認ダイアログの文言（仕様 5.4）。
        /// 登録とグループの移動の件数、新しく作るグループ・Label、アドレスの重複があれば、そのまま登録するかを尋ねる。
        /// </summary>
        internal static string FormatConfirmation(RegistrationPlan plan)
        {
            var builder = new StringBuilder();
            builder.Append(EditorText.Format("registration.confirm.summary", plan.CountAdded(), plan.CountMoved()));

            if (plan.AccNewGroups.Count > 0)
            {
                builder.Append("\n\n").Append(EditorText.Get("registration.confirm.newGroups"));
                for (var i = 0; i < plan.AccNewGroups.Count; i++) builder.Append(i > 0 ? ListSeparator : string.Empty).Append(plan.AccNewGroups[i].AccName);
            }
            if (plan.AccNewLabels.Count > 0)
                builder.Append(plan.AccNewGroups.Count > 0 ? "\n" : "\n\n").Append(EditorText.Get("registration.confirm.newLabels")).Append(string.Join(ListSeparator, plan.AccNewLabels));

            if (plan.AccDuplicates.Count > 0)
            {
                builder.Append("\n\n").Append(EditorText.Format("registration.confirm.duplicates", plan.AccDuplicates.Count));
                for (var i = 0; i < plan.AccDuplicates.Count; i++) builder.Append(i > 0 ? ListSeparator : string.Empty).Append(plan.AccDuplicates[i].AccAddress);
                builder.Append('\n').Append(EditorText.Get("registration.confirm.askAnyway"));
            }
            else
            {
                builder.Append("\n\n").Append(EditorText.Get("registration.confirm.ask"));
            }
            return builder.ToString();
        }

        /// <summary> アセット1件分の行（例：登録：Assets/A.prefab → [UI] Title (Label +Common -Old)） </summary>
        internal static string FormatChange(RegistrationChange change)
        {
            var builder = new StringBuilder();
            builder.Append(EditorText.Get(change.AccIsNewEntry ? "registration.change.new" : "registration.change.update"));
            builder.Append(change.AccAssetPath).Append(" → [").Append(change.AccToGroup).Append("] ").Append(change.AccToAddress);

            if (!change.AccIsNewEntry)
            {
                if (change.AccGroupChanged) builder.Append(EditorText.Format("registration.change.group", change.AccFromGroup, change.AccToGroup));
                if (change.AccAddressChanged) builder.Append(EditorText.Format("registration.change.address", change.AccFromAddress, change.AccToAddress));
            }
            if (change.AccLabelsChanged)
            {
                var labels = new StringBuilder();
                for (var i = 0; i < change.AccLabelsToAdd.Count; i++) labels.Append(labels.Length > 0 ? " +" : "+").Append(change.AccLabelsToAdd[i]);
                for (var i = 0; i < change.AccLabelsToRemove.Count; i++) labels.Append(labels.Length > 0 ? " -" : "-").Append(change.AccLabelsToRemove[i]);
                builder.Append(EditorText.Format("registration.change.labels", labels.ToString()));
            }
            return builder.ToString();
        }

        /// <summary> 警告を1件に要約する。件数と、最初の MAX_SUMMARY_WARNINGS 件を出す </summary>
        internal static string FormatWarningSummary(IReadOnlyList<string> warnings)
        {
            var builder = new StringBuilder();
            builder.Append(Prefix).Append(EditorText.Format("registration.warningSummary.header", warnings.Count, MAX_SUMMARY_WARNINGS));
            for (var i = 0; i < MAX_SUMMARY_WARNINGS && i < warnings.Count; i++) builder.Append("\n  ").Append(warnings[i]);
            builder.Append("\n  ").Append(EditorText.Format("registration.warningSummary.more", warnings.Count - MAX_SUMMARY_WARNINGS));
            return builder.ToString();
        }

        /// <summary> ルールの組ごとに1件にまとめた競合。件数と、最初の MAX_CONFLICT_PATHS 件のパスを出す </summary>
        internal static string FormatConflict(RegistrationConflict conflict)
        {
            var builder = new StringBuilder();
            builder.Append(Prefix).Append(EditorText.Format("registration.conflict.header", string.Join(ListSeparator, conflict.AccRuleNames), conflict.AccAssetPaths.Count));

            var shown = conflict.AccAssetPaths.Count < MAX_CONFLICT_PATHS ? conflict.AccAssetPaths.Count : MAX_CONFLICT_PATHS;
            for (var i = 0; i < shown; i++) builder.Append("\n  ").Append(conflict.AccAssetPaths[i]);
            if (conflict.AccAssetPaths.Count > shown)
                builder.Append("\n  ").Append(EditorText.Format("registration.conflict.more", conflict.AccAssetPaths.Count - shown));
            return builder.ToString();
        }

        /// <summary> アドレスの重複。同じアドレスを持つアセットと、そのグループを出す </summary>
        internal static string FormatDuplicate(RegistrationDuplicate duplicate)
        {
            var builder = new StringBuilder();
            builder.Append(Prefix).Append(EditorText.Format("registration.duplicate.header", duplicate.AccAddress));
            for (var i = 0; i < duplicate.AccEntries.Count; i++)
            {
                var entry = duplicate.AccEntries[i];
                builder.Append("\n  ").Append(EditorText.Format("rule.assetLink", entry.AccAssetPath, entry.AccGroupName));
            }
            return builder.ToString();
        }

        private static void AddCount(List<string> parts, int count, string key)
        {
            if (count > 0) parts.Add(EditorText.Format(key, count));
        }
    }
}
