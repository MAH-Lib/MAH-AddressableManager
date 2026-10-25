using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// リークが LeakRegistryModel に記録されるかのテスト。
    /// 記録の対象は、ログに警告を出す対象と同じ（呼び出し元にチケットを返し終えた分だけ）。ロード中の回収・明示解放済みは記録しない。
    /// </summary>
    internal sealed class LoadingControllerLeakTests : LoadingControllerTestBase
    {
        private const string LABEL = "leak-label";
        private static readonly Type ASSET_TYPE = typeof(FakeAsset);

        private GameObject _owner;

        [TearDown]
        public void TearDown()
        {
            if (_owner != null)
                UnityEngine.Object.DestroyImmediate(_owner);
        }

        private GameObject CreateOwner() => _owner = new GameObject("LoadingControllerLeakTests_Owner");

        private List<LeakSnapshot> CopyLeaks()
        {
            var leaks = new List<LeakSnapshot>();
            Leaks.CopyLeaksTo(leaks);
            return leaks;
        }

        private void CompleteWorker(AssetKey key) => Factory.GetWorker(key, ASSET_TYPE).Complete(ScriptableObject.CreateInstance<FakeAsset>());

        // Owner 破棄で回収された、返し済みのチケットが記録される（ログの警告も従来どおり出る）
        [Test]
        public async Task OwnerDestroyedDeliveredTicketIsRecorded()
        {
            var owner = CreateOwner();
            var key = AssetKey.FromPath("test/leak-owner");
            var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));
            CompleteWorker(key);
            await task;

            using var counter = new WarningCounter("リーク");
            WatcherBinder.SimulateOwnerDestroyed(owner);

            var leaks = CopyLeaks();
            Assert.AreEqual(1, leaks.Count);
            Assert.AreEqual(1, counter.AccCount, "ログの警告も従来どおり出るはず");
            var leak = leaks[0];
            Assert.IsFalse(leak.AccIsGroup);
            Assert.AreEqual(key, leak.AccKey);
            Assert.AreEqual(ASSET_TYPE, leak.AccAssetType);
            Assert.AreEqual(LifetimeKind.Owner, leak.AccScopeKind);
            Assert.AreEqual(owner.GetInstanceID(), leak.AccScopeValue);
            Assert.AreEqual(owner.name, leak.AccScopeName, "回収時に生きている Owner の GameObject 名を記録するはず");
            Assert.AreEqual(1, leak.AccCount);
            Assert.IsFalse(string.IsNullOrEmpty(leak.AccStackTrace), "確保元のスタックトレースを持つはず");
        }

        // Scene アンロードで回収された、返し済みのチケットが記録される
        [Test]
        public async Task SceneUnloadedDeliveredTicketIsRecorded()
        {
            var scene = SceneManager.GetActiveScene();
            var key = AssetKey.FromPath("test/leak-scene");
            var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Scene(scene));
            CompleteWorker(key);
            await task;

            Controller.HandleSceneUnloaded(scene);

            var leaks = CopyLeaks();
            Assert.AreEqual(1, leaks.Count);
            Assert.AreEqual(LifetimeKind.Scene, leaks[0].AccScopeKind);
            int scene_handle = scene.handle; // Unity 6000.3 の Scene.handle は int ではない型のため、int へ変換してから比べる（object 同士の比較は型違いで不一致になる）
            Assert.AreEqual(scene_handle, leaks[0].AccScopeValue);
            Assert.AreEqual(scene.name, leaks[0].AccScopeName, "アンロードされたシーン名を記録するはず");
        }

        // Owner が破棄済みで、ポーリングで見つかった場合は、名前なしで記録される
        [Test]
        public async Task OwnerDestroyedBeforePollingIsRecordedWithoutName()
        {
            var owner = CreateOwner();
            var key = AssetKey.FromPath("test/leak-polling");
            var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));
            CompleteWorker(key);
            await task;

            var instance_id = owner.GetInstanceID();
            UnityEngine.Object.DestroyImmediate(owner); // Watcher の通知を経ずに破棄された状態を作る
            Controller.Tick(10f); // ポーリング間隔（3秒）を超える

            var leaks = CopyLeaks();
            Assert.AreEqual(1, leaks.Count);
            Assert.AreEqual(instance_id, leaks[0].AccScopeValue);
            Assert.AreEqual(string.Empty, leaks[0].AccScopeName, "破棄済みの Owner は名前が取れないため、空文字のはず");
        }

        // 明示解放済みのチケットは、記録されない
        [Test]
        public async Task ExplicitlyReleasedTicketIsNotRecorded()
        {
            var scene = SceneManager.GetActiveScene();
            var key = AssetKey.FromPath("test/leak-explicit");
            var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Scene(scene));
            CompleteWorker(key);
            var ticket = await task;

            Controller.Release(ticket);
            Controller.HandleSceneUnloaded(scene);

            Assert.AreEqual(0, CopyLeaks().Count);
        }

        // ロード中（返す前）に寿命が終わった分は、リークではないため記録されない
        [Test]
        public async Task LifetimeEndsWhileLoadingIsNotRecorded()
        {
            var owner = CreateOwner();
            var key = AssetKey.FromPath("test/leak-loading");
            var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));

            WatcherBinder.SimulateOwnerDestroyed(owner); // ロード中に寿命が終わる
            CompleteWorker(key);
            await TestAsync.Catch<OperationCanceledException>(async () => await task);

            Assert.AreEqual(0, CopyLeaks().Count);
        }

        // 同じ呼び出し元の確保が複数あれば、1行にまとまり、回数が加算される
        [Test]
        public async Task SameCallSiteLeaksAreAggregatedIntoOneRow()
        {
            var owner = CreateOwner();
            var key = AssetKey.FromPath("test/leak-same-site");
            Func<Task> start = async () => await Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));

            // await を挟まずに同じ行から呼ぶ（スタックトレースが一致する）
            var tasks = new List<Task>();
            for (var i = 0; i < 3; i++) tasks.Add(start());
            CompleteWorker(key);
            await Task.WhenAll(tasks);

            WatcherBinder.SimulateOwnerDestroyed(owner);

            var leaks = CopyLeaks();
            Assert.AreEqual(1, leaks.Count);
            Assert.AreEqual(3, leaks[0].AccCount);
        }

        // 呼び出し元の行が違えば、別の行になる
        [Test]
        public async Task DifferentCallSitesAreSeparateRows()
        {
            var owner = CreateOwner();
            var key = AssetKey.FromPath("test/leak-two-sites");
            Func<Task> start = async () => await Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));

            var task1 = start();
            var task2 = start(); // 1つ目とは別の行から呼ぶ
            CompleteWorker(key);
            await task1;
            await task2;

            WatcherBinder.SimulateOwnerDestroyed(owner);

            Assert.AreEqual(2, CopyLeaks().Count);
        }

        // Label 一括は、まとめチケット1枚につき1行（1回）だけ記録される
        [Test]
        public async Task GroupLeakIsRecordedOncePerGroup()
        {
            var owner = CreateOwner();
            LabelLocations.SetLocations(LABEL, new LabelLocation("a", ASSET_TYPE), new LabelLocation("b", ASSET_TYPE), new LabelLocation("c", ASSET_TYPE));
            var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Owner(owner));
            CompleteWorker(AssetKey.FromPath("a"));
            CompleteWorker(AssetKey.FromPath("b"));
            CompleteWorker(AssetKey.FromPath("c"));
            await task;

            WatcherBinder.SimulateOwnerDestroyed(owner);

            var leaks = CopyLeaks();
            Assert.AreEqual(1, leaks.Count, "中身の件数分ではなく、まとめチケット1枚につき1行のはず");
            Assert.IsTrue(leaks[0].AccIsGroup);
            Assert.AreEqual(LABEL, leaks[0].AccLabel);
            Assert.AreEqual(3, leaks[0].AccGroupCount);
            Assert.AreEqual(1, leaks[0].AccCount);
            Assert.AreEqual(owner.name, leaks[0].AccScopeName);
            Assert.IsFalse(string.IsNullOrEmpty(leaks[0].AccStackTrace));
        }

        // Label 一括で、Scene アンロードで回収された場合は、シーン名が記録される
        [Test]
        public async Task GroupLeakBySceneUnloadedRecordsSceneName()
        {
            var scene = SceneManager.GetActiveScene();
            LabelLocations.SetLocations(LABEL, new LabelLocation("a", ASSET_TYPE));
            var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Scene(scene));
            CompleteWorker(AssetKey.FromPath("a"));
            await task;

            Controller.HandleSceneUnloaded(scene);

            var leaks = CopyLeaks();
            Assert.AreEqual(1, leaks.Count);
            Assert.AreEqual(LifetimeKind.Scene, leaks[0].AccScopeKind);
            Assert.AreEqual(scene.name, leaks[0].AccScopeName);
        }

        // Label 一括でも、明示解放済みと、ロード中に寿命が終わった分は、記録されない
        [Test]
        public async Task GroupExplicitlyReleasedOrLoadingIsNotRecorded()
        {
            var owner = CreateOwner();
            LabelLocations.SetLocations(LABEL, new LabelLocation("a", ASSET_TYPE));

            // 明示解放済み
            var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Owner(owner));
            CompleteWorker(AssetKey.FromPath("a"));
            var group = await task;
            Controller.Release(group);
            WatcherBinder.SimulateOwnerDestroyed(owner);
            Assert.AreEqual(0, CopyLeaks().Count, "明示解放済みは記録しないはず");

            // ロード中に寿命が終わる
            var loading_task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Owner(owner));
            WatcherBinder.SimulateOwnerDestroyed(owner);
            CompleteWorker(AssetKey.FromPath("a"));
            await TestAsync.Catch<OperationCanceledException>(async () => await loading_task);
            Assert.AreEqual(0, CopyLeaks().Count, "ロード中の回収は記録しないはず");
        }

        // 破棄すると、それまでに記録したリークも空になる（Model を使い終えた後に、古い記録を残さない）
        [Test]
        public async Task DisposeClearsRecordedLeaks()
        {
            var owner = CreateOwner();
            var key = AssetKey.FromPath("test/leak-dispose-clear");
            var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));
            CompleteWorker(key);
            await task;
            using (new WarningCounter("リーク"))
                WatcherBinder.SimulateOwnerDestroyed(owner);
            Assert.AreEqual(1, CopyLeaks().Count, "前提：リークが1件記録されているはず");

            Controller.Dispose();

            Assert.AreEqual(0, CopyLeaks().Count);
        }

        // Manager の破棄（最終防衛線の強制解放）は、リークとして記録しない
        [Test]
        public async Task DisposeDoesNotRecordLeaks()
        {
            var scene = SceneManager.GetActiveScene();
            var key = AssetKey.FromPath("test/leak-dispose");
            var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Scene(scene));
            CompleteWorker(key);
            await task;

            Controller.Dispose();

            Assert.AreEqual(0, CopyLeaks().Count);
        }
    }
}
