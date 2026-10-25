using System.IO;
using MAH.AddressableManager.Editor.SettingsSync;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// ルールの SO を作る入口（仕様 3.3）。作成時に Editor SO の「ルールの作成時の初期値」をコピーする。
    /// ScriptableObject.Reset が CreateAssetMenu からの作成で呼ばれるかに頼らず、作成のたびに確実に初期化するため、CreateAssetMenu は使わない。
    /// 入口は2つ：Create メニュー（右クリックした場所に作り、対象フォルダの初期値はその場所）と、ルール一覧ウィンドウの「作成」（固定の場所に作る）。
    /// </summary>
    internal static class RegistrationRuleAssetCreator
    {
        /// <summary> ルール一覧ウィンドウの「作成」で作る場所。保存先を選ばせない（MAH.Data の外に置けてしまうため） </summary>
        internal const string FIXED_FOLDER = "Assets/MAH.Data/AddressableManager/Editor/AutoRegistration";

        private const string MENU_PATH = "Assets/Create/MAH/Addressable/AutoRegistrationRule";
        private const string DEFAULT_FILE_NAME = "AutoRegistrationRule";
        private const string ASSETS_ROOT = "Assets";

        /// <summary>
        /// ルール一覧ウィンドウの「作成」。固定の場所に、入力したファイル名で作る（同じ名前があれば番号を付ける）。
        /// 対象フォルダの初期値は空。ファイル名が空・使えない文字だけのときは、既定の名前にする。
        /// </summary>
        internal static AddressableAutoRegistrationRuleSO CreateInFixedFolder(string file_name)
        {
            AddressableEditorSettingsLocator.EnsureFolder(FIXED_FOLDER);

            var path = AssetDatabase.GenerateUniqueAssetPath($"{FIXED_FOLDER}/{SanitizeFileName(file_name)}.asset");
            var rule = ScriptableObject.CreateInstance<AddressableAutoRegistrationRuleSO>();
            rule.InitializeFrom(AddressableEditorSettingsLocator.FindRegistrationSettings(), string.Empty);
            AssetDatabase.CreateAsset(rule, path);
            AssetDatabase.SaveAssets();
            RegistrationRuleScanner.InvalidateRules();
            return rule;
        }

        // Project ウィンドウの Create メニュー。右クリックした場所に作り、名前の入力を始める
        [MenuItem(MENU_PATH)]
        private static void CreateFromMenu()
        {
            var folder = GetSelectedFolderPath();
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{DEFAULT_FILE_NAME}.asset");
            var rule = ScriptableObject.CreateInstance<AddressableAutoRegistrationRuleSO>();
            var end_action = ScriptableObject.CreateInstance<CreateRuleEndNameEditAction>();
            // 作った SO を直接持たせる（ID から引き直す InstanceIDToObject は、非推奨のため使わない）
            end_action.SetRule(rule);
            var icon = EditorGUIUtility.IconContent("ScriptableObject Icon").image as Texture2D;
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(rule.GetInstanceID(), end_action, path, icon, null);
        }

        // 選択中のフォルダ（アセットを選んでいれば、その親のフォルダ）。何も選んでいなければ Assets
        private static string GetSelectedFolderPath()
        {
            var selected = Selection.activeObject;
            if (selected == null) return ASSETS_ROOT;

            var path = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(path)) return ASSETS_ROOT;
            return AssetDatabase.IsValidFolder(path) ? path : Path.GetDirectoryName(path).Replace('\\', '/');
        }

        private static string SanitizeFileName(string file_name)
        {
            var name = Path.GetFileNameWithoutExtension(file_name ?? string.Empty).Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid.ToString(), string.Empty);
            return name.Length == 0 ? DEFAULT_FILE_NAME : name;
        }

        /// <summary> 名前の入力が終わったときに呼ばれ、決まったパスにアセットを作る。作る場所のフォルダを対象フォルダの初期値にする </summary>
        private sealed class CreateRuleEndNameEditAction : EndNameEditAction
        {
            // 名前の入力が終わったら、アセットとして保存する SO
            private AddressableAutoRegistrationRuleSO _rule;

            /// <summary> 保存する SO を持たせる </summary>
            internal void SetRule(AddressableAutoRegistrationRuleSO rule)
            {
                _rule = rule;
            }

            /// <summary> 名前の入力がキャンセルされたら、アセットにならなかった SO を破棄する（残すと、持ち主のない SO がメモリに残る） </summary>
            public override void Cancelled(int instanceId, string pathName, string resourceFile)
            {
                if (_rule != null && !EditorUtility.IsPersistent(_rule)) DestroyImmediate(_rule);
                _rule = null;
            }

            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                var rule = _rule;
                if (rule == null) return;

                var folder = Path.GetDirectoryName(pathName).Replace('\\', '/');
                rule.InitializeFrom(AddressableEditorSettingsLocator.FindRegistrationSettings(), folder);
                AssetDatabase.CreateAsset(rule, pathName);
                RegistrationRuleScanner.InvalidateRules();
                ProjectWindowUtil.ShowCreatedAsset(rule);
            }
        }
    }
}
