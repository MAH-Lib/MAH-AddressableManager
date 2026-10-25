using System;
using System.Collections.Generic;
using UnityEngine;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// owner寿命の破棄検知専用コンポーネント。内部専用であり、実装者は何も継承・実装しない。
    /// 同一GameObjectへの複数Loadでも、Watcherは1つに集約される（AssetLifetimeWatcherBinderが保証する）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // メニューから誤って追加できないようにする
    internal sealed class AssetLifetimeWatcher : MonoBehaviour
    {
        private readonly List<LifetimeScopeId> _scopes = new();
        private Action<LifetimeScopeId> _onDestroyed;

        internal void Register(LifetimeScopeId scope, Action<LifetimeScopeId> on_destroyed)
        {
            _scopes.Add(scope);
            _onDestroyed = on_destroyed; // Controller がフィールドにキャッシュした delegate（毎回生成しない）
        }

        internal void Unregister(LifetimeScopeId scope) => _scopes.Remove(scope);

#if UNITY_EDITOR
        /// <summary> このスコープが登録されているか。Editor のデバッグウィンドウ（Owner の可視化）用 </summary>
        internal bool IsRegistered(LifetimeScopeId scope) => _scopes.Contains(scope);
#endif

        /// <summary> Controller の破棄時に呼ぶ。破棄済みの Controller を呼ばないようにする </summary>
        internal void Unbind() => _onDestroyed = null;

        private void OnDestroy()
        {
            // _onDestroyed（ReleaseLifetime）経由でUnregisterが呼ばれ_scopesが変更されるため、
            // 1件ずつ先に取り除いてから通知する（列挙しないので変更されても壊れず、配列も確保しない）
            while (_scopes.Count > 0)
            {
                var last = _scopes.Count - 1;
                var scope = _scopes[last];
                _scopes.RemoveAt(last);
                _onDestroyed?.Invoke(scope);
            }
        }
    }
}
