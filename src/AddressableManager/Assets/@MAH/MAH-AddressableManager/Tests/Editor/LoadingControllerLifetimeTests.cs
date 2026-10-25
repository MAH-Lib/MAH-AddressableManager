using System;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class LoadingControllerLifetimeTests : LoadingControllerTestBase
    {
        // 明示解放していない分だけ回収される
        [Test]
        public async Task HandleSceneUnloadedOnlyUnreleasedEntriesAreReclaimed()
        {
            var scene = SceneManager.GetActiveScene();
            var key1 = AssetKey.FromPath("test/scene-leak");
            var key2 = AssetKey.FromPath("test/scene-explicit");

            var task1 = Controller.LoadAsync<FakeAsset>(key1, AssetLifetime.Scene(scene));
            var task2 = Controller.LoadAsync<FakeAsset>(key2, AssetLifetime.Scene(scene));
            Factory.GetWorker(key1).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            Factory.GetWorker(key2).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            var ticket1 = await task1;
            var ticket2 = await task2;

            Controller.Release(ticket2); // key2だけ明示解放しておく（未使用警告を避けるため変数として参照する）
            GC.KeepAlive(ticket1);

            Controller.HandleSceneUnloaded(scene);

            Assert.AreEqual(1, Factory.GetWorker(key1).DisposeCount, "明示解放していないkey1は回収されるはず");
            Assert.AreEqual(1, Factory.GetWorker(key2).DisposeCount, "key2は明示解放済みなので二重に解放されないはず");
        }

        // 未ロードのシーンを指定すると例外
        [Test]
        public async Task LoadAsyncUnloadedSceneThrowsArgumentException()
        {
            var key = AssetKey.FromPath("test/unloaded-scene");
            var unloaded_scene = default(Scene); // isLoaded == false

            ArgumentException caught = null;
            try
            {
                await Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Scene(unloaded_scene));
            }
            catch (ArgumentException ex)
            {
                caught = ex;
            }
            Assert.IsNotNull(caught);
        }

        // Owner寿命で同じownerへ複数Loadしても、Bindは1回だけ
        [Test]
        public async Task LoadAsyncOwnerLifetimeSameOwnerMultipleLoadsBindsOnce()
        {
            var owner = new GameObject("LoadingControllerLifetimeTests_Owner");
            try
            {
                var key1 = AssetKey.FromPath("test/owner-a");
                var key2 = AssetKey.FromPath("test/owner-b");

                var task1 = Controller.LoadAsync<FakeAsset>(key1, AssetLifetime.Owner(owner));
                var task2 = Controller.LoadAsync<FakeAsset>(key2, AssetLifetime.Owner(owner));
                Factory.GetWorker(key1).Complete(ScriptableObject.CreateInstance<FakeAsset>());
                Factory.GetWorker(key2).Complete(ScriptableObject.CreateInstance<FakeAsset>());
                await task1;
                await task2;

                Assert.AreEqual(1, WatcherBinder.BindCallCount, "同じownerなのでBindは1回だけのはず");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        // Owner破棄をシミュレートすると自動回収される
        [Test]
        public async Task OwnerDestroyedSimulationAutoReclaimed()
        {
            var owner = new GameObject("LoadingControllerLifetimeTests_Owner");
            try
            {
                var key = AssetKey.FromPath("test/owner-destroy");
                var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));
                Factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
                await task;

                WatcherBinder.SimulateOwnerDestroyed(owner);

                Assert.AreEqual(1, Factory.GetWorker(key).DisposeCount, "owner破棄で自動回収されるはず");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        // Owner寿命のチケットが尽きるとUnbindされる
        [Test]
        public async Task ReleaseOwnerLifetimeTicketsExhaustedUnbinds()
        {
            var owner = new GameObject("LoadingControllerLifetimeTests_Owner");
            try
            {
                var key = AssetKey.FromPath("test/owner-unbind");
                var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));
                Factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
                var ticket = await task;

                Controller.Release(ticket);

                Assert.AreEqual(1, WatcherBinder.UnbindCallCount, "最後のチケットが解放されたらUnbindされるはず");
                Assert.AreEqual(0, WatcherBinder.BoundCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        // nullを渡すと例外
        [Test]
        public void AssetLifetimeOwnerNullThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => AssetLifetime.Owner((GameObject)null));
        }

        // Load時点でownerがすでに破棄されていると例外
        [Test]
        public async Task LoadAsyncOwnerAlreadyDestroyedAtLoadTimeThrowsArgumentException()
        {
            var owner = new GameObject("LoadingControllerLifetimeTests_ToDestroy");
            var lifetime = AssetLifetime.Owner(owner);
            UnityEngine.Object.DestroyImmediate(owner); // Load時点で破棄済みの状態を作る

            var key = AssetKey.FromPath("test/owner-already-destroyed");

            ArgumentException caught = null;
            try
            {
                await Controller.LoadAsync<FakeAsset>(key, lifetime);
            }
            catch (ArgumentException ex)
            {
                caught = ex;
            }
            Assert.IsNotNull(caught);
        }

        // 受け取り後に寿命が終わった（Release の呼び忘れ）分は、リーク警告が1件出る
        [Test]
        public async Task OwnerDestroyedAfterDeliveryWarnsLeakOnce()
        {
            var owner = new GameObject("LoadingControllerLifetimeTests_Owner");
            try
            {
                var key = AssetKey.FromPath("test/owner-leak-after-delivery");
                var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));
                Factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
                var ticket = await task; // 呼び出し元がチケットを受け取った（Release はしない）
                GC.KeepAlive(ticket);

                using var counter = new WarningCounter("明示 Release されずに");
                WatcherBinder.SimulateOwnerDestroyed(owner);

                Assert.AreEqual(1, counter.AccCount, "受け取り後に寿命が終わった分は、リーク警告が出るはず");
                Assert.AreEqual(1, Factory.GetWorker(key).DisposeCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        // ロード中（受け取り前）に寿命が終わった分は、OperationCanceledException が届くだけで、リーク警告は出ない
        [Test]
        public async Task OwnerDestroyedWhileLoadingThrowsOperationCanceledWithoutLeakWarning()
        {
            var owner = new GameObject("LoadingControllerLifetimeTests_Owner");
            try
            {
                var key = AssetKey.FromPath("test/owner-ends-while-loading");
                using var counter = new WarningCounter("明示 Release されずに");
                var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));

                WatcherBinder.SimulateOwnerDestroyed(owner); // ロード中に寿命が終わる
                Factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());

                OperationCanceledException caught = null;
                try { await task; } catch (OperationCanceledException ex) { caught = ex; }

                Assert.IsNotNull(caught, "寿命の終了は OperationCanceledException になるはず");
                Assert.AreEqual(0, counter.AccCount, "受け取り前に寿命が終わった分は、リーク警告を出さないはず");
                await TestAsync.WaitUntil(() => Factory.GetWorker(key).DisposeCount == 1);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        // 未初期化（default）のキーは、Owner の Watcher を登録する前に例外になり、監視が残らない
        [Test]
        public async Task LoadAsyncDefaultKeyOwnerLifetimeThrowsBeforeWatching()
        {
            var owner = new GameObject("LoadingControllerLifetimeTests_Owner");
            try
            {
                ArgumentException caught = null;
                try
                {
                    await Controller.LoadAsync<FakeAsset>(default, AssetLifetime.Owner(owner));
                }
                catch (ArgumentException ex)
                {
                    caught = ex;
                }

                Assert.IsNotNull(caught, "default のキーは ArgumentException になるはず");
                Assert.AreEqual(0, WatcherBinder.BindCallCount, "寿命の解決より前に弾くので、Watcher は登録されないはず");
                Assert.AreEqual(0, Factory.CreateCallCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        // Label ロードの確保が途中で失敗してチケットが1件も無くても、Owner 破棄の通知で監視が外れる
        [Test]
        public async Task OwnerDestroyedWithNoTicketsStillUnbindsWatcher()
        {
            var owner = new GameObject("LoadingControllerLifetimeTests_Owner");
            try
            {
                // 1件目のアドレスが空文字のため、Watcher の登録（ResolveScope）の後、チケットの登録の前に例外になる
                const string LABEL = "test-label-no-tickets";
                LabelLocations.SetLocations(LABEL, new LabelLocation(string.Empty, typeof(FakeAsset)));
                try { await Controller.LoadByLabelAsync(LABEL, AssetLifetime.Owner(owner)); } catch (ArgumentException) { }
                Assert.AreEqual(1, WatcherBinder.BoundCount, "チケットが無いまま、監視だけが残っている状態を作れているはず");

                WatcherBinder.SimulateOwnerDestroyed(owner);

                Assert.AreEqual(1, WatcherBinder.UnbindCallCount, "チケットが無くても、Owner 破棄の通知で監視が外れるはず");
                Assert.AreEqual(0, WatcherBinder.BoundCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        // 同じ状況で、ポーリングが回収した後は、以後の走査の対象に残らない
        [Test]
        public async Task PollingReclaimsOwnerWithNoTicketsOnceAndStopsRescanning()
        {
            var owner = new GameObject("LoadingControllerLifetimeTests_Owner");
            try
            {
                const string LABEL = "test-label-no-tickets-polling";
                LabelLocations.SetLocations(LABEL, new LabelLocation(string.Empty, typeof(FakeAsset)));
                try { await Controller.LoadByLabelAsync(LABEL, AssetLifetime.Owner(owner)); } catch (ArgumentException) { }

                UnityEngine.Object.DestroyImmediate(owner); // Fake の Binder は OnDestroy を発火しないため、ポーリングでのみ検知される

                Controller.Tick(3.5f); // 間隔（3秒）を超える
                Assert.AreEqual(1, WatcherBinder.UnbindCallCount, "ポーリングで回収され、監視が外れるはず");

                Controller.Tick(3.5f);
                Assert.AreEqual(1, WatcherBinder.UnbindCallCount, "外れた後は、再び走査・回収されないはず");
            }
            finally
            {
                if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        // 残エントリをすべて強制解放しUnbindAllを呼ぶ
        [Test]
        public async Task DisposeForciblyReleasesAllEntriesAndCallsUnbindAll()
        {
            var key = AssetKey.FromPath("test/dispose");
            var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);
            Factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            await task;

            Controller.Dispose();

            Assert.AreEqual(1, Factory.GetWorker(key).DisposeCount);
            Assert.AreEqual(1, WatcherBinder.UnbindAllCallCount);
        }

        // Dispose後のLoadAsyncはObjectDisposedException
        [Test]
        public void LoadAsyncAfterDisposeThrowsObjectDisposedException()
        {
            Controller.Dispose();

            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await Controller.LoadAsync<FakeAsset>(AssetKey.FromPath("test/after-dispose"), AssetLifetime.Manual));
        }

        // Dispose後のReleaseは何もしない
        [Test]
        public async Task ReleaseAfterDisposeDoesNothing()
        {
            var key = AssetKey.FromPath("test/release-after-dispose");
            var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);
            Factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            var ticket = await task;

            Controller.Dispose(); // この時点で強制解放される
            Assert.AreEqual(1, Factory.GetWorker(key).DisposeCount);

            Assert.DoesNotThrow(() => Controller.Release(ticket));
            Assert.AreEqual(1, Factory.GetWorker(key).DisposeCount, "二重に解放されないはず");
        }
    }
}
