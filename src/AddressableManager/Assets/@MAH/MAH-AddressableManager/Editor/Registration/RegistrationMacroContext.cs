using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// マクロの展開に必要な値をまとめた入力。Unity の型に依存せず、文字列だけで持つ（Expander を EditMode でテストするため）。
    /// 標準の値（UnityEngine.Application.version など）は、Scanner が Unity から読んで表にして渡す。
    /// </summary>
    internal sealed class RegistrationMacroContext
    {
        private static readonly Dictionary<string, string> EMPTY_VALUES = new();

        private readonly string _assetPath;
        private readonly string _assetGuid;
        private readonly string _assetTypeName;
        private readonly string _root;
        private readonly string _ruleName;
        private readonly IReadOnlyDictionary<string, string> _standardValues;

        /// <summary> アセットパス（拡張子付き。例：Assets/App/Audio/BGM/Title.wav） </summary>
        internal string AccAssetPath => _assetPath;
        /// <summary> アセットの GUID </summary>
        internal string AccAssetGuid => _assetGuid;
        /// <summary> メインアセットの型名（例：AudioClip） </summary>
        internal string AccAssetTypeName => _assetTypeName;
        /// <summary> ルールの対象フォルダ（手動一括登録では、選んだフォルダ。末尾の / なし） </summary>
        internal string AccRoot => _root;
        /// <summary> ルールの SO の名前（手動一括登録では空） </summary>
        internal string AccRuleName => _ruleName;
        /// <summary> 標準の値の表（マクロ名 → 値。例：UnityEngine.Application.version → 1.2.0） </summary>
        internal IReadOnlyDictionary<string, string> AccStandardValues => _standardValues;

        internal RegistrationMacroContext(
            string asset_path,
            string asset_guid,
            string asset_type_name,
            string root,
            string rule_name,
            IReadOnlyDictionary<string, string> standard_values)
        {
            _assetPath = (asset_path ?? string.Empty).Replace('\\', '/');
            _assetGuid = asset_guid ?? string.Empty;
            _assetTypeName = asset_type_name ?? string.Empty;
            _root = (root ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            _ruleName = rule_name ?? string.Empty;
            _standardValues = standard_values ?? EMPTY_VALUES;
        }
    }
}
