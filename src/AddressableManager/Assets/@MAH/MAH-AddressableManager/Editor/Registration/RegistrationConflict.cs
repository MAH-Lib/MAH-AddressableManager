using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// ルールの競合。同じ組のルール（2つ以上）に当たってしまったアセットを、ルールの組ごとにまとめたもの。
    /// 競合したアセットは、どのルールでも登録・変更しない。Unity の型に依存しない。
    /// </summary>
    internal sealed class RegistrationConflict
    {
        private readonly List<string> _ruleNames;
        private readonly List<string> _ruleGuids;
        private readonly List<string> _assetPaths = new();

        /// <summary> 競合しているルールの SO の名前（名前順） </summary>
        internal IReadOnlyList<string> AccRuleNames => _ruleNames;
        /// <summary> 競合しているルールの SO の GUID（AccRuleNames と同じ並び。GUID が分からないものは空）。同じ名前の SO を区別するために使う </summary>
        internal IReadOnlyList<string> AccRuleGuids => _ruleGuids;
        /// <summary> 競合しているアセットのパス（アセットパス順） </summary>
        internal List<string> AccAssetPaths => _assetPaths;

        /// <summary> rule_guids は rule_names と同じ並び・同じ数で渡す。省略したときは、GUID をすべて空として扱う </summary>
        internal RegistrationConflict(List<string> rule_names, List<string> rule_guids = null)
        {
            _ruleNames = rule_names;
            _ruleGuids = rule_guids ?? EmptyGuids(rule_names.Count);
        }

        private static List<string> EmptyGuids(int count)
        {
            var guids = new List<string>(count);
            for (var i = 0; i < count; i++) guids.Add(string.Empty);
            return guids;
        }
    }
}
