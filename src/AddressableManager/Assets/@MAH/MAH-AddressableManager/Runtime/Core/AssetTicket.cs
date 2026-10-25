using MAH.AddressableManager.Core.Loading;

namespace MAH.AddressableManager
{
    /// <summary>
    /// Load 1回ごとに発行される解放用チケットで、読み込んだアセットも持つ。
    /// 通し番号（AccSerial）で一意になり、同じ key・同じ寿命での二重解放を防ぐ。
    /// </summary>
    public readonly struct AssetTicket<T> where T : UnityEngine.Object
    {
        private readonly AssetKey _key;
        private readonly T _asset;
        private readonly LifetimeScopeId _scopeId;
        private readonly long _serial; // 0 は無効（default のチケット）

        /// <summary> 読み込んだアセットのキー </summary>
        public AssetKey AccKey => _key;
        /// <summary> 読み込んだアセット本体 </summary>
        public T AccAsset => _asset;
        internal LifetimeScopeId AccScopeId => _scopeId;
        internal long AccSerial => _serial;

        internal AssetTicket(AssetKey key, T asset, LifetimeScopeId scope_id, long serial)
        {
            _key = key;
            _asset = asset;
            _scopeId = scope_id;
            _serial = serial;
        }
    }
}
