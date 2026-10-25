using System.IO;
using MAH.AddressableManager.Editor.Localization;
using UnityEditor;
using UnityEngine;

namespace MAH.AddressableManager.Editor.SettingsSync
{
    /// <summary>
    /// AddressableEditorSettingsSO の探索と、無いときの自動作成を行う。
    /// 配置場所はプロジェクトごとに変わり得るため、パスではなく型で探す（作成後は任意の場所へ移動してよい）。
    /// 将来の計測シンボルの整合チェックも、この [InitializeOnLoad] の入口に揃える。
    /// </summary>
    [InitializeOnLoad]
    internal static class AddressableEditorSettingsLocator
    {
        private const string LOG_PREFIX = "[AddressableManager] ";
        private const string SETTINGS_FILTER = "t:" + nameof(AddressableEditorSettingsSO);
        // MAH の他モジュールに合わせ、データの置き場所は Assets/MAH.Data/<モジュール名>/ にする
        private const string DEFAULT_FOLDER = "Assets/MAH.Data/AddressableManager/Editor";
        private const string DEFAULT_ASSET_PATH = DEFAULT_FOLDER + "/AddressableEditorSettings.asset";

        static AddressableEditorSettingsLocator()
        {
            // 起動直後はインポート中で AssetDatabase が使えないことがあるため、遅らせる
            EditorApplication.delayCall += EnsureSettingsAsset;
        }

        /// <summary>
        /// プロジェクト内の AddressableEditorSettingsSO を探して返す。無ければ null。
        /// 複数あるときは、警告を出して1件目（パスの順）を使う。
        /// </summary>
        internal static AddressableEditorSettingsSO FindSettings()
        {
            var guids = AssetDatabase.FindAssets(SETTINGS_FILTER);
            if (guids.Length == 0) return null;

            var first_path = AssetDatabase.GUIDToAssetPath(guids[0]);
            if (guids.Length > 1)
                Debug.LogWarning(LOG_PREFIX + EditorText.Format("settings.multiple", nameof(AddressableEditorSettingsSO), guids.Length, first_path));

            return AssetDatabase.LoadAssetAtPath<AddressableEditorSettingsSO>(first_path);
        }

        /// <summary>
        /// 「リーク」のログ出力の設定を返す。SO が無いときは、既定値の設定（自動出力 OFF・出力先 Logs/AddressableManager）。
        /// 押したとき・Play の終了時に読むため、SO が編集されても追従する
        /// </summary>
        internal static AddressableLeakLogSettings FindLeakLogSettings()
        {
            var settings = FindSettings();
            return settings != null ? settings.AccLeakLog : new AddressableLeakLogSettings();
        }

        /// <summary>
        /// Dependency Check のテキスト出力の設定を返す。SO が無いときは、既定値の設定（出力先 Logs/AddressableManager）。
        /// 押したときに読むため、SO が編集されても追従する
        /// </summary>
        internal static AddressableDependencyCheckSettings FindDependencyCheckSettings()
        {
            var settings = FindSettings();
            return settings != null ? settings.AccDependencyCheck : new AddressableDependencyCheckSettings();
        }

        /// <summary>
        /// アセットの登録（自動登録・手動一括登録）の設定を返す。SO が無いときは、既定値の設定（自動登録オン）。
        /// インポートのたびに読むため、SO が編集されても追従する
        /// </summary>
        internal static AddressableRegistrationSettings FindRegistrationSettings()
        {
            var settings = FindSettings();
            return settings != null ? settings.AccRegistration : new AddressableRegistrationSettings();
        }

        // 0件のときだけ、既定のパスに作成する。作成したことはログで知らせる
        private static void EnsureSettingsAsset()
        {
            // 再生の開始に合わせたドメインリロード中は、アセットを作らない
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            // インポート・コンパイル中は、終わってからもう一度試す
            if (EditorApplication.isUpdating || EditorApplication.isCompiling)
            {
                EditorApplication.delayCall += EnsureSettingsAsset;
                return;
            }

            if (AssetDatabase.FindAssets(SETTINGS_FILTER).Length > 0) return;

            EnsureFolder(DEFAULT_FOLDER);
            var settings = ScriptableObject.CreateInstance<AddressableEditorSettingsSO>();
            AssetDatabase.CreateAsset(settings, DEFAULT_ASSET_PATH);
            AssetDatabase.SaveAssets();
            Debug.Log(LOG_PREFIX + EditorText.Format("settings.created", nameof(AddressableEditorSettingsSO), DEFAULT_ASSET_PATH), settings);
        }

        // "Assets/A/B/C" の各階層を、無ければ作る（ルールの SO の置き場所の作成でも使う）
        internal static void EnsureFolder(string folder_path)
        {
            if (AssetDatabase.IsValidFolder(folder_path)) return;

            var parent = Path.GetDirectoryName(folder_path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder_path));
        }
    }
}
