using System;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class AssetEntryKeyTests
    {
        // 同じキー・同じ型なら等しい
        [Test]
        public void SameKeyAndSameTypeAreEqual()
        {
            var a = new AssetEntryKey(AssetKey.FromPath("test/key"), typeof(Sprite));
            var b = new AssetEntryKey(AssetKey.FromPath("test/key"), typeof(Sprite));
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        // 同じキーでも型が違えば等しくない（別のエントリになる）
        [Test]
        public void SameKeyAndDifferentTypeAreNotEqual()
        {
            var a = new AssetEntryKey(AssetKey.FromPath("test/key"), typeof(Sprite));
            var b = new AssetEntryKey(AssetKey.FromPath("test/key"), typeof(Texture2D));
            Assert.AreNotEqual(a, b);
        }

        // 型が同じでもキーが違えば等しくない
        [Test]
        public void DifferentKeyAndSameTypeAreNotEqual()
        {
            var a = new AssetEntryKey(AssetKey.FromPath("test/key-a"), typeof(Sprite));
            var b = new AssetEntryKey(AssetKey.FromPath("test/key-b"), typeof(Sprite));
            Assert.AreNotEqual(a, b);
        }

        // 型が null なら例外
        [Test]
        public void NullTypeThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new AssetEntryKey(AssetKey.FromPath("test/key"), null));
        }
    }
}
