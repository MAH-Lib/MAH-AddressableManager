using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.ResourceManagement.AsyncOperations;
#if ADDRESSABLE_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#endif

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// 実ロードを担当する単位。handle を内部に隠蔽し、dedup や RefCount には関知しない。
    /// LoadingController から呼べるよう、非ジェネリックの共通インターフェースにしている。
    /// </summary>
    internal interface IAssetLoaderWorker : IDisposable
    {
#if ADDRESSABLE_UNITASK_SUPPORT
        UniTask LoadAsync(CancellationToken token);
#else
        Task LoadAsync(CancellationToken token);
#endif
        /// <summary> 読み込んだアセット本体 </summary>
        UnityEngine.Object AccAsset { get; }
        /// <summary> ロードした型 </summary>
        Type AccAssetType { get; }
        /// <summary> ロードの進行状態 </summary>
        AsyncOperationStatus AccStatus { get; }
    }
}
