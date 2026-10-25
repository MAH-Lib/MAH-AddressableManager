using System.Collections.Generic;
using System.Threading;
#if ADDRESSABLE_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#else
using System.Threading.Tasks;
#endif

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// Label のロケーションを、型を絞らずに取得する Service。LoadingController はこれ経由でのみロケーションを解決する。
    /// EditMode テストでは偽の実装に差し替え、実際の Addressables を呼ばずに Controller を検証できるようにする。
    /// </summary>
    internal interface ILabelLocationService
    {
        /// <summary>
        /// Label のロケーションを解決し、(PrimaryKey, ResourceType) の組を、重複を除いて result の末尾に詰める。
        /// result は呼び出しごとに確保したものを渡す（await をまたぐため、使い回すと並行する呼び出しの間で中身が混ざる）。
        /// 結果の順序は保証しない。該当が0件でも例外にはならない。解決に失敗したら AssetLoadException を投げる。
        /// </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        UniTask ResolveAsync(string label, List<LabelLocation> result, CancellationToken ct);
#else
        Task ResolveAsync(string label, List<LabelLocation> result, CancellationToken ct);
#endif
    }
}
