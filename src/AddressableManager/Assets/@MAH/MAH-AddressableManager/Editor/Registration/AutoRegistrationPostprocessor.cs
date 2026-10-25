using System.Collections.Generic;
using System.IO;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Editor.SettingsSync;
using UnityEditor;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// 自動登録の入口。アセットのインポート・移動・名前の変更のたびに、ルールの SO に従って Addressables に登録・更新する（仕様 2章）。
    /// 流れは「入力を集める（Scanner）→ 計画を作る（PlanBuilder）→ 書き込む（Writer）→ ログを出す」。
    /// Editor SO の自動登録スイッチがオフのときは、最初に return して何もしない。
    /// 何度実行しても同じ結果になり、差分がなければ書き込まない（Writer を呼ばない）。
    /// </summary>
    internal sealed class AutoRegistrationPostprocessor : AssetPostprocessor
    {
        private const string ASSET_EXTENSION = ".asset";

        // 毎回確保しないよう使い回す（Editor のメインスレッドでのみ使う）
        private static readonly List<RegistrationAssetInput> ASSETS = new();
        private static readonly RegistrationLogOutput LOG_OUTPUT = new();

        // 書き込みの途中で、再びこの処理に入らないようにする
        private static bool s_isRunning;
        // Editor SO のキャッシュ（インポートのたびに FindAssets しないため）。破棄されたら探し直す
        private static AddressableEditorSettingsSO s_settingsSo;

        // Unity が、インポート・削除・移動のあとに呼ぶ
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (s_isRunning) return;

            // ルールの SO が追加・削除・変更されていたら、キャッシュを作り直す。
            // 自動登録がオフの間の変更も取りこぼさないよう、スイッチの判定より前に行う。
            // フォルダの移動・削除でも捨てる（ルールの入力は対象フォルダのパスを持つため、名前の変更で古くなる）
            if (HasRuleAssetChanged(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths)
                || ContainsFolder(movedAssets) || ContainsDeletedFolder(deletedAssets))
                RegistrationRuleScanner.InvalidateRules();

            var registration_settings = GetRegistrationSettings();
            if (!registration_settings.AccAutoRegistration) return;

            var settings = RegistrationRuleScanner.GetSettings();
            if (settings == null) return;
            var rules = RegistrationRuleScanner.GetRuleInputs();
            if (!HasEnabledRule(rules)) return;

            RegistrationRuleScanner.CollectImportedAssets(settings, rules, importedAssets, movedAssets, movedFromAssetPaths, ASSETS);
            if (ASSETS.Count == 0) return;

            s_isRunning = true;
            try
            {
                var environment = RegistrationRuleScanner.CreateEnvironment(settings);
                var plan = RegistrationPlanBuilder.BuildAuto(rules, ASSETS, environment);

                // 差分がなければ書き込まない（チームの別のメンバーが登録したアセットを pull しても、結果が同じなら何も起きない）
                var result = plan.AccHasChanges
                    ? RegistrationEntryWriter.Apply(settings, plan, registration_settings.AccDefaultGroupTemplate, false)
                    : default;

                RegistrationLogFormatter.Format(plan, registration_settings.AccLogDetail, LOG_OUTPUT);
                RegistrationLogPrinter.Print(LOG_OUTPUT);
                if (result.AccSkippedCount > 0) AddressableLogger.LogWarning(RegistrationLogFormatter.FormatSkipped(result.AccSkippedCount));
            }
            finally
            {
                s_isRunning = false;
                ASSETS.Clear();
            }
        }

        // 登録の設定を返す。SO が無いときは既定値（自動登録オン）
        private static AddressableRegistrationSettings GetRegistrationSettings()
        {
            if (s_settingsSo == null) s_settingsSo = AddressableEditorSettingsLocator.FindSettings();
            return s_settingsSo != null ? s_settingsSo.AccRegistration : new AddressableRegistrationSettings();
        }

        private static bool HasEnabledRule(IReadOnlyList<RegistrationRuleInput> rules)
        {
            for (var i = 0; i < rules.Count; i++)
            {
                if (rules[i].AccEnabled) return true;
            }
            return false;
        }

        // .asset のうち、ルールの SO が追加・変更・移動されたか、.asset が削除・移動元になったか
        private static bool HasRuleAssetChanged(string[] imported, string[] deleted, string[] moved, string[] moved_from)
        {
            return ContainsRuleAsset(imported) || ContainsRuleAsset(moved) || ContainsAsset(deleted) || ContainsAsset(moved_from);
        }

        private static bool ContainsRuleAsset(string[] paths)
        {
            for (var i = 0; i < paths.Length; i++)
            {
                if (paths[i].EndsWith(ASSET_EXTENSION) && AssetDatabase.GetMainAssetTypeAtPath(paths[i]) == typeof(AddressableAutoRegistrationRuleSO))
                    return true;
            }
            return false;
        }

        // 移動・名前の変更されたものにフォルダが含まれるか
        private static bool ContainsFolder(string[] paths)
        {
            for (var i = 0; i < paths.Length; i++)
            {
                if (AssetDatabase.IsValidFolder(paths[i])) return true;
            }
            return false;
        }

        // 削除されたものの種別は調べられないため、拡張子のないパスをフォルダとみなす（拡張子のないファイルでも、捨てて再計算するだけで害はない）
        private static bool ContainsDeletedFolder(string[] paths)
        {
            for (var i = 0; i < paths.Length; i++)
            {
                if (!Path.HasExtension(paths[i])) return true;
            }
            return false;
        }

        // 削除されたものの型は調べられないため、.asset であれば作り直す
        private static bool ContainsAsset(string[] paths)
        {
            for (var i = 0; i < paths.Length; i++)
            {
                if (paths[i].EndsWith(ASSET_EXTENSION)) return true;
            }
            return false;
        }
    }
}
