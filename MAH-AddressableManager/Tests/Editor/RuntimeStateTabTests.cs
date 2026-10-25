using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Loading;
using MAH.AddressableManager.Editor.Window;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MAH.AddressableManager.Tests.Editor
{
    // 型名だけが同じで、namespace が違う型（並べ替えで型名だけを比べると区別できない）
    namespace SortA
    {
        internal sealed class SameName { }
    }

    namespace SortB
    {
        internal sealed class SameName { }
    }

    internal sealed class RuntimeStateTabTests
    {
        private static AssetEntrySnapshot CreateSnapshot(AssetKey key, System.Type asset_type)
            => new(key, asset_type, string.Empty, 1, AsyncOperationStatus.None, 0, 0);

        // 同じキーで、型名だけが同じ別の型は、0 にならず、順序が一意に決まる（Sort で行が入れ替わらない）
        [Test]
        public void CompareEntriesSameShortTypeNameDifferentNamespaceIsNotZero()
        {
            var key = AssetKey.FromPath("test/key");
            var a = CreateSnapshot(key, typeof(SortA.SameName));
            var b = CreateSnapshot(key, typeof(SortB.SameName));

            var forward = RuntimeStateTab.CompareEntries(a, b);
            var backward = RuntimeStateTab.CompareEntries(b, a);

            Assert.AreNotEqual(0, forward);
            Assert.AreEqual(System.Math.Sign(forward), -System.Math.Sign(backward), "引数を入れ替えると、符号が逆になるはず");
        }

        // 完全に同じ Entry（キーも型も同じ）は 0 になる
        [Test]
        public void CompareEntriesSameKeyAndTypeIsZero()
        {
            var key = AssetKey.FromPath("test/key");

            Assert.AreEqual(0, RuntimeStateTab.CompareEntries(
                CreateSnapshot(key, typeof(SortA.SameName)),
                CreateSnapshot(key, typeof(SortA.SameName))));
        }

        // キーの値が違えば、型にかかわらず、値の順（序数比較）で決まる
        [Test]
        public void CompareEntriesDifferentValueOrdersByValue()
        {
            var first = CreateSnapshot(AssetKey.FromPath("a/key"), typeof(SortB.SameName));
            var second = CreateSnapshot(AssetKey.FromPath("b/key"), typeof(SortA.SameName));

            Assert.Less(RuntimeStateTab.CompareEntries(first, second), 0);
            Assert.Greater(RuntimeStateTab.CompareEntries(second, first), 0);
        }
    }
}
