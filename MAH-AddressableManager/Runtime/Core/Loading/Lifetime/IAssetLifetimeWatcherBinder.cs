using System;
using UnityEngine;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// Watcher の貼り付け・解除を担う窓口。LoadingController は AddComponent を直接呼ばず、
    /// これ経由で Watcher を扱う（EditMode テストで差し替えるため）。
    /// </summary>
    internal interface IAssetLifetimeWatcherBinder
    {
        void Bind(GameObject owner, LifetimeScopeId scope, Action<LifetimeScopeId> on_destroyed);
        void Unbind(GameObject owner, LifetimeScopeId scope);
        /// <summary> Manager の破棄時に、貼ったすべての Watcher を Unbind する </summary>
        void UnbindAll();
    }
}
