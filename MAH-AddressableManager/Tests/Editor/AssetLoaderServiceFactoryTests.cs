using System;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary> 実際の AssetLoaderServiceFactory を使い、実行時の型から Worker を生成できることを確認する（ロードは行わない）。 </summary>
    internal sealed class AssetLoaderServiceFactoryTests
    {
        private AssetLoaderServiceFactory _factory;

        [SetUp]
        public void SetUp()
        {
            _factory = new AssetLoaderServiceFactory(new AddressableLoadingSettings(true, 3f, 3, 1000));
        }

        // 型引数で生成した Worker は、その型を持つ
        [Test]
        public void CreateGenericReturnsWorkerOfThatType()
        {
            var worker = _factory.Create<Sprite>(AssetKey.FromPath("test/key"));
            Assert.AreEqual(typeof(Sprite), worker.AccAssetType);
        }

        // 実行時の型から生成した Worker も、その型を持つ
        [Test]
        public void CreateByTypeReturnsWorkerOfThatType()
        {
            var key = AssetKey.FromPath("test/key");
            Assert.AreEqual(typeof(Sprite), _factory.Create(key, typeof(Sprite)).AccAssetType);
            Assert.AreEqual(typeof(Texture2D), _factory.Create(key, typeof(Texture2D)).AccAssetType);
            Assert.AreEqual(typeof(GameObject), _factory.Create(key, typeof(GameObject)).AccAssetType);
            Assert.AreEqual(typeof(Sprite), _factory.Create(key, typeof(Sprite)).AccAssetType, "キャッシュした delegate を使った2回目も同じ結果のはず");
        }

        // UnityEngine.Object の派生型ではない型は例外
        [Test]
        public void CreateByTypeNonUnityObjectThrows()
        {
            Assert.Throws<ArgumentException>(() => _factory.Create(AssetKey.FromPath("test/key"), typeof(string)));
        }

        // 型が null なら例外
        [Test]
        public void CreateByTypeNullThrows()
        {
            Assert.Throws<ArgumentNullException>(() => _factory.Create(AssetKey.FromPath("test/key"), null));
        }
    }
}
