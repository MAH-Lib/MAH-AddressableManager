using System;

namespace MAH.AddressableManager
{
    /// <summary>
    /// アセットの読み込みに失敗したことを示す例外。リトライを使い切っても成功しなかった場合に投げられる。
    /// 最後に発生した例外は InnerException に入る。
    /// </summary>
    public sealed class AssetLoadException : Exception
    {
        private readonly AssetKey _key;

        /// <summary> 読み込みに失敗したアセットのキー </summary>
        public AssetKey AccKey => _key;

        public AssetLoadException(AssetKey key, string message, Exception inner_exception)
            : base(message, inner_exception)
        {
            _key = key;
        }
    }
}
