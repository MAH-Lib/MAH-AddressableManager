using System;
using MAH.AddressableManager;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// IAssetLoaderWorker の生成を担うファクトリ。LoadingController はこれ経由でのみ Worker を作る。
    /// EditMode テストでは偽の実装に差し替え、実際の Addressables を呼ばずに Controller を検証できるようにする。
    /// </summary>
    internal interface IAssetLoaderServiceFactory
    {
        IAssetLoaderWorker Create<T>(AssetKey key) where T : UnityEngine.Object;

        /// <summary> 実行時にしかわからない型から生成する（Label 一括ロード用）。型は UnityEngine.Object の派生型であること </summary>
        IAssetLoaderWorker Create(AssetKey key, Type type);
    }
}
