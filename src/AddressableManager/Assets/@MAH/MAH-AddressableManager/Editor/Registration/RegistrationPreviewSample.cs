using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary> Inspector のプレビューに出す、アセット1件分の展開結果（アドレス・グループ・Label）。Unity の型に依存しない </summary>
    internal sealed class RegistrationPreviewSample
    {
        private readonly string _assetPath;
        private readonly string _address;
        private readonly string _group;
        private readonly List<string> _labels;

        /// <summary> アセットパス </summary>
        internal string AccAssetPath => _assetPath;
        /// <summary> 展開したアドレス（テンプレートが空、または展開の結果が空ならアセットパス） </summary>
        internal string AccAddress => _address;
        /// <summary> 展開したグループ名（テンプレートが空、または展開の結果が空なら DefaultGroup の名前） </summary>
        internal string AccGroup => _group;
        /// <summary> 展開した Label（空の結果は除く） </summary>
        internal IReadOnlyList<string> AccLabels => _labels;

        internal RegistrationPreviewSample(string asset_path, string address, string group, List<string> labels)
        {
            _assetPath = asset_path;
            _address = address;
            _group = group;
            _labels = labels;
        }
    }
}
