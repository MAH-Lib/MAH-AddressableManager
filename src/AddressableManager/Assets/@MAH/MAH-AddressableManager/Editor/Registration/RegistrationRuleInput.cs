using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// ルール1つ分の入力。Unity の型に依存せず、文字列と列挙だけで持つ（Matcher・PlanBuilder を EditMode でテストするため）。
    /// 自動登録ではルールの SO から、手動一括登録では画面の指定から作る。
    /// </summary>
    internal sealed class RegistrationRuleInput
    {
        private static readonly string[] EMPTY = new string[0];

        private readonly string _name;
        private readonly string _guid;
        private readonly string _folderPath;
        private readonly bool _enabled;
        private readonly RegistrationTargetMode _targetMode;
        private readonly string[] _extensions;
        private readonly string[] _excludedExtensions;
        private readonly string[] _excludePatterns;
        private readonly string _addressTemplate;
        private readonly string _targetGroup;
        private readonly string _groupTemplateAssetGuid;
        private readonly string[] _labels;
        private readonly RegistrationAddressMode _addressMode;

        /// <summary> ルールの SO の名前（手動一括登録では空） </summary>
        internal string AccName => _name;
        /// <summary> ルールの SO の GUID（手動一括登録では空） </summary>
        internal string AccGuid => _guid;
        /// <summary> 対象フォルダのパス（末尾の / なし。例：Assets/App） </summary>
        internal string AccFolderPath => _folderPath;
        /// <summary> 有効か。オフのルールは無いものとして扱う </summary>
        internal bool AccEnabled => _enabled;
        /// <summary> 対象の指定方法 </summary>
        internal RegistrationTargetMode AccTargetMode => _targetMode;
        /// <summary> 対象の拡張子（小文字・ドットなしに正規化済み。Extensions のとき使う） </summary>
        internal IReadOnlyList<string> AccExtensions => _extensions;
        /// <summary> 除外する拡張子（小文字・ドットなしに正規化済み。All のとき使う） </summary>
        internal IReadOnlyList<string> AccExcludedExtensions => _excludedExtensions;
        /// <summary> 除外パターン（glob。アセットのパス全体に対して判定する） </summary>
        internal IReadOnlyList<string> AccExcludePatterns => _excludePatterns;
        /// <summary> アドレスのテンプレート（空ならアセットパス） </summary>
        internal string AccAddressTemplate => _addressTemplate;
        /// <summary> 追加先のグループ名（テンプレート。マクロを使える。空なら DefaultGroup） </summary>
        internal string AccTargetGroup => _targetGroup;
        /// <summary> グループを作るときの Group Template の GUID（空なら Editor SO の既定） </summary>
        internal string AccGroupTemplateAssetGuid => _groupTemplateAssetGuid;
        /// <summary> Label のテンプレート（空なら Label を付けない） </summary>
        internal IReadOnlyList<string> AccLabels => _labels;
        /// <summary> アドレスの追従／固定 </summary>
        internal RegistrationAddressMode AccAddressMode => _addressMode;

        internal RegistrationRuleInput(
            string name,
            string guid,
            string folder_path,
            bool enabled,
            RegistrationTargetMode target_mode,
            IReadOnlyList<string> extensions,
            IReadOnlyList<string> excluded_extensions,
            IReadOnlyList<string> exclude_patterns,
            string address_template,
            string target_group,
            string group_template_asset_guid,
            IReadOnlyList<string> labels,
            RegistrationAddressMode address_mode)
        {
            _name = name ?? string.Empty;
            _guid = guid ?? string.Empty;
            _folderPath = NormalizeFolderPath(folder_path);
            _enabled = enabled;
            _targetMode = target_mode;
            _extensions = NormalizeExtensions(extensions);
            _excludedExtensions = NormalizeExtensions(excluded_extensions);
            _excludePatterns = ToArray(exclude_patterns);
            _addressTemplate = address_template ?? string.Empty;
            _targetGroup = target_group ?? string.Empty;
            _groupTemplateAssetGuid = group_template_asset_guid ?? string.Empty;
            _labels = ToArray(labels);
            _addressMode = address_mode;
        }

        /// <summary> 拡張子を小文字・ドットなしにそろえる（".PNG" と "png" を同じ扱いにする）。空白だけなら空文字 </summary>
        internal static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return string.Empty;
            return extension.Trim().TrimStart('.').ToLowerInvariant();
        }

        // 区切りを / にそろえ、前後の空白と末尾の / を取り除く
        private static string NormalizeFolderPath(string folder_path)
        {
            if (string.IsNullOrWhiteSpace(folder_path)) return string.Empty;
            return folder_path.Trim().Replace('\\', '/').TrimEnd('/');
        }

        // 正規化したうえで、空の項目と重複を取り除く
        private static string[] NormalizeExtensions(IReadOnlyList<string> source)
        {
            if (source == null || source.Count == 0) return EMPTY;

            var result = new List<string>(source.Count);
            for (var i = 0; i < source.Count; i++)
            {
                var normalized = NormalizeExtension(source[i]);
                if (normalized.Length == 0 || result.Contains(normalized)) continue;
                result.Add(normalized);
            }
            return result.ToArray();
        }

        private static string[] ToArray(IReadOnlyList<string> source)
        {
            if (source == null || source.Count == 0) return EMPTY;

            var result = new string[source.Count];
            for (var i = 0; i < source.Count; i++) result[i] = source[i] ?? string.Empty;
            return result;
        }
    }
}
