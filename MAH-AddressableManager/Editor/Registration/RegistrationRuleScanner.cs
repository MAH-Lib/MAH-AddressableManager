using System;
using System.Collections.Generic;
using MAH.AddressableManager.Editor.SettingsSync;
using MAH.AddressableManager.Editor.Localization;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// Unity（AssetDatabase・AddressableAssetSettings）から読み取り、Matcher・PlanBuilder に渡す入力を作る。
    /// ルールの SO の探索はキャッシュし、追加・削除・変更のときだけ作り直す（インポートのたびに FindAssets しないため）。
    /// 読み取り専用で、AddressableAssetSettings には書き込まない（書き込みは RegistrationEntryWriter だけ）。
    /// </summary>
    internal static class RegistrationRuleScanner
    {
        private const string RULE_FILTER = "t:" + nameof(AddressableAutoRegistrationRuleSO);
        private const int PROGRESS_INTERVAL = 200;

        // ルールのキャッシュ。Editor のメインスレッドでのみ使う
        private static readonly List<AddressableAutoRegistrationRuleSO> RULE_ASSETS = new();
        private static readonly List<RegistrationRuleInput> RULE_INPUTS = new();
        private static bool s_isDirty = true;
        private static int s_ruleVersion;

        /// <summary> ルールの SO が追加・削除・変更されるたびに増える番号（ウィンドウが、表示中の結果が古くなったかを知るため） </summary>
        internal static int AccRuleVersion => s_ruleVersion;

        /// <summary> ルールの SO の追加・削除・変更があったとき、次に使うときに探し直すようにする </summary>
        internal static void InvalidateRules()
        {
            s_isDirty = true;
            s_ruleVersion++;
        }

        /// <summary> すべてのルール（Enabled がオフのものを含む）の入力。パスの順 </summary>
        internal static IReadOnlyList<RegistrationRuleInput> GetRuleInputs()
        {
            RefreshRules();
            return RULE_INPUTS;
        }

        /// <summary> すべてのルールの SO。GetRuleInputs と同じ並び </summary>
        internal static IReadOnlyList<AddressableAutoRegistrationRuleSO> GetRuleAssets()
        {
            RefreshRules();
            return RULE_ASSETS;
        }

        /// <summary> Addressables の設定を返す。無ければ null </summary>
        internal static AddressableAssetSettings GetSettings()
        {
            return AddressableAssetSettingsDefaultObject.SettingsExists ? AddressableAssetSettingsDefaultObject.Settings : null;
        }

        /// <summary> Addressables 側の状態（グループ・Label・すべてのエントリ）と標準の値から、計画を作るときの環境を作る </summary>
        internal static RegistrationEnvironment CreateEnvironment(AddressableAssetSettings settings)
        {
            var group_names = new List<string>(settings.groups.Count);
            var entries = new List<RegistrationEntryInput>();
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                group_names.Add(group.Name);
                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;
                    entries.Add(new RegistrationEntryInput(entry.AssetPath, entry.address, group.Name));
                }
            }

            var default_group = settings.DefaultGroup;
            return new RegistrationEnvironment(
                group_names,
                default_group != null ? default_group.Name : string.Empty,
                settings.GetLabels(),
                BuildStandardValues(),
                entries);
        }

        /// <summary>
        /// インポート・移動されたアセットから、自動登録の入力を作る。同じパスは1件にまとめる（移動なら元のパスを持つ）。
        /// フォルダ・いつも登録しないもの（ルールの SO・Editor SO・Addressables の設定フォルダ）は入れない。result は最初に Clear する。
        /// AssetDatabase に触る前に、パスの文字列だけで絞り込む。いつも登録しないもの（Resources を除く）と、
        /// 今のパスも移動元も有効なルールの対象フォルダの外にあるものは、自動登録と無関係なので入れない。
        /// </summary>
        internal static void CollectImportedAssets(
            AddressableAssetSettings settings,
            IReadOnlyList<RegistrationRuleInput> rules,
            string[] imported_assets,
            string[] moved_assets,
            string[] moved_from_asset_paths,
            List<RegistrationAssetInput> result)
        {
            result.Clear();
            var index_by_path = new Dictionary<string, int>();

            for (var i = 0; i < moved_assets.Length; i++)
            {
                var moved_from = i < moved_from_asset_paths.Length ? moved_from_asset_paths[i] : string.Empty;
                if (!IsRelatedToRules(rules, moved_assets[i], moved_from)) continue;
                AddAssetInput(settings, moved_assets[i], moved_from, result, index_by_path);
            }
            for (var i = 0; i < imported_assets.Length; i++)
            {
                if (index_by_path.ContainsKey(imported_assets[i])) continue;
                if (!IsRelatedToRules(rules, imported_assets[i], string.Empty)) continue;
                AddAssetInput(settings, imported_assets[i], string.Empty, result, index_by_path);
            }
        }

        /// <summary>
        /// 指定したフォルダの配下のアセットを集める（当て直す・手動一括登録・Inspector のプレビュー用）。
        /// show_progress のときは、キャンセルできる進捗バーを出す（try/finally で必ず消す）。キャンセルしたら Canceled を返し、result は使わない。
        /// </summary>
        internal static RegistrationScanResult TryCollectAssets(
            AddressableAssetSettings settings,
            IReadOnlyList<string> folders,
            bool show_progress,
            List<RegistrationAssetInput> result)
        {
            result.Clear();
            if (settings == null) return RegistrationScanResult.NoSettings;
            if (folders.Count == 0) return RegistrationScanResult.Completed;

            var search_folders = new string[folders.Count];
            for (var i = 0; i < folders.Count; i++) search_folders[i] = folders[i];

            try
            {
                // 入れ子のフォルダを指定しても、同じアセットは1回だけ集める
                var guids = AssetDatabase.FindAssets(string.Empty, search_folders);
                var index_by_path = new Dictionary<string, int>();
                for (var i = 0; i < guids.Length; i++)
                {
                    if (show_progress && i % PROGRESS_INTERVAL == 0
                        && EditorUtility.DisplayCancelableProgressBar(EditorText.Get("registration.progress.collecting"), $"{i} / {guids.Length}", (float)i / guids.Length))
                    {
                        result.Clear();
                        return RegistrationScanResult.Canceled;
                    }
                    AddAssetInput(settings, AssetDatabase.GUIDToAssetPath(guids[i]), string.Empty, result, index_by_path);
                }
                return RegistrationScanResult.Completed;
            }
            finally
            {
                if (show_progress) EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// 指定したフォルダの配下にある拡張子と、その数を返す（拡張子の選択で、実在する拡張子を上に出すため）。
        /// 小文字・ドット付き（例：.png）。数の多い順、同数なら名前順。フォルダ・登録しないもの・拡張子の無いものは数えない。
        /// </summary>
        internal static List<KeyValuePair<string, int>> CollectExtensionCounts(string folder_path)
        {
            var counts = new Dictionary<string, int>();
            if (!string.IsNullOrEmpty(folder_path) && AssetDatabase.IsValidFolder(folder_path))
            {
                foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { folder_path }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (RegistrationRuleMatcher.GetExclusionKind(path) != RegistrationExclusionKind.None) continue;
                    if (AssetDatabase.IsValidFolder(path)) continue;

                    var extension = RegistrationRuleInput.NormalizeExtension(System.IO.Path.GetExtension(path));
                    if (extension.Length == 0) continue;
                    counts[extension] = counts.TryGetValue(extension, out var count) ? count + 1 : 1;
                }
            }

            var result = new List<KeyValuePair<string, int>>();
            foreach (var pair in counts) result.Add(new KeyValuePair<string, int>("." + pair.Key, pair.Value));
            result.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
            return result;
        }

        /// <summary> 標準の値（仕様 6.3）の表を、Unity から読んで作る </summary>
        internal static Dictionary<string, string> BuildStandardValues()
        {
            return new Dictionary<string, string>
            {
                { "UnityEngine.Application.version", Application.version },
                { "UnityEngine.Application.productName", Application.productName },
                { "UnityEngine.Application.companyName", Application.companyName },
                { "UnityEngine.Application.identifier", Application.identifier },
                { "UnityEngine.Application.unityVersion", Application.unityVersion },
                { "UnityEngine.Application.platform", Application.platform.ToString() },
                { "UnityEditor.PlayerSettings.bundleVersion", PlayerSettings.bundleVersion },
                { "UnityEditor.EditorUserBuildSettings.activeBuildTarget", EditorUserBuildSettings.activeBuildTarget.ToString() },
            };
        }

        // キャッシュが古いときだけ、ルールの SO を探し直す（パスの順に並べ、結果が毎回同じになるようにする）
        private static void RefreshRules()
        {
            if (!s_isDirty) return;

            RULE_ASSETS.Clear();
            RULE_INPUTS.Clear();

            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets(RULE_FILTER)) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(string.CompareOrdinal);

            foreach (var path in paths)
            {
                var rule = AssetDatabase.LoadAssetAtPath<AddressableAutoRegistrationRuleSO>(path);
                if (rule == null) continue;
                RULE_ASSETS.Add(rule);
                RULE_INPUTS.Add(rule.ToInput());
            }
            s_isDirty = false;
        }

        // 自動登録に関係しうるパスか。いつも登録しないもの（Resources を除く）を捨て、
        // 今のパスか移動元が、有効なルールの対象フォルダの配下にあるものだけ残す（文字列の比較だけで、AssetDatabase には触れない）
        private static bool IsRelatedToRules(IReadOnlyList<RegistrationRuleInput> rules, string path, string moved_from)
        {
            if (RegistrationRuleMatcher.GetExclusionKind(path) == RegistrationExclusionKind.Ignored) return false;
            return RegistrationRuleMatcher.IsUnderAnyEnabledRuleFolder(rules, path)
                   || RegistrationRuleMatcher.IsUnderAnyEnabledRuleFolder(rules, moved_from);
        }

        // アセット1つを入力にして result に足す。登録の対象にならないもの（フォルダ・ルールの SO など）は足さない
        private static void AddAssetInput(
            AddressableAssetSettings settings,
            string path,
            string moved_from,
            List<RegistrationAssetInput> result,
            Dictionary<string, int> index_by_path)
        {
            if (string.IsNullOrEmpty(path) || index_by_path.ContainsKey(path)) return;
            if (!TryCreateAssetInput(settings, path, moved_from, out var input)) return;

            index_by_path[path] = result.Count;
            result.Add(input);
        }

        internal static bool TryCreateAssetInput(AddressableAssetSettings settings, string path, string moved_from, out RegistrationAssetInput input)
        {
            input = null;
            if (AssetDatabase.IsValidFolder(path)) return false;
            // Addressables 自身の設定フォルダ（グループ・設定の .asset）は、登録しない
            if (path.StartsWith(settings.ConfigFolder + "/", StringComparison.OrdinalIgnoreCase)) return false;

            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null) return false;
            // ルールの SO・Editor SO が自分自身を登録しないよう、型で除く（仕様 7.2）
            if (type == typeof(AddressableAutoRegistrationRuleSO) || type == typeof(AddressableEditorSettingsSO)) return false;

            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return false;

            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
            {
                input = new RegistrationAssetInput(path, guid, type.Name, false, string.Empty, string.Empty, null, moved_from);
                return true;
            }

            // Label は並びを固定する（同じ入力から同じ計画を作るため）
            var labels = new List<string>(entry.labels);
            labels.Sort(string.CompareOrdinal);
            input = new RegistrationAssetInput(path, guid, type.Name, true, entry.parentGroup.Name, entry.address, labels, moved_from);
            return true;
        }
    }
}
