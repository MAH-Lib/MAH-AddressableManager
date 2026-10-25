using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// 計画を作るときの、Addressables 側の状態（既存のグループ・Label・すべてのエントリ）と標準の値の表。
    /// Scanner が Unity から読んで作る。Unity の型に依存しない。
    /// </summary>
    internal sealed class RegistrationEnvironment
    {
        private static readonly RegistrationEntryInput[] NO_ENTRIES = new RegistrationEntryInput[0];

        private readonly HashSet<string> _existingGroupNames;
        private readonly string _defaultGroupName;
        private readonly HashSet<string> _existingLabels;
        private readonly IReadOnlyDictionary<string, string> _standardValues;
        private readonly IReadOnlyList<RegistrationEntryInput> _allEntries;

        /// <summary> 今あるグループ名（無いグループは、登録のときに新しく作る） </summary>
        internal IReadOnlyCollection<string> AccExistingGroupNames => _existingGroupNames;
        /// <summary> Addressables の DefaultGroup の名前（グループ名のテンプレートが空のときに使う） </summary>
        internal string AccDefaultGroupName => _defaultGroupName;
        /// <summary> 今ある Label </summary>
        internal IReadOnlyCollection<string> AccExistingLabels => _existingLabels;
        /// <summary> 標準の値の表（マクロ名 → 値） </summary>
        internal IReadOnlyDictionary<string, string> AccStandardValues => _standardValues;
        /// <summary> すべてのエントリ（アドレスの重複を調べる範囲。本機能で登録したものに限らない） </summary>
        internal IReadOnlyList<RegistrationEntryInput> AccAllEntries => _allEntries;

        /// <summary> 同じ名前のグループが今あるか </summary>
        internal bool HasGroup(string group_name) => _existingGroupNames.Contains(group_name);

        /// <summary> 同じ名前の Label が今あるか </summary>
        internal bool HasLabel(string label) => _existingLabels.Contains(label);

        internal RegistrationEnvironment(
            IEnumerable<string> existing_group_names,
            string default_group_name,
            IEnumerable<string> existing_labels,
            IReadOnlyDictionary<string, string> standard_values,
            IReadOnlyList<RegistrationEntryInput> all_entries)
        {
            _existingGroupNames = new HashSet<string>(existing_group_names ?? new string[0]);
            _defaultGroupName = default_group_name ?? string.Empty;
            _existingLabels = new HashSet<string>(existing_labels ?? new string[0]);
            _standardValues = standard_values;
            _allEntries = all_entries ?? NO_ENTRIES;
        }
    }
}
