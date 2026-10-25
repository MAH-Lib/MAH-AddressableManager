using System;
using System.Collections.Generic;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// IAssetLifetimeWatcherBinder の偽実装。実際に GameObject へ AddComponent せず、
    /// テストから owner の破棄（AssetLifetimeWatcher.OnDestroy 相当）を手動で発火できるようにする。
    /// </summary>
    internal sealed class FakeAssetLifetimeWatcherBinder : IAssetLifetimeWatcherBinder
    {
        private readonly List<(GameObject Owner, LifetimeScopeId Scope, Action<LifetimeScopeId> OnDestroyed)> _bound = new();

        internal int BindCallCount { get; private set; }
        internal int UnbindCallCount { get; private set; }
        internal int UnbindAllCallCount { get; private set; }

        /// <summary> 現在 Bind されたままの件数 </summary>
        internal int BoundCount => _bound.Count;

        public void Bind(GameObject owner, LifetimeScopeId scope, Action<LifetimeScopeId> on_destroyed)
        {
            BindCallCount++;
            _bound.Add((owner, scope, on_destroyed));
        }

        public void Unbind(GameObject owner, LifetimeScopeId scope)
        {
            UnbindCallCount++;
            _bound.RemoveAll(e => ReferenceEquals(e.Owner, owner) && e.Scope.Equals(scope));
        }

        public void UnbindAll()
        {
            UnbindAllCallCount++;
            _bound.Clear();
        }

        /// <summary> テストから owner の破棄を疑似的に発火させる（AssetLifetimeWatcher.OnDestroy 相当） </summary>
        internal void SimulateOwnerDestroyed(GameObject owner)
        {
            var matches = _bound.FindAll(e => ReferenceEquals(e.Owner, owner));
            foreach (var entry in matches)
                entry.OnDestroyed?.Invoke(entry.Scope);
        }
    }
}
