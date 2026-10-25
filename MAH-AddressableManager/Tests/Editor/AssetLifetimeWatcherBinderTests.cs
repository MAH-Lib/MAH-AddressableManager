using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// AssetLifetimeWatcherBinder（実物）のテスト。実際に GameObject へ Watcher を貼り付ける。
    /// AssetLifetimeWatcher は ExecuteAlways ではないため、EditMode では OnDestroy が呼ばれない。
    /// そのため OnDestroy の通知は対象外で、Bind / Unbind による記録の増減と、Watcher の集約を確認する。
    /// </summary>
    internal sealed class AssetLifetimeWatcherBinderTests
    {
        private static LifetimeScopeId Scope(int value) => new LifetimeScopeId(LifetimeKind.Owner, value);

        private static void NoOp(LifetimeScopeId scope) { }

        // Bind すると記録が増え、Watcher が貼り付く
        [Test]
        public void BindAddsWatcherAndRecordsScope()
        {
            var binder = new AssetLifetimeWatcherBinder();
            var owner = new GameObject("BinderTests_Owner");
            try
            {
                binder.Bind(owner, Scope(1), NoOp);

                Assert.AreEqual(1, binder.AccBoundCount);
                Assert.IsTrue(owner.TryGetComponent<AssetLifetimeWatcher>(out _), "Watcher が貼り付いているはず");
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        // 同じ GameObject へ別のスコープを Bind しても、Watcher は1つのまま
        [Test]
        public void BindSameOwnerTwiceKeepsSingleWatcher()
        {
            var binder = new AssetLifetimeWatcherBinder();
            var owner = new GameObject("BinderTests_Owner");
            try
            {
                binder.Bind(owner, Scope(1), NoOp);
                binder.Bind(owner, Scope(2), NoOp);

                Assert.AreEqual(1, owner.GetComponents<AssetLifetimeWatcher>().Length, "Watcher は1つに集約されるはず");
                Assert.AreEqual(2, binder.AccBoundCount);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        // owner が生きたまま Unbind すると、記録が外れる
        [Test]
        public void UnbindAliveOwnerRemovesRecord()
        {
            var binder = new AssetLifetimeWatcherBinder();
            var owner = new GameObject("BinderTests_Owner");
            try
            {
                binder.Bind(owner, Scope(1), NoOp);

                binder.Unbind(owner, Scope(1));

                Assert.AreEqual(0, binder.AccBoundCount);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        // owner が破棄済みでも、Unbind で記録が外れる（破棄済みの Watcher が溜まり続けない）
        [Test]
        public void UnbindDestroyedOwnerStillRemovesRecord()
        {
            var binder = new AssetLifetimeWatcherBinder();
            var owner = new GameObject("BinderTests_Owner");
            binder.Bind(owner, Scope(1), NoOp);
            Object.DestroyImmediate(owner);

            binder.Unbind(owner, Scope(1));

            Assert.AreEqual(0, binder.AccBoundCount, "破棄済みの owner でも、記録は外れるはず");
        }

        // Owner の生成と破棄を繰り返しても、記録が増え続けない
        [Test]
        public void RepeatedBindAndUnbindWithDestroyedOwnersDoesNotAccumulate()
        {
            var binder = new AssetLifetimeWatcherBinder();
            for (int i = 0; i < 10; i++)
            {
                var owner = new GameObject("BinderTests_Owner");
                binder.Bind(owner, Scope(i), NoOp);
                Object.DestroyImmediate(owner);
                binder.Unbind(owner, Scope(i));
            }

            Assert.AreEqual(0, binder.AccBoundCount);
        }

        // UnbindAll で、すべての記録が外れる。破棄済みの Watcher が混ざっていても例外にならない
        [Test]
        public void UnbindAllClearsRecordsEvenWithDestroyedWatchers()
        {
            var binder = new AssetLifetimeWatcherBinder();
            var alive = new GameObject("BinderTests_Alive");
            var destroyed = new GameObject("BinderTests_Destroyed");
            try
            {
                binder.Bind(alive, Scope(1), NoOp);
                binder.Bind(destroyed, Scope(2), NoOp);
                Object.DestroyImmediate(destroyed);

                Assert.DoesNotThrow(() => binder.UnbindAll());
                Assert.AreEqual(0, binder.AccBoundCount);
            }
            finally
            {
                Object.DestroyImmediate(alive);
            }
        }
    }
}
