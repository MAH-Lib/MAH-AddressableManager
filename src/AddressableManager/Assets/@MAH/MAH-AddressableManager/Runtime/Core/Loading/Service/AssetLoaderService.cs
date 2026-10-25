using MAH.AddressableManager;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// AddressableLoaderBase の具象クラス。string キー（AssetKey.AccValue）のみを受け取って実ロードする。
    /// 入口（Path/Reference）ごとの派生クラスは作らず、これ1つで両方をまかなう。
    /// </summary>
    internal sealed class AssetLoaderService<T> : AddressableLoaderBase<T> where T : UnityEngine.Object
    {
        internal AssetLoaderService(AssetKey key, int max_retry, int retry_delay_ms)
            : base(key, max_retry, retry_delay_ms)
        {
        }
    }
}
