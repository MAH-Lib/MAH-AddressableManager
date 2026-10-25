using System;
using System.Collections.Generic;
using MAH.AddressableManager.Editor.Registration;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace MAH.AddressableManager.Editor.SettingsSync
{
    /// <summary>
    /// アセットの登録（自動登録・手動一括登録）の設定。
    /// SO ではなく、AddressableEditorSettingsSO が保持する [Serializable] クラス（AddressableDependencyCheckSettings と同じ形）。
    /// 「ルールの作成時の初期値」は作るときにだけコピーする値で、評価のときには参照しない（仕様 3.4）。
    /// </summary>
    [Serializable]
    internal sealed class AddressableRegistrationSettings
    {
        /// <summary> アドレスのテンプレートの既定。必ず一意で、Addressables に普通に登録したときと同じアドレスになる </summary>
        internal const string DEFAULT_ADDRESS_TEMPLATE = "{Asset.Path}";
        /// <summary> テスト用アセットと Editor フォルダのものを混ぜないための除外パターンの既定 </summary>
        internal static readonly string[] DEFAULT_EXCLUDE_PATTERNS = { "**/_Test/**", "**/Editor/**" };

        [SerializeField] private bool _autoRegistration = true;

        [SerializeField] private RegistrationLogDetail _logDetail = RegistrationLogDetail.Summary;

        [SerializeField] private string _defaultAddressTemplate = DEFAULT_ADDRESS_TEMPLATE;

        [SerializeField] private List<string> _defaultLabels = new();

        [SerializeField] private List<string> _defaultExcludePatterns = new(DEFAULT_EXCLUDE_PATTERNS);

        [SerializeField] private string _defaultTargetGroup = string.Empty;

        [SerializeField] private AddressableAssetGroupTemplate _defaultGroupTemplate;

        /// <summary> 自動登録が有効か </summary>
        internal bool AccAutoRegistration => _autoRegistration;
        /// <summary> 自動登録のログの細かさ </summary>
        internal RegistrationLogDetail AccLogDetail => _logDetail;
        /// <summary> ルールの作成時の初期値：アドレスのテンプレート </summary>
        internal string AccDefaultAddressTemplate => _defaultAddressTemplate;
        /// <summary> ルールの作成時の初期値：Label </summary>
        internal IReadOnlyList<string> AccDefaultLabels => _defaultLabels;
        /// <summary> ルールの作成時の初期値：除外パターン </summary>
        internal IReadOnlyList<string> AccDefaultExcludePatterns => _defaultExcludePatterns;
        /// <summary> 手動一括登録で、追加先のグループを指定しなかったときの既定のグループ名（空なら Addressables の DefaultGroup） </summary>
        internal string AccDefaultTargetGroup => _defaultTargetGroup;
        /// <summary> 既定の Group Template（空なら Addressables の既定） </summary>
        internal AddressableAssetGroupTemplate AccDefaultGroupTemplate => _defaultGroupTemplate;

        /// <summary> Unity のシリアライズ用。既定値で生成する </summary>
        public AddressableRegistrationSettings()
        {
        }

        /// <summary> テスト等でコードから値を指定して生成する </summary>
        internal AddressableRegistrationSettings(
            bool auto_registration,
            RegistrationLogDetail log_detail,
            string default_address_template,
            List<string> default_labels,
            List<string> default_exclude_patterns,
            string default_target_group = "")
        {
            _defaultTargetGroup = default_target_group;
            _autoRegistration = auto_registration;
            _logDetail = log_detail;
            _defaultAddressTemplate = default_address_template;
            _defaultLabels = default_labels;
            _defaultExcludePatterns = default_exclude_patterns;
            ClampValues();
        }

        /// <summary>
        /// 設定値を有効な範囲に補正する（SO の OnValidate から呼ばれる）。
        /// アドレスのテンプレートが空のときは既定値に戻し、リストの null と空白だけの項目を取り除く。
        /// </summary>
        internal void ClampValues()
        {
            _defaultAddressTemplate = string.IsNullOrWhiteSpace(_defaultAddressTemplate)
                ? DEFAULT_ADDRESS_TEMPLATE
                : _defaultAddressTemplate.Trim();
            _defaultTargetGroup = (_defaultTargetGroup ?? string.Empty).Trim();
            _defaultLabels = TrimList(_defaultLabels);
            _defaultExcludePatterns = TrimList(_defaultExcludePatterns);
        }

        // null のリストは空にし、空白だけの項目は取り除く。前後の空白は削る
        private static List<string> TrimList(List<string> source)
        {
            if (source == null) return new List<string>();

            for (var i = source.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(source[i])) source.RemoveAt(i);
                else source[i] = source[i].Trim();
            }
            return source;
        }
    }
}
