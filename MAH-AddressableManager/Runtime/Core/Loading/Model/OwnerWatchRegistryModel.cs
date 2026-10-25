using System.Collections.Generic;
using UnityEngine;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// Owner寿命のポーリング対象owner・ポーリング経過時間という状態データを保持するModel。
    /// </summary>
    internal sealed class OwnerWatchRegistryModel
    {
        private readonly Dictionary<LifetimeScopeId, GameObject> _watchedOwners = new();
        private readonly List<LifetimeScopeId> _destroyedScanBuffer = new(); // CollectDestroyedScopes の再利用バッファ
        private float _elapsedSec;
#if UNITY_EDITOR
        // 登録時の Owner 名。ポーリングで見つかる Owner は偽 null で名前が取れないため、Editor のデバッグウィンドウ用に文字列で保持する
        private readonly Dictionary<LifetimeScopeId, string> _ownerNames = new();
#endif

        internal int Count => _watchedOwners.Count;

        internal bool IsWatching(LifetimeScopeId scope_id) => _watchedOwners.ContainsKey(scope_id);

        internal void Watch(LifetimeScopeId scope_id, GameObject owner)
        {
            _watchedOwners[scope_id] = owner;
#if UNITY_EDITOR
            _ownerNames[scope_id] = owner != null ? owner.name : string.Empty;
#endif
        }

        internal void Unwatch(LifetimeScopeId scope_id)
        {
            _watchedOwners.Remove(scope_id);
#if UNITY_EDITOR
            _ownerNames.Remove(scope_id);
#endif
        }

#if UNITY_EDITOR
        /// <summary> 登録時の Owner 名を取得する。登録されていなければ false </summary>
        internal bool TryGetOwnerName(LifetimeScopeId scope_id, out string owner_name) => _ownerNames.TryGetValue(scope_id, out owner_name);
#endif

        internal bool TryGetOwner(LifetimeScopeId scope_id, out GameObject owner) => _watchedOwners.TryGetValue(scope_id, out owner);

        /// <summary> 経過時間を加算し、ポーリング間隔を超えていたらtrueを返す（超えていたら経過時間を0に戻す） </summary>
        internal bool AdvanceElapsed(float delta_time, float interval_sec)
        {
            _elapsedSec += delta_time;
            if (_elapsedSec < interval_sec)
                return false;
            _elapsedSec = 0f;
            return true;
        }

#if UNITY_EDITOR
        // 呼ぶのは AddressableDebugReader のみ
        /// <summary>
        /// 監視中の Owner を owners へ書き出す（Clear してから）。生存・Watcher の貼り付け・登録の状態は、ここ（取得時）で判定して bool にする。
        /// 並びは登録の順とは限らない。
        /// _watchedOwners には Owner 種別のスコープしか入らない（Watch を呼ぶのは LoadingController.ResolveOwnerScope だけ）ため、種別の絞り込みはしない。
        /// </summary>
        internal void CopyOwnersTo(List<OwnerWatchSnapshot> owners)
        {
            owners.Clear();
            foreach (var pair in _watchedOwners) // Dictionary は具体型のまま foreach する（ボックス化を避ける）
            {
                var owner = pair.Value;
                var is_alive = owner != null; // 偽null判定
                var has_watcher = false;
                var is_registered = false;
                if (is_alive && owner.TryGetComponent<AssetLifetimeWatcher>(out var watcher))
                {
                    has_watcher = true;
                    is_registered = watcher.IsRegistered(pair.Key);
                }
                _ownerNames.TryGetValue(pair.Key, out var owner_name);
                owners.Add(new OwnerWatchSnapshot(pair.Key.AccValue, owner_name ?? string.Empty, is_alive, has_watcher, is_registered));
            }
        }
#endif

        /// <summary> 偽nullになったownerのスコープ一覧（再利用バッファのスナップショット） </summary>
        internal IReadOnlyList<LifetimeScopeId> CollectDestroyedScopes()
        {
            _destroyedScanBuffer.Clear();
            foreach (var pair in _watchedOwners)
            {
                if (pair.Value == null) // 偽null判定
                    _destroyedScanBuffer.Add(pair.Key);
            }
            return _destroyedScanBuffer;
        }

        internal void Clear()
        {
            _watchedOwners.Clear();
#if UNITY_EDITOR
            _ownerNames.Clear();
#endif
        }
    }
}
