#if UNITY_EDITOR
namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// 監視中の Owner（Owner 寿命の GameObject）1件分の、取得した時点の値のコピー。
    /// GameObject や Watcher の参照は持たず、生存・貼り付けの状態は取得時に判定した bool で持つ（Runtime の Snapshot に UnityEngine.Object を持たせないため）。
    /// 確保中のチケット数は、Editor 側が発行済みチケットの Snapshot から InstanceID ごとに数える。
    /// </summary>
    internal readonly struct OwnerWatchSnapshot
    {
        private readonly int _instanceId;
        private readonly string _ownerName;
        private readonly bool _isAlive;
        private readonly bool _hasWatcher;
        private readonly bool _isRegistered;

        /// <summary> Owner の GameObject の InstanceID（スコープの値） </summary>
        internal int AccInstanceId => _instanceId;
        /// <summary> 登録時の GameObject 名。登録後にリネームされても変わらない </summary>
        internal string AccOwnerName => _ownerName;
        /// <summary> 取得した時点で GameObject が生きているか（偽 null でない）。false は、破棄済みなのに未回収の状態 </summary>
        internal bool AccIsAlive => _isAlive;
        /// <summary> 取得した時点で AssetLifetimeWatcher が貼られているか（GameObject が破棄済みなら false） </summary>
        internal bool AccHasWatcher => _hasWatcher;
        /// <summary> その Watcher に、このスコープが登録されているか（Watcher が無ければ false） </summary>
        internal bool AccIsRegistered => _isRegistered;

        internal OwnerWatchSnapshot(int instance_id, string owner_name, bool is_alive, bool has_watcher, bool is_registered)
        {
            _instanceId = instance_id;
            _ownerName = owner_name;
            _isAlive = is_alive;
            _hasWatcher = has_watcher;
            _isRegistered = is_registered;
        }
    }
}
#endif
