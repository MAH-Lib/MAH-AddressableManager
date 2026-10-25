#if UNITY_EDITOR
namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// Owner 寿命の回収が、どの入口から起きたか。破棄検知の多重の安全網（Watcher の OnDestroy → ポーリング）のどちらが働いたかを区別する。
    /// ポーリング経由が多い場合は、Watcher が取りこぼしていること（例：一度も Active にならず OnDestroy が呼ばれない GameObject）を示す。
    /// </summary>
    internal enum OwnerReclaimRoute
    {
        /// <summary> Watcher の OnDestroy からの通知（主経路） </summary>
        OnDestroy = 0,
        /// <summary> 低頻度ポーリングで、破棄済みの Owner を見つけた（取りこぼしの回収） </summary>
        Polling = 1,
    }
}
#endif
