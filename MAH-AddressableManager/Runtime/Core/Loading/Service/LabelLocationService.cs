using System;
using System.Collections.Generic;
using System.Threading;
using MAH.AddressableManager.Core;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
#if ADDRESSABLE_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#else
using System.Threading.Tasks;
#endif

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// ILabelLocationService の実装。Addressables.LoadResourceLocationsAsync を型を絞らずに呼び、
    /// UnityEngine.Object ではない型と重複を除いた (PrimaryKey, ResourceType) の組を返す。
    /// ロケーションの handle は、キャンセル時も含めて必ず解放する。ロケーションの解決はリトライしない。
    /// </summary>
    internal sealed class LabelLocationService : ILabelLocationService
    {
        /// <inheritdoc/>
#if ADDRESSABLE_UNITASK_SUPPORT
        public async UniTask ResolveAsync(string label, List<LabelLocation> result, CancellationToken ct)
#else
        public async Task ResolveAsync(string label, List<LabelLocation> result, CancellationToken ct)
#endif
        {
            if (string.IsNullOrEmpty(label))
                throw new ArgumentException("Label が null または空文字です。", nameof(label));
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var handle = Addressables.LoadResourceLocationsAsync(label); // 型を絞らずに取得する
            try
            {
#if ADDRESSABLE_UNITASK_SUPPORT
                await handle.ToUniTask(cancellationToken: ct);
#else
                await handle.ToTask().WithCancellation(ct);
#endif
                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    // Label 専用の入口種別は作らないため、Label を Path 入口として包んだキーを持たせる
                    var message = $"Label「{label}」のロケーションの解決に失敗しました [Status: {handle.Status}]";
                    AddressableLogger.LogError(message);
                    throw new AssetLoadException(AssetKey.FromPath(label), message, handle.OperationException);
                }
                Collect(label, handle.Result, result);
            }
            finally
            {
                ReleaseWhenCompleted(handle);
            }
        }

        /// <summary>
        /// ロケーションから、UnityEngine.Object ではない型と重複を除いて result に詰める。
        /// 同じ (PrimaryKey, ResourceType) が複数のロケーションから出てきたら、アドレスの重複として警告する
        /// （LoadAssetAsync(key) は先頭の1件しか読まないため、区別できない）。
        /// </summary>
        internal static void Collect(string label, IList<IResourceLocation> locations, List<LabelLocation> result)
        {
            if (locations == null || locations.Count == 0)
                return;

            var added = new HashSet<(string, Type)>(); // 呼び出しごとに確保する（毎フレーム呼ぶ処理ではない）
            for (int i = 0; i < locations.Count; i++)
            {
                var primary_key = locations[i].PrimaryKey;
                var resource_type = locations[i].ResourceType;

                // Label は Scene のエントリにも付けられ、その場合の型は SceneInstance になる。
                // Worker の型制約（where T : UnityEngine.Object）に違反するため、読み込まずに除外する
                if (resource_type == null || !typeof(UnityEngine.Object).IsAssignableFrom(resource_type))
                {
                    AddressableLogger.LogWarning($"Label「{label}」の {primary_key} は UnityEngine.Object ではない型（{resource_type?.Name}）のため、読み込みません。");
                    continue;
                }

                if (!added.Add((primary_key, resource_type)))
                {
                    AddressableLogger.LogWarning($"Label「{label}」でアドレスが重複しています：{primary_key}<{resource_type.Name}>。先頭の1件だけを読み込みます。");
                    continue;
                }
                result.Add(new LabelLocation(primary_key, resource_type));
            }
        }

        // キャンセルされて待機をやめた場合など、完了前の handle は完了を待ってから解放する
        private static void ReleaseWhenCompleted(AsyncOperationHandle<IList<IResourceLocation>> handle)
        {
            if (!handle.IsValid())
                return;
            if (handle.IsDone)
            {
                Addressables.Release(handle);
                return;
            }
            handle.Completed += completed => Addressables.Release(completed); // 完了前にキャンセルされた経路だけで生成する
        }
    }
}
