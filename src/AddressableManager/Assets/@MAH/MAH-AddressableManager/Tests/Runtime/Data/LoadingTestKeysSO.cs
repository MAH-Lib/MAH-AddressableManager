using MAH.AddressableManager.Attribute;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Runtime
{
    /// <summary>
    /// PlayMode テストで使うアドレスを持つテスト専用の設定アセット。
    /// テストコードにアドレスを直書きしないよう、Inspector のプルダウンで設定してもらう。
    /// </summary>
    [CreateAssetMenu(menuName = "MAH/Addressable/Tests/LoadingTestKeys", fileName = "LoadingTestKeys")]
    public sealed class LoadingTestKeysSO : ScriptableObject
    {
        [SerializeField, AddressablePath] private string _spriteAddress;
        [SerializeField, AddressablePath] private string _prefabAddress;

        /// <summary> Sprite 用のテストアドレス </summary>
        public string AccSpriteAddress => _spriteAddress;
        /// <summary> Prefab 用のテストアドレス </summary>
        public string AccPrefabAddress => _prefabAddress;
    }
}
