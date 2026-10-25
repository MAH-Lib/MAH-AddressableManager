using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// アセット1つ分の変更（登録・グループの移動・アドレスの変更・Label の追加と削除）。差分がないアセットには作らない。
    /// Unity の型に依存しない。
    /// </summary>
    internal sealed class RegistrationChange
    {
        private readonly string _assetPath;
        private readonly string _assetGuid;
        private readonly string _ruleName;
        private readonly bool _isNewEntry;
        private readonly string _fromGroup;
        private readonly string _toGroup;
        private readonly string _fromAddress;
        private readonly string _toAddress;
        private readonly List<string> _labelsToAdd;
        private readonly List<string> _labelsToRemove;
        private readonly bool _groupChanged;
        private readonly bool _addressChanged;
        private readonly bool _labelsChanged;

        /// <summary> アセットパス </summary>
        internal string AccAssetPath => _assetPath;
        /// <summary> アセットの GUID </summary>
        internal string AccAssetGuid => _assetGuid;
        /// <summary> 当てたルールの SO の名前（手動一括登録では空） </summary>
        internal string AccRuleName => _ruleName;
        /// <summary> 新しくエントリを作るか（false なら既存のエントリの更新） </summary>
        internal bool AccIsNewEntry => _isNewEntry;
        /// <summary> 変更前のグループ名（新規なら空） </summary>
        internal string AccFromGroup => _fromGroup;
        /// <summary> 変更後のグループ名（DefaultGroup のときは、その名前に解決済み） </summary>
        internal string AccToGroup => _toGroup;
        /// <summary> 変更前のアドレス（新規なら空） </summary>
        internal string AccFromAddress => _fromAddress;
        /// <summary> 変更後のアドレス </summary>
        internal string AccToAddress => _toAddress;
        /// <summary> 追加する Label </summary>
        internal IReadOnlyList<string> AccLabelsToAdd => _labelsToAdd;
        /// <summary> 外す Label </summary>
        internal IReadOnlyList<string> AccLabelsToRemove => _labelsToRemove;

        /// <summary> グループが変わるか（新規は、新しく入るので変わるとみなす） </summary>
        internal bool AccGroupChanged => _groupChanged;
        /// <summary> アドレスが変わるか（新規は、新しく付くので変わるとみなす） </summary>
        internal bool AccAddressChanged => _addressChanged;
        /// <summary> Label の追加・削除があるか </summary>
        internal bool AccLabelsChanged => _labelsChanged;

        internal RegistrationChange(
            string asset_path,
            string asset_guid,
            string rule_name,
            bool is_new_entry,
            string from_group,
            string to_group,
            string from_address,
            string to_address,
            List<string> labels_to_add,
            List<string> labels_to_remove)
        {
            _assetPath = asset_path;
            _assetGuid = asset_guid;
            _ruleName = rule_name ?? string.Empty;
            _isNewEntry = is_new_entry;
            _fromGroup = from_group ?? string.Empty;
            _toGroup = to_group;
            _fromAddress = from_address ?? string.Empty;
            _toAddress = to_address;
            _labelsToAdd = labels_to_add ?? new List<string>();
            _labelsToRemove = labels_to_remove ?? new List<string>();

            // アクセサの中で計算しないよう、作るときに求めておく
            _groupChanged = _isNewEntry || _fromGroup != _toGroup;
            _addressChanged = _isNewEntry || _fromAddress != _toAddress;
            _labelsChanged = _labelsToAdd.Count > 0 || _labelsToRemove.Count > 0;
        }
    }
}
