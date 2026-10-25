using MAH.AddressableManager.Core;
using MAH.AddressableManager.Editor.Localization;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// 計画（RegistrationPlan）を AddressableAssetSettings に書き込む。
    /// <b>AddressableAssetSettings に書き込むのは、本モジュールではこのクラスだけ</b>（Editor 拡張は読み取り専用という原則の、唯一の例外。CLAUDE.md の API ルール）。
    /// 大量に登録するときの再描画・保存が件数分走らないよう、各操作では通知を止め（postEvent = false）、最後に1回だけまとめて通知する。
    /// </summary>
    internal static class RegistrationEntryWriter
    {
        private const int PROGRESS_INTERVAL = 20;

        /// <summary>
        /// 計画を書き込む。差分がなければ何もしない。
        /// show_progress のときは、キャンセルできる進捗バーを出す。キャンセルしたら、そこまでの分は書き込まれた状態で止まる。
        /// fallback_template は、Group Template が指定されていない新しいグループに使う（null なら Addressables の既定の Template）。
        /// </summary>
        internal static RegistrationApplyResult Apply(
            AddressableAssetSettings settings,
            RegistrationPlan plan,
            AddressableAssetGroupTemplate fallback_template,
            bool show_progress)
        {
            if (settings == null || !plan.AccHasChanges) return default;

            var applied = 0;
            var failed = 0;
            var skipped = 0;
            var canceled = false;
            try
            {
                for (var i = 0; i < plan.AccNewGroups.Count; i++) CreateGroupIfMissing(settings, plan.AccNewGroups[i], fallback_template);

                for (var i = 0; i < plan.AccChanges.Count; i++)
                {
                    if (show_progress && i % PROGRESS_INTERVAL == 0
                        && EditorUtility.DisplayCancelableProgressBar(EditorText.Get("registration.progress.writing"), plan.AccChanges[i].AccAssetPath, (float)i / plan.AccChanges.Count))
                    {
                        canceled = true;
                        break;
                    }

                    switch (ApplyChange(settings, plan.AccChanges[i]))
                    {
                        case ApplyOutcome.Applied:
                            applied++;
                            break;
                        case ApplyOutcome.Skipped:
                            skipped++;
                            break;
                        default:
                            failed++;
                            break;
                    }
                }
            }
            finally
            {
                if (show_progress) EditorUtility.ClearProgressBar();
                // 途中で止まっても、そこまでの変更は通知して保存の対象にする
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            }
            return new RegistrationApplyResult(applied, failed, skipped, canceled);
        }

        // 変更1件の書き込みの結果
        private enum ApplyOutcome
        {
            Applied,
            Failed,
            Skipped,
        }

        // 変更1件を書き込む。計画を作った時点から状態が変わっていればスキップし、グループやエントリが見つからなければ警告して Failed を返す
        private static ApplyOutcome ApplyChange(AddressableAssetSettings settings, RegistrationChange change)
        {
            // 件数は結果で返し、呼び出し側がまとめて知らせる（大量にあっても Console があふれないよう、ここでは出さない）
            if (IsStale(settings.FindAssetEntry(change.AccAssetGuid), change)) return ApplyOutcome.Skipped;

            var group = settings.FindGroup(change.AccToGroup);
            if (group == null)
            {
                AddressableLogger.LogWarning(RegistrationLogFormatter.Prefix + EditorText.Format("registration.writer.groupNotFound", change.AccToGroup, change.AccAssetPath));
                return ApplyOutcome.Failed;
            }

            var entry = change.AccIsNewEntry || change.AccGroupChanged
                ? settings.CreateOrMoveEntry(change.AccAssetGuid, group, false, false)
                : settings.FindAssetEntry(change.AccAssetGuid);
            if (entry == null)
            {
                AddressableLogger.LogWarning(RegistrationLogFormatter.Prefix + EditorText.Format("registration.writer.entryFailed", change.AccAssetPath));
                return ApplyOutcome.Failed;
            }

            if (change.AccAddressChanged) entry.SetAddress(change.AccToAddress, false);
            // force を付けると、設定に無い Label も作られる
            for (var i = 0; i < change.AccLabelsToAdd.Count; i++) entry.SetLabel(change.AccLabelsToAdd[i], true, true, false);
            for (var i = 0; i < change.AccLabelsToRemove.Count; i++) entry.SetLabel(change.AccLabelsToRemove[i], false, false, false);
            return ApplyOutcome.Applied;
        }

        // 計画の「変更前」と今のエントリが食い違うか。ウィンドウを開いたままの間などに状態が変わると、見せた差分と違う内容を書いてしまうため確かめる
        private static bool IsStale(AddressableAssetEntry entry, RegistrationChange change)
        {
            // 新規のはずが、すでに登録されている
            if (change.AccIsNewEntry) return entry != null;

            // 既存のエントリのはずが、無くなっている・グループやアドレスが変わっている
            return entry == null
                   || entry.parentGroup == null
                   || entry.parentGroup.Name != change.AccFromGroup
                   || entry.address != change.AccFromAddress;
        }

        // 無いグループだけを作る。Template は、指定 → 引数の既定 → Addressables の最初の Template の順に使う
        private static void CreateGroupIfMissing(AddressableAssetSettings settings, RegistrationGroupRequest request, AddressableAssetGroupTemplate fallback_template)
        {
            if (settings.FindGroup(request.AccName) != null) return;

            var template = LoadTemplate(request.AccTemplateAssetGuid) ?? fallback_template ?? FindFirstTemplate(settings);
            if (template != null)
            {
                var group = settings.CreateGroup(request.AccName, false, false, false, null, template.GetTypes());
                template.ApplyToAddressableAssetGroup(group);
                return;
            }

            // Template が1つも無いときは、標準のスキーマで作る
            settings.CreateGroup(request.AccName, false, false, false, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        }

        private static AddressableAssetGroupTemplate LoadTemplate(string template_asset_guid)
        {
            if (string.IsNullOrEmpty(template_asset_guid)) return null;
            return AssetDatabase.LoadAssetAtPath<AddressableAssetGroupTemplate>(AssetDatabase.GUIDToAssetPath(template_asset_guid));
        }

        private static AddressableAssetGroupTemplate FindFirstTemplate(AddressableAssetSettings settings)
        {
            foreach (var template_object in settings.GroupTemplateObjects)
            {
                if (template_object is AddressableAssetGroupTemplate template) return template;
            }
            return null;
        }
    }
}
