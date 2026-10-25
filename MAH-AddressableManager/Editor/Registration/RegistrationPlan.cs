using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary> 新しく作るグループ（名前と、作るのに使う Group Template の GUID。空なら Editor SO の既定） </summary>
    internal readonly struct RegistrationGroupRequest
    {
        private readonly string _name;
        private readonly string _templateAssetGuid;

        /// <summary> 新しく作るグループの名前 </summary>
        internal string AccName => _name;
        /// <summary> 作るのに使う Group Template の GUID（空なら Editor SO の既定） </summary>
        internal string AccTemplateAssetGuid => _templateAssetGuid;

        internal RegistrationGroupRequest(string name, string template_asset_guid)
        {
            _name = name;
            _templateAssetGuid = template_asset_guid ?? string.Empty;
        }
    }

    /// <summary>
    /// PlanBuilder が作る計画。差分（変更の一覧）・新しく作るグループと Label・競合・アドレスの重複・警告とエラーを持つ。
    /// 差分がなければ AccChanges は空で、Writer は何も書き込まない。Unity の型に依存しない。
    /// </summary>
    internal sealed class RegistrationPlan
    {
        private readonly List<RegistrationChange> _changes = new();
        private readonly List<RegistrationGroupRequest> _newGroups = new();
        private readonly List<string> _newLabels = new();
        private readonly List<RegistrationConflict> _conflicts = new();
        private readonly List<RegistrationDuplicate> _duplicates = new();
        private readonly List<string> _warnings = new();
        private readonly List<string> _errors = new();

        /// <summary> アセットごとの変更（アセットパス順） </summary>
        internal List<RegistrationChange> AccChanges => _changes;
        /// <summary> 新しく作るグループ </summary>
        internal List<RegistrationGroupRequest> AccNewGroups => _newGroups;
        /// <summary> 新しく作る Label </summary>
        internal List<string> AccNewLabels => _newLabels;
        /// <summary> ルールの競合（登録も変更もされないアセット） </summary>
        internal List<RegistrationConflict> AccConflicts => _conflicts;
        /// <summary> アドレスの重複（登録はするが、エラーとして知らせる） </summary>
        internal List<RegistrationDuplicate> AccDuplicates => _duplicates;
        /// <summary> 警告 </summary>
        internal List<string> AccWarnings => _warnings;
        /// <summary> エラー（そのアセットは登録も変更もされない） </summary>
        internal List<string> AccErrors => _errors;

        /// <summary> 書き込む差分があるか </summary>
        internal bool AccHasChanges => _changes.Count > 0;
        /// <summary> 新しく登録する件数を数える </summary>
        internal int CountAdded() => Count(change => change.AccIsNewEntry);
        /// <summary> 既存のエントリのグループを移す件数を数える </summary>
        internal int CountMoved() => Count(change => !change.AccIsNewEntry && change.AccGroupChanged);
        /// <summary> 既存のエントリのアドレスを変える件数を数える </summary>
        internal int CountReaddressed() => Count(change => !change.AccIsNewEntry && change.AccAddressChanged);
        /// <summary> 既存のエントリの Label を増減する件数を数える </summary>
        internal int CountLabelChanged() => Count(change => !change.AccIsNewEntry && change.AccLabelsChanged);

        // 件数を数える（集計は画面・ログの表示時だけなので、ラムダの確保は許容する）
        private int Count(System.Func<RegistrationChange, bool> predicate)
        {
            var count = 0;
            for (var i = 0; i < _changes.Count; i++)
            {
                if (predicate(_changes[i])) count++;
            }
            return count;
        }
    }
}
