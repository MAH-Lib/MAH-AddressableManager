using System;
using UnityEngine;

namespace MAH.AddressableManager.Editor.SettingsSync
{
    /// <summary>
    /// Dependency Check のテキスト出力の設定。出力先のフォルダを持つ。
    /// SO ではなく、AddressableEditorSettingsSO が保持する [Serializable] クラス（AddressableLeakLogSettings と同じ形）。
    /// </summary>
    [Serializable]
    internal sealed class AddressableDependencyCheckSettings
    {
        /// <summary> 既定の出力先。リークのログと同じ場所（ファイル名の接頭辞で区別される） </summary>
        internal const string DEFAULT_OUTPUT_FOLDER = "Logs/AddressableManager";

        [SerializeField] private string _outputFolder = DEFAULT_OUTPUT_FOLDER;

        /// <summary> テキスト出力の出力先フォルダ（相対パスはプロジェクトのルートから） </summary>
        internal string AccOutputFolder => _outputFolder;

        /// <summary> Unity のシリアライズ用。既定値で生成する </summary>
        public AddressableDependencyCheckSettings()
        {
        }

        /// <summary> テスト等でコードから値を指定して生成する </summary>
        internal AddressableDependencyCheckSettings(string output_folder)
        {
            _outputFolder = output_folder;
            ClampValues();
        }

        /// <summary> 設定値を有効な範囲に補正する（SO の OnValidate から呼ばれる）。出力先が空のときは既定値に戻す </summary>
        internal void ClampValues()
        {
            _outputFolder = string.IsNullOrWhiteSpace(_outputFolder) ? DEFAULT_OUTPUT_FOLDER : _outputFolder.Trim();
        }
    }
}
