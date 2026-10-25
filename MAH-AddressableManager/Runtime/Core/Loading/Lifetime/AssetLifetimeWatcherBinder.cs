using System;
using System.Collections.Generic;
using UnityEngine;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary> IAssetLifetimeWatcherBinder の実装。実際に GameObject へ AssetLifetimeWatcher を貼り付ける </summary>
    internal sealed class AssetLifetimeWatcherBinder : IAssetLifetimeWatcherBinder
    {
        // Bind 中のスコープと、その Watcher の対応。Owner 寿命は1 GameObject につき1スコープなので、スコープが鍵になる。
        // Unbind でスコープごとに取り除く（Owner の生成と破棄を繰り返しても、破棄済みの Watcher が溜まり続けない）
        private readonly Dictionary<LifetimeScopeId, AssetLifetimeWatcher> _boundWatchers = new();

        /// <summary> 現在 Bind されたままのスコープ数 </summary>
        internal int AccBoundCount => _boundWatchers.Count;

        public void Bind(GameObject owner, LifetimeScopeId scope, Action<LifetimeScopeId> on_destroyed)
        {
            var watcher = GetOrAddWatcher(owner);
            watcher.Register(scope, on_destroyed);
            _boundWatchers[scope] = watcher;
        }

        public void Unbind(GameObject owner, LifetimeScopeId scope)
        {
            _boundWatchers.Remove(scope); // owner が破棄済みでも、記録は必ず外す
            if (owner == null) return; // すでに破棄済みなら Watcher 側は何もしない（Watcher.OnDestroyで処理済み）
            if (owner.TryGetComponent<AssetLifetimeWatcher>(out var watcher))
                watcher.Unregister(scope);
        }

        public void UnbindAll()
        {
            foreach (var pair in _boundWatchers) // Dictionary は具体型のまま foreach する（ボックス化を避ける）
            {
                if (pair.Value != null)
                    pair.Value.Unbind();
            }
            _boundWatchers.Clear();
        }

        // 同一GameObjectへの複数Loadでも Watcher は1つに集約する
        private AssetLifetimeWatcher GetOrAddWatcher(GameObject owner)
        {
            if (owner.TryGetComponent<AssetLifetimeWatcher>(out var watcher))
                return watcher;

            return owner.AddComponent<AssetLifetimeWatcher>();
        }
    }
}
