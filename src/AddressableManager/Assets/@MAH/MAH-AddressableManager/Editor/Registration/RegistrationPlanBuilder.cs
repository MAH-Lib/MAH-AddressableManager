using System;
using System.Collections.Generic;
using MAH.AddressableManager.Editor.Localization;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// 今のエントリの状態と、当てるルールから、差分（登録・グループの移動・アドレスの変更・Label の増減）と、警告・エラー・競合・アドレスの重複を求める。
    /// 差分がなければ計画は空になる。同じ入力からは、必ず同じ計画ができる（アセットパス順に処理する）。
    /// 自動登録・当て直しは BuildAuto、手動一括登録は BuildManualRows → BuildManual を通る。Unity に依存しない。
    /// </summary>
    internal static class RegistrationPlanBuilder
    {
        // 展開の警告の一時置き場と、当たるルールの一時置き場（毎回確保しないよう使い回す。Editor のメインスレッドでのみ使う）
        private static readonly List<string> MACRO_WARNINGS = new();
        private static readonly List<RegistrationRuleInput> MATCHES = new();

        /// <summary>
        /// 自動登録・当て直しの計画を作る（仕様 2.2）。
        /// 競合したアセットは、どのルールでも登録・変更しない。アドレスの重複は、登録はして記録する。
        /// </summary>
        internal static RegistrationPlan BuildAuto(
            IReadOnlyList<RegistrationRuleInput> rules,
            IReadOnlyList<RegistrationAssetInput> assets,
            RegistrationEnvironment env)
        {
            var plan = new RegistrationPlan();
            var targets = new List<RegistrationEntryInput>();
            var conflicts = new Dictionary<string, RegistrationConflict>();

            var sorted = SortByPath(assets);
            for (var i = 0; i < sorted.Count; i++)
            {
                var asset = sorted[i];
                var exclusion = RegistrationRuleMatcher.GetExclusionKind(asset.AccPath);
                if (exclusion == RegistrationExclusionKind.Ignored) continue;

                if (exclusion == RegistrationExclusionKind.Resources)
                {
                    // Resources の配下は、ルールに当たるときだけ警告する（当たらないものまで警告すると多すぎる）
                    if (AnyRuleMatches(rules, asset.AccPath))
                        plan.AccWarnings.Add(EditorText.Format("registration.warn.resources", asset.AccPath));
                    continue;
                }

                RegistrationRuleMatcher.FindMatches(rules, asset.AccPath, MATCHES);
                if (MATCHES.Count == 0)
                {
                    // 移動・名前の変更で、どのルールにも当たらなくなった登録済みのアセットは、登録を外さず警告する
                    if (asset.AccIsRegistered && asset.AccMovedFromPath.Length > 0 && AnyRuleMatches(rules, asset.AccMovedFromPath))
                        plan.AccWarnings.Add(EditorText.Format("registration.warn.noLongerMatches", asset.AccMovedFromPath, asset.AccPath));
                    continue;
                }

                if (MATCHES.Count > 1)
                {
                    AddConflict(conflicts, plan, MATCHES, asset.AccPath);
                    continue;
                }

                if (!TryEvaluate(MATCHES[0], asset, env, false, plan.AccWarnings, out var error, out _, out var change, out var resulting))
                {
                    plan.AccErrors.Add(error);
                    continue;
                }

                targets.Add(resulting);
                if (change != null) AddChange(plan, change, MATCHES[0].AccGroupTemplateAssetGuid, env);
            }

            DetectDuplicates(plan, targets, env);
            return plan;
        }

        /// <summary>
        /// 手動一括登録の一覧の行を作る（仕様 5.3）。spec は画面の指定をルールの入力と同じ形にしたもの。
        /// rules は自動登録のルール（自動登録がオフのときは空を渡す）。rows は最初に Clear する。
        /// </summary>
        internal static void BuildManualRows(
            RegistrationRuleInput spec,
            IReadOnlyList<RegistrationRuleInput> rules,
            IReadOnlyList<RegistrationAssetInput> assets,
            RegistrationEnvironment env,
            List<RegistrationManualRow> rows)
        {
            rows.Clear();
            var sorted = SortByPath(assets);
            for (var i = 0; i < sorted.Count; i++)
            {
                var asset = sorted[i];
                // いつも登録しないもの（Resources を含む）は表示しない
                if (RegistrationRuleMatcher.GetExclusionKind(asset.AccPath) != RegistrationExclusionKind.None) continue;
                if (!RegistrationRuleMatcher.IsMatch(spec, asset.AccPath)) continue;

                var warnings = new List<string>();
                var evaluated = TryEvaluate(spec, asset, env, true, warnings, out var error, out var address, out var change, out _);
                RegistrationRuleMatcher.FindMatches(rules, asset.AccPath, MATCHES);

                if (MATCHES.Count > 1)
                {
                    rows.Add(new RegistrationManualRow(asset.AccPath, ManualRegistrationState.Conflicting, asset.AccCurrentGroup, string.Empty, address, string.Empty, null, warnings));
                }
                else if (MATCHES.Count == 1)
                {
                    // 自動登録のルールの内容と一致していれば「自動登録」、まだ当て直していなければ「自動登録の対象（未適用）」
                    var rule = MATCHES[0];
                    var applied = TryEvaluate(rule, asset, env, false, new List<string>(), out _, out _, out var auto_change, out _) && auto_change == null;
                    var state = applied ? ManualRegistrationState.AutoRegistered : ManualRegistrationState.PendingAuto;
                    rows.Add(new RegistrationManualRow(asset.AccPath, state, asset.AccCurrentGroup, rule.AccName, address, string.Empty, null, warnings));
                }
                else
                {
                    var state = asset.AccIsRegistered ? ManualRegistrationState.Registered : ManualRegistrationState.Unregistered;
                    rows.Add(new RegistrationManualRow(asset.AccPath, state, asset.AccCurrentGroup, string.Empty, address, evaluated ? string.Empty : error, change, warnings));
                }
            }
        }

        /// <summary>
        /// 手動一括登録の計画を作る。チェックが入っていて、チェックできる行の変更だけを集める。
        /// 登録済みのアセットは、グループの移動とアドレスの書き換えを行う。Label は追加だけ（外さない）。
        /// </summary>
        internal static RegistrationPlan BuildManual(RegistrationRuleInput spec, IReadOnlyList<RegistrationManualRow> rows, RegistrationEnvironment env)
        {
            var plan = new RegistrationPlan();
            var targets = new List<RegistrationEntryInput>();

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (!row.AccChecked || !row.AccCanCheck) continue;

                for (var j = 0; j < row.AccWarnings.Count; j++) plan.AccWarnings.Add(row.AccWarnings[j]);
                var change = row.AccChange;
                if (change == null) continue;

                targets.Add(new RegistrationEntryInput(change.AccAssetPath, change.AccToAddress, change.AccToGroup));
                AddChange(plan, change, spec.AccGroupTemplateAssetGuid, env);
            }

            DetectDuplicates(plan, targets, env);
            return plan;
        }

        // アセットパス順の一覧を作る（入力の並びに依存せず、同じ入力から同じ計画を作るため）
        private static List<RegistrationAssetInput> SortByPath(IReadOnlyList<RegistrationAssetInput> assets)
        {
            var sorted = new List<RegistrationAssetInput>(assets.Count);
            for (var i = 0; i < assets.Count; i++) sorted.Add(assets[i]);
            sorted.Sort((a, b) => string.CompareOrdinal(a.AccPath, b.AccPath));
            return sorted;
        }

        private static bool AnyRuleMatches(IReadOnlyList<RegistrationRuleInput> rules, string asset_path)
        {
            for (var i = 0; i < rules.Count; i++)
            {
                if (RegistrationRuleMatcher.IsMatch(rules[i], asset_path)) return true;
            }
            return false;
        }

        // 競合を、ルールの組ごとにまとめて記録する
        private static void AddConflict(Dictionary<string, RegistrationConflict> conflicts, RegistrationPlan plan, List<RegistrationRuleInput> matches, string asset_path)
        {
            var ids = new List<string>(matches.Count);
            var pairs = new List<KeyValuePair<string, string>>(matches.Count);
            for (var i = 0; i < matches.Count; i++)
            {
                ids.Add(matches[i].AccGuid.Length > 0 ? matches[i].AccGuid : matches[i].AccName);
                pairs.Add(new KeyValuePair<string, string>(matches[i].AccName, matches[i].AccGuid));
            }
            ids.Sort(string.CompareOrdinal);
            // 名前と GUID は組のまま名前順に並べる（別々に並べると、同じ名前の SO や並びの違いで対応がずれる）
            pairs.Sort((a, b) =>
            {
                var by_name = string.CompareOrdinal(a.Key, b.Key);
                return by_name != 0 ? by_name : string.CompareOrdinal(a.Value, b.Value);
            });

            var key = string.Join("|", ids);
            if (!conflicts.TryGetValue(key, out var conflict))
            {
                var names = new List<string>(pairs.Count);
                var guids = new List<string>(pairs.Count);
                for (var i = 0; i < pairs.Count; i++)
                {
                    names.Add(pairs[i].Key);
                    guids.Add(pairs[i].Value);
                }
                conflict = new RegistrationConflict(names, guids);
                conflicts[key] = conflict;
                plan.AccConflicts.Add(conflict);
            }
            conflict.AccAssetPaths.Add(asset_path);
        }

        // 変更を計画に足し、新しく作るグループ・Label を（重複なく）記録する
        private static void AddChange(RegistrationPlan plan, RegistrationChange change, string group_template_asset_guid, RegistrationEnvironment env)
        {
            plan.AccChanges.Add(change);

            if (!env.HasGroup(change.AccToGroup) && !ContainsGroup(plan.AccNewGroups, change.AccToGroup))
                plan.AccNewGroups.Add(new RegistrationGroupRequest(change.AccToGroup, group_template_asset_guid));

            for (var i = 0; i < change.AccLabelsToAdd.Count; i++)
            {
                var label = change.AccLabelsToAdd[i];
                if (!env.HasLabel(label) && !plan.AccNewLabels.Contains(label)) plan.AccNewLabels.Add(label);
            }
        }

        private static bool ContainsGroup(List<RegistrationGroupRequest> groups, string name)
        {
            for (var i = 0; i < groups.Count; i++)
            {
                if (groups[i].AccName == name) return true;
            }
            return false;
        }

        /// <summary>
        /// 1つのアセットについて、ルールを当てた結果を求める。
        /// 失敗（グループ名に使えない文字）のときは false と error を返す。差分がなければ change は null（resulting は常に求める）。
        /// is_manual のときは、Label は追加だけにし、アドレスの変更を警告しない（手動の操作で書き換えるため）。
        /// </summary>
        private static bool TryEvaluate(
            RegistrationRuleInput rule,
            RegistrationAssetInput asset,
            RegistrationEnvironment env,
            bool is_manual,
            List<string> warnings,
            out string error,
            out string expanded_address,
            out RegistrationChange change,
            out RegistrationEntryInput resulting)
        {
            error = string.Empty;
            change = null;
            resulting = null;

            var context = new RegistrationMacroContext(asset.AccPath, asset.AccGuid, asset.AccTypeName, rule.AccFolderPath, rule.AccName, env.AccStandardValues);

            // アドレス：空ならアセットパス。登録済みで固定のルールは、今のアドレスを変えない
            var address = Expand(rule.AccAddressTemplate, context, asset.AccPath, warnings);
            if (address.Length == 0) address = asset.AccPath;
            if (asset.AccIsRegistered && rule.AccAddressMode == RegistrationAddressMode.Fixed && asset.AccCurrentAddress.Length > 0)
                address = asset.AccCurrentAddress;
            expanded_address = address;

            // グループ：空なら DefaultGroup。グループ名に使えない文字が入るときは登録しない
            var group = Expand(rule.AccTargetGroup, context, asset.AccPath, warnings);
            if (group.Length == 0)
            {
                group = env.AccDefaultGroupName;
            }
            else if (!RegistrationMacroExpander.IsValidGroupName(group))
            {
                error = EditorText.Format("registration.error.invalidGroupName", asset.AccPath, group);
                return false;
            }

            var labels = new List<string>(rule.AccLabels.Count);
            for (var i = 0; i < rule.AccLabels.Count; i++)
            {
                var label = Expand(rule.AccLabels[i], context, asset.AccPath, warnings);
                if (label.Length > 0 && !labels.Contains(label)) labels.Add(label);
            }

            resulting = new RegistrationEntryInput(asset.AccPath, address, group);

            var labels_to_add = new List<string>();
            var labels_to_remove = new List<string>();
            for (var i = 0; i < labels.Count; i++)
            {
                if (!Contains(asset.AccCurrentLabels, labels[i])) labels_to_add.Add(labels[i]);
            }
            if (!is_manual)
            {
                for (var i = 0; i < asset.AccCurrentLabels.Count; i++)
                {
                    if (!labels.Contains(asset.AccCurrentLabels[i])) labels_to_remove.Add(asset.AccCurrentLabels[i]);
                }
            }

            if (!asset.AccIsRegistered)
            {
                change = new RegistrationChange(asset.AccPath, asset.AccGuid, rule.AccName, true, string.Empty, group, string.Empty, address, labels_to_add, labels_to_remove);
                return true;
            }

            var group_changed = asset.AccCurrentGroup != group;
            var address_changed = asset.AccCurrentAddress != address;
            if (!group_changed && !address_changed && labels_to_add.Count == 0 && labels_to_remove.Count == 0) return true;

            // 追従のルールでアドレスが変わるときは、コードに書いたアドレスが壊れ得るため、変更前後を警告する
            if (address_changed && !is_manual)
                warnings.Add(EditorText.Format("registration.warn.addressChange", asset.AccCurrentAddress, address, asset.AccPath));

            change = new RegistrationChange(asset.AccPath, asset.AccGuid, rule.AccName, false, asset.AccCurrentGroup, group, asset.AccCurrentAddress, address, labels_to_add, labels_to_remove);
            return true;
        }

        // テンプレートを展開し、展開の警告をアセットパス付きで warnings に足す
        private static string Expand(string template, RegistrationMacroContext context, string asset_path, List<string> warnings)
        {
            MACRO_WARNINGS.Clear();
            var expanded = RegistrationMacroExpander.Expand(template, context, MACRO_WARNINGS);
            for (var i = 0; i < MACRO_WARNINGS.Count; i++) warnings.Add(EditorText.Format("registration.pathMessage", asset_path, MACRO_WARNINGS[i]));
            return expanded;
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return true;
            }
            return false;
        }

        /// <summary>
        /// アドレスの重複を調べる（仕様 7.1）。この計画の対象のアセット（targets）のアドレスが、
        /// すべての既存のエントリ（計画で変わるものは、変わった後の状態）の別のアセットと同じなら、重複として記録する。
        /// 対象を含まない既存どうしの重複は、この計画と関係ないため記録しない。
        /// </summary>
        private static void DetectDuplicates(RegistrationPlan plan, List<RegistrationEntryInput> targets, RegistrationEnvironment env)
        {
            if (targets.Count == 0) return;

            var target_paths = new HashSet<string>();
            for (var i = 0; i < targets.Count; i++) target_paths.Add(targets[i].AccAssetPath);

            var by_address = new Dictionary<string, List<RegistrationEntryInput>>();
            for (var i = 0; i < env.AccAllEntries.Count; i++)
            {
                var entry = env.AccAllEntries[i];
                if (target_paths.Contains(entry.AccAssetPath)) continue;
                AddByAddress(by_address, entry);
            }
            for (var i = 0; i < targets.Count; i++) AddByAddress(by_address, targets[i]);

            var reported = new HashSet<string>();
            for (var i = 0; i < targets.Count; i++)
            {
                var address = targets[i].AccAddress;
                if (address.Length == 0 || !reported.Add(address)) continue;
                if (by_address[address].Count > 1) plan.AccDuplicates.Add(new RegistrationDuplicate(address, by_address[address]));
            }
        }

        private static void AddByAddress(Dictionary<string, List<RegistrationEntryInput>> by_address, RegistrationEntryInput entry)
        {
            if (entry.AccAddress.Length == 0) return;
            if (!by_address.TryGetValue(entry.AccAddress, out var list))
            {
                list = new List<RegistrationEntryInput>();
                by_address[entry.AccAddress] = list;
            }
            list.Add(entry);
        }
    }
}
