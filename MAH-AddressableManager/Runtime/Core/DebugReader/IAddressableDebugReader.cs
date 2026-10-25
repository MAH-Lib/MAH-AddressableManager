#if UNITY_EDITOR
using System.Collections.Generic;
using MAH.AddressableManager.Core.Loading;

namespace MAH.AddressableManager.Core
{
    /// <summary>
    /// Editor のデバッグウィンドウが、実行中の状態を読み取り専用で見るための窓口。
    /// Model を書き換えるメソッドは、ここに足さない（手動 Release・強制回収のボタンを作らないため）。
    /// 機能ごとに Copy〜To を足す。渡された List は Clear してから書き出す（Window が List を使い回す）。
    /// </summary>
    internal interface IAddressableDebugReader
    {
        /// <summary>
        /// Registry の全 Entry を entries へ、寿命スコープごとの確保内訳を scope_counts へ書き出す。
        /// 各 Entry の内訳は、scope_counts の中の AccScopeCountStart から AccScopeCountLength 件。
        /// </summary>
        void CopyAssetEntriesTo(List<AssetEntrySnapshot> entries, List<ScopeCountSnapshot> scope_counts);

        /// <summary> 発行済みで、まだ解放されていないチケットを tickets へ書き出す </summary>
        void CopyIssuedTicketsTo(List<IssuedTicketSnapshot> tickets);

        /// <summary> 明示 Release されずに寿命回収されたチケット（リーク）を、確保元ごとに集約した行として leaks へ書き出す </summary>
        void CopyLeaksTo(List<LeakSnapshot> leaks);

        /// <summary> Owner 寿命で監視中の GameObject を、生存・Watcher の貼り付け状態とともに owners へ書き出す </summary>
        void CopyWatchedOwnersTo(List<OwnerWatchSnapshot> owners);

        /// <summary> Owner 寿命の回収を、回収経路（OnDestroy / ポーリング）と Owner 名ごとに集約した行として reclaims へ書き出す </summary>
        void CopyOwnerReclaimsTo(List<OwnerReclaimSnapshot> reclaims);
    }
}
#endif
