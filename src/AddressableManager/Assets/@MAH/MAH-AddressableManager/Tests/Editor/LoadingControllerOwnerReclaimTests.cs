using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// Owner の回収経路（OnDestroy / ポーリング）が OwnerReclaimRegistryModel に記録されるかのテスト。
    /// 記録するのは監視中の Owner だけで、明示 Release で監視が外れた Owner や回収済みの Owner は記録しない。
    /// 記録を足しても、解放の動作（RefCount・リークの記録）は変わらない。
    /// </summary>
    internal sealed class LoadingControllerOwnerReclaimTests : LoadingControllerTestBase
    {
        private readonly List<GameObject> _owners = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var owner in _owners)
            {
                if (owner != null)
                    UnityEngine.Object.DestroyImmediate(owner);
            }
            _owners.Clear();
        }

        private GameObject CreateOwner(string name)
        {
            var owner = new GameObject(name);
            _owners.Add(owner);
            return owner;
        }

        private List<OwnerReclaimSnapshot> CopyReclaims()
        {
            var reclaims = new List<OwnerReclaimSnapshot>();
            OwnerReclaims.CopyReclaimsTo(reclaims);
            return reclaims;
        }

        private async Task<AssetTicket<FakeAsset>> LoadOwnerAsync(string address, GameObject owner)
        {
            var key = AssetKey.FromPath(address);
            var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));
            Factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            return await task;
        }

        // Watcher の OnDestroy 経由の回収は、経路 OnDestroy で記録される（名前は登録時のもの）
        [Test]
        public async Task OwnerDestroyedNotificationIsRecordedAsOnDestroy()
        {
            var owner = CreateOwner("Enemy_A");
            await LoadOwnerAsync("test/reclaim-ondestroy", owner);

            WatcherBinder.SimulateOwnerDestroyed(owner);

            var reclaims = CopyReclaims();
            Assert.AreEqual(1, reclaims.Count);
            Assert.AreEqual(OwnerReclaimRoute.OnDestroy, reclaims[0].AccRoute);
            Assert.AreEqual("Enemy_A", reclaims[0].AccOwnerName);
            Assert.AreEqual(owner.GetInstanceID(), reclaims[0].AccLastInstanceId);
            Assert.AreEqual(1, reclaims[0].AccCount);
            Assert.AreEqual(1, reclaims[0].AccTicketCount);
        }

        // ポーリングで破棄済みの Owner が見つかった回収は、経路 Polling で記録される（名前は登録時のもの）
        [Test]
        public async Task PollingReclaimIsRecordedAsPollingWithRegisteredName()
        {
            var owner = CreateOwner("Enemy_B");
            await LoadOwnerAsync("test/reclaim-polling", owner);
            var instance_id = owner.GetInstanceID();

            UnityEngine.Object.DestroyImmediate(owner); // Watcher の通知を経ずに破棄された状態を作る
            Controller.Tick(10f); // ポーリング間隔（3秒）を超える

            var reclaims = CopyReclaims();
            Assert.AreEqual(1, reclaims.Count);
            Assert.AreEqual(OwnerReclaimRoute.Polling, reclaims[0].AccRoute);
            Assert.AreEqual("Enemy_B", reclaims[0].AccOwnerName, "破棄後でも、登録時の名前で記録するはず");
            Assert.AreEqual(instance_id, reclaims[0].AccLastInstanceId);
        }

        // 記録するチケット数は、回収前に残っていた件数（同じ Owner の複数チケット）
        [Test]
        public async Task TicketCountIsTheNumberOfTicketsBeforeReclaim()
        {
            var owner = CreateOwner("Enemy_C");
            await LoadOwnerAsync("test/reclaim-count-1", owner);
            await LoadOwnerAsync("test/reclaim-count-2", owner);
            await LoadOwnerAsync("test/reclaim-count-3", owner);

            WatcherBinder.SimulateOwnerDestroyed(owner);

            var reclaims = CopyReclaims();
            Assert.AreEqual(1, reclaims.Count, "同じ Owner の回収は1回だけ記録するはず");
            Assert.AreEqual(3, reclaims[0].AccTicketCount);
        }

        // 同じ経路・同じ名前の Owner の回収は、別の GameObject でも1行にまとまる
        [Test]
        public async Task SameNameOwnersAreAggregatedIntoOneRow()
        {
            var first = CreateOwner("Enemy(Clone)");
            var second = CreateOwner("Enemy(Clone)");
            await LoadOwnerAsync("test/reclaim-agg-1", first);
            await LoadOwnerAsync("test/reclaim-agg-2", second);

            WatcherBinder.SimulateOwnerDestroyed(first);
            WatcherBinder.SimulateOwnerDestroyed(second);

            var reclaims = CopyReclaims();
            Assert.AreEqual(1, reclaims.Count);
            Assert.AreEqual(2, reclaims[0].AccCount);
            Assert.AreEqual(2, reclaims[0].AccTicketCount);
            Assert.AreEqual(second.GetInstanceID(), reclaims[0].AccLastInstanceId, "最後に回収された Owner の ID になるはず");
        }

        // 全チケットを明示 Release して監視が外れた Owner の破棄は、記録しない（回収ではない）
        [Test]
        public async Task OwnerDestroyedAfterExplicitReleaseIsNotRecorded()
        {
            var owner = CreateOwner("Enemy_D");
            var ticket = await LoadOwnerAsync("test/reclaim-released", owner);
            Controller.Release(ticket);

            WatcherBinder.SimulateOwnerDestroyed(owner);
            UnityEngine.Object.DestroyImmediate(owner);
            Controller.Tick(10f);

            Assert.AreEqual(0, CopyReclaims().Count);
        }

        // OnDestroy で回収した Owner は、その後のポーリングで二重に記録されない
        [Test]
        public async Task OnDestroyReclaimIsNotRecordedAgainByPolling()
        {
            var owner = CreateOwner("Enemy_E");
            await LoadOwnerAsync("test/reclaim-no-double-1", owner);

            WatcherBinder.SimulateOwnerDestroyed(owner);
            UnityEngine.Object.DestroyImmediate(owner);
            Controller.Tick(10f);

            var reclaims = CopyReclaims();
            Assert.AreEqual(1, reclaims.Count);
            Assert.AreEqual(OwnerReclaimRoute.OnDestroy, reclaims[0].AccRoute);
            Assert.AreEqual(1, reclaims[0].AccCount);
        }

        // ポーリングで回収した Owner に、遅れて OnDestroy の通知が届いても、二重に記録されない
        [Test]
        public async Task PollingReclaimIsNotRecordedAgainByLateOnDestroy()
        {
            var binder = new CapturingBinder();
            var controller = CreateController(binder, out var owner_reclaims, out var factory);
            var owner = CreateOwner("Enemy_F");
            var key = AssetKey.FromPath("test/reclaim-late-ondestroy");
            var task = controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));
            factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            await task;

            UnityEngine.Object.DestroyImmediate(owner);
            controller.Tick(10f);
            binder.InvokeCaptured(); // 回収済みのスコープへ、遅れて通知が届いた状態を作る

            var reclaims = new List<OwnerReclaimSnapshot>();
            owner_reclaims.CopyReclaimsTo(reclaims);
            Assert.AreEqual(1, reclaims.Count);
            Assert.AreEqual(OwnerReclaimRoute.Polling, reclaims[0].AccRoute);
            Assert.AreEqual(1, reclaims[0].AccCount);
            controller.Dispose();
        }

        // Manual 寿命・Scene 寿命は監視中の Owner に入らない（Owner 寿命の1件だけが残る）
        [Test]
        public async Task ManualAndSceneLifetimesAreNotWatched()
        {
            var owner = CreateOwner("Enemy_I");
            var manual_key = AssetKey.FromPath("test/watch-manual");
            var scene_key = AssetKey.FromPath("test/watch-scene");
            var manual_task = Controller.LoadAsync<FakeAsset>(manual_key, AssetLifetime.Manual);
            var scene_task = Controller.LoadAsync<FakeAsset>(scene_key, AssetLifetime.Scene(UnityEngine.SceneManagement.SceneManager.GetActiveScene()));
            Factory.GetWorker(manual_key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            Factory.GetWorker(scene_key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            await manual_task;
            await scene_task;
            await LoadOwnerAsync("test/watch-owner", owner);

            var owners = new List<OwnerWatchSnapshot>();
            OwnerWatch.CopyOwnersTo(owners);

            Assert.AreEqual(1, owners.Count, "監視中の一覧には Owner 寿命だけが入るはず");
            Assert.AreEqual(owner.GetInstanceID(), owners[0].AccInstanceId);
        }

        // Dispose で監視が外れるため、Dispose 後に届いた通知は IsWatching で弾かれ、記録されない（例外にもならない）
        [Test]
        public async Task NotificationAfterDisposeIsNotRecorded()
        {
            var binder = new CapturingBinder();
            var controller = CreateController(binder, out var owner_reclaims, out var factory);
            var owner = CreateOwner("Enemy_G");
            var key = AssetKey.FromPath("test/reclaim-after-dispose");
            var task = controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));
            factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            await task;

            controller.Dispose();
            binder.InvokeCaptured();

            var reclaims = new List<OwnerReclaimSnapshot>();
            owner_reclaims.CopyReclaimsTo(reclaims);
            Assert.AreEqual(0, reclaims.Count);
        }

        // 回収の動作は従来どおり：アセットの解放・リークの記録が行われる
        [Test]
        public async Task ReclaimStillReleasesAssetAndRecordsLeak()
        {
            var owner = CreateOwner("Enemy_H");
            var key = AssetKey.FromPath("test/reclaim-existing-behavior");
            await LoadOwnerAsync("test/reclaim-existing-behavior", owner);

            WatcherBinder.SimulateOwnerDestroyed(owner);

            Assert.AreEqual(1, Factory.GetWorker(key).DisposeCount, "Owner の回収で、アセットが解放されるはず");
            var leaks = new List<LeakSnapshot>();
            Leaks.CopyLeaksTo(leaks);
            Assert.AreEqual(1, leaks.Count, "リークの記録も従来どおり行われるはず");
        }

        private static LoadingController CreateController(
            IAssetLifetimeWatcherBinder binder, out OwnerReclaimRegistryModel owner_reclaims, out FakeAssetLoaderServiceFactory factory)
        {
            var settings = new AddressableLoadingSettings(true, 3f, 3, 1000);
            factory = new FakeAssetLoaderServiceFactory();
            owner_reclaims = new OwnerReclaimRegistryModel();
            return new LoadingController(
                settings, factory, new FakeLabelLocationService(), binder, new FakeInitializationGate(),
                new AssetRegistryModel(), new IssuedTicketRegistryModel(), new OwnerWatchRegistryModel(), new LeakRegistryModel(), owner_reclaims);
        }

        /// <summary>
        /// Watcher に渡された通知（delegate）を保持し、Unbind や UnbindAll の後でも呼べる偽の Binder。
        /// 回収済み・Dispose 後のスコープへ、遅れて届く OnDestroy の通知を再現する。
        /// </summary>
        private sealed class CapturingBinder : IAssetLifetimeWatcherBinder
        {
            private readonly List<(LifetimeScopeId Scope, Action<LifetimeScopeId> OnDestroyed)> _captured = new();

            public void Bind(GameObject owner, LifetimeScopeId scope, Action<LifetimeScopeId> on_destroyed)
                => _captured.Add((scope, on_destroyed));

            public void Unbind(GameObject owner, LifetimeScopeId scope) { }

            public void UnbindAll() { }

            internal void InvokeCaptured()
            {
                foreach (var entry in _captured)
                    entry.OnDestroyed?.Invoke(entry.Scope);
            }
        }
    }
}
