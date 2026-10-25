using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// アセット1つ分の入力。今のエントリの状態（登録済みか・グループ・アドレス・Label）を、文字列だけで持つ。
    /// Unity の型に依存しない（PlanBuilder を EditMode でテストするため）。
    /// </summary>
    internal sealed class RegistrationAssetInput
    {
        private static readonly string[] EMPTY = new string[0];

        private readonly string _path;
        private readonly string _guid;
        private readonly string _typeName;
        private readonly bool _isRegistered;
        private readonly string _currentGroup;
        private readonly string _currentAddress;
        private readonly string[] _currentLabels;
        private readonly string _movedFromPath;

        /// <summary> アセットパス（拡張子付き） </summary>
        internal string AccPath => _path;
        /// <summary> アセットの GUID </summary>
        internal string AccGuid => _guid;
        /// <summary> メインアセットの型名（{Asset.Type} 用） </summary>
        internal string AccTypeName => _typeName;
        /// <summary> すでに Addressables のエントリか </summary>
        internal bool AccIsRegistered => _isRegistered;
        /// <summary> 今のグループ名（未登録なら空） </summary>
        internal string AccCurrentGroup => _currentGroup;
        /// <summary> 今のアドレス（未登録なら空） </summary>
        internal string AccCurrentAddress => _currentAddress;
        /// <summary> 今の Label（未登録なら空） </summary>
        internal IReadOnlyList<string> AccCurrentLabels => _currentLabels;
        /// <summary> 移動・名前の変更で来たときの、元のパス（そうでなければ空） </summary>
        internal string AccMovedFromPath => _movedFromPath;

        internal RegistrationAssetInput(
            string path,
            string guid,
            string type_name,
            bool is_registered,
            string current_group,
            string current_address,
            IReadOnlyList<string> current_labels,
            string moved_from_path)
        {
            _path = (path ?? string.Empty).Replace('\\', '/');
            _guid = guid ?? string.Empty;
            _typeName = type_name ?? string.Empty;
            _isRegistered = is_registered;
            _currentGroup = current_group ?? string.Empty;
            _currentAddress = current_address ?? string.Empty;
            _movedFromPath = (moved_from_path ?? string.Empty).Replace('\\', '/');

            if (current_labels == null || current_labels.Count == 0)
            {
                _currentLabels = EMPTY;
                return;
            }
            _currentLabels = new string[current_labels.Count];
            for (var i = 0; i < current_labels.Count; i++) _currentLabels[i] = current_labels[i] ?? string.Empty;
        }
    }
}
