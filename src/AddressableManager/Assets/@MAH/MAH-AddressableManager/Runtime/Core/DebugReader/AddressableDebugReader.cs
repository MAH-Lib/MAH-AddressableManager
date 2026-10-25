#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using MAH.AddressableManager.Core.Loading;

namespace MAH.AddressableManager.Core
{
    /// <summary>
    /// IAddressableDebugReader の実装。Manager が Model を注入して生成し、Model のコピー用メソッドへ委譲するだけで、ロジックは持たない。
    /// 複数機能の Model を知るため、機能フォルダの外（Core/DebugReader/）に置く（AddressableHandler と同じ扱い）。
    /// </summary>
    internal sealed class AddressableDebugReader : IAddressableDebugReader
    {
        private readonly AssetRegistryModel _assetRegistry;
        private readonly IssuedTicketRegistryModel _issuedTickets;
        private readonly LeakRegistryModel _leaks;
        private readonly OwnerWatchRegistryModel _ownerWatch;
        private readonly OwnerReclaimRegistryModel _ownerReclaims;

        internal AddressableDebugReader(
            AssetRegistryModel asset_registry,
            IssuedTicketRegistryModel issued_tickets,
            LeakRegistryModel leaks,
            OwnerWatchRegistryModel owner_watch,
            OwnerReclaimRegistryModel owner_reclaims)
        {
            _assetRegistry = asset_registry ?? throw new ArgumentNullException(nameof(asset_registry));
            _issuedTickets = issued_tickets ?? throw new ArgumentNullException(nameof(issued_tickets));
            _leaks = leaks ?? throw new ArgumentNullException(nameof(leaks));
            _ownerWatch = owner_watch ?? throw new ArgumentNullException(nameof(owner_watch));
            _ownerReclaims = owner_reclaims ?? throw new ArgumentNullException(nameof(owner_reclaims));
        }

        public void CopyAssetEntriesTo(List<AssetEntrySnapshot> entries, List<ScopeCountSnapshot> scope_counts)
            => _assetRegistry.CopyEntriesTo(entries, scope_counts);

        public void CopyIssuedTicketsTo(List<IssuedTicketSnapshot> tickets)
            => _issuedTickets.CopyTicketsTo(tickets);

        public void CopyLeaksTo(List<LeakSnapshot> leaks)
            => _leaks.CopyLeaksTo(leaks);

        public void CopyWatchedOwnersTo(List<OwnerWatchSnapshot> owners)
            => _ownerWatch.CopyOwnersTo(owners);

        public void CopyOwnerReclaimsTo(List<OwnerReclaimSnapshot> reclaims)
            => _ownerReclaims.CopyReclaimsTo(reclaims);
    }
}
#endif
