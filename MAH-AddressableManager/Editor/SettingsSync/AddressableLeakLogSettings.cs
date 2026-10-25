using System;
using UnityEngine;

namespace MAH.AddressableManager.Editor.SettingsSync
{
    /// <summary>
    /// 「リーク」タブのログ出力の設定。出力先のフォルダと、Play 終了時の自動出力の ON / OFF を持つ。
    /// SO ではなく、AddressableEditorSettingsSO が保持する [Serializable] クラス（AddressableDebugWindowSettings と同じ形）。
    /// </summary>
    [Serializable]
    internal sealed class AddressableLeakLogSettings
    {
        /// <summary>
        /// 既定の出力先。プロジェクト直下の Logs/ は Unity がインポートせず、git の管理外。
        /// Unity 自身のログ（Editor.log など）と混ざらないよう、1階層かませている
        /// </summary>
        internal const string DEFAULT_OUTPUT_FOLDER = "Logs/AddressableManager";

        [SerializeField] private string _outputFolder = DEFAULT_OUTPUT_FOLDER;

        [SerializeField] private bool _exportOnExitPlayMode;

        /// <summary> ログの出力先フォルダ（相対パスはプロジェクトのルートから） </summary>
        internal string AccOutputFolder => _outputFolder;
        /// <summary> Play の終了時に、リークを自動で出力するか </summary>
        internal bool AccExportOnExitPlayMode => _exportOnExitPlayMode;

        /// <summary> Unity のシリアライズ用。既定値で生成する </summary>
        public AddressableLeakLogSettings()
        {
        }

        /// <summary> テスト等でコードから値を指定して生成する </summary>
        internal AddressableLeakLogSettings(string output_folder, bool export_on_exit_play_mode)
        {
            _outputFolder = output_folder;
            _exportOnExitPlayMode = export_on_exit_play_mode;
            ClampValues();
        }

        /// <summary> 設定値を有効な範囲に補正する（SO の OnValidate から呼ばれる）。出力先が空のときは既定値に戻す </summary>
        internal void ClampValues()
        {
            _outputFolder = string.IsNullOrWhiteSpace(_outputFolder) ? DEFAULT_OUTPUT_FOLDER : _outputFolder.Trim();
        }
    }
}
