using System;
using NUnit.Framework;
using MAH.AddressableManager;
using UnityEngine.AddressableAssets;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class AssetKeyTests
    {
        // 同じパスなら等しい
        [Test]
        public void FromPathSamePathAreEqual()
        {
            var a = AssetKey.FromPath("Assets/foo.png");
            var b = AssetKey.FromPath("Assets/foo.png");
            Assert.AreEqual(a, b);
        }

        // 異なるパスなら等しくない
        [Test]
        public void FromPathDifferentPathAreNotEqual()
        {
            var a = AssetKey.FromPath("Assets/foo.png");
            var b = AssetKey.FromPath("Assets/bar.png");
            Assert.AreNotEqual(a, b);
        }

        // PathとReferenceは値が同じでも種別が違うため等しくない
        [Test]
        public void PathAndReferenceSameValueDifferentKindAreNotEqual()
        {
            var reference = new AssetReference("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4");
            var path_key = AssetKey.FromPath(reference.RuntimeKey.ToString());
            var reference_key = AssetKey.FromReference(reference);

            Assert.AreNotEqual(path_key, reference_key);
        }

        // nullは例外
        [Test]
        public void FromPathNullThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => AssetKey.FromPath(null));
        }

        // 空文字は例外
        [Test]
        public void FromPathEmptyThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => AssetKey.FromPath(string.Empty));
        }

        // nullは例外
        [Test]
        public void FromReferenceNullThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => AssetKey.FromReference(null));
        }

        // 無効な参照は例外
        [Test]
        public void FromReferenceInvalidReferenceThrowsArgumentException()
        {
            var reference = new AssetReference(); // GUID未設定のためRuntimeKeyIsValid()がfalse
            Assert.Throws<ArgumentException>(() => AssetKey.FromReference(reference));
        }
    }
}
