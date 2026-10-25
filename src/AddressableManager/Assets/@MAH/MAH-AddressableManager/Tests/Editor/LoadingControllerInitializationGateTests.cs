using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// 初期化ゲート（IInitializationGate）が閉じているときの、LoadAsync / LoadByLabelAsync の動作（initialize-spec 5章）。
    /// 偽のゲートを使い、閉じている間は RefCount・チケット・Watcher・実ロードのどれも発生しないことを確認する。
    /// </summary>
    internal sealed class LoadingControllerInitializationGateTests : LoadingControllerTestBase
    {
        private const string LABEL = "gate-label";
        private static readonly Type ASSET_TYPE = typeof(FakeAsset);

        private static AssetKey Key(string address) => AssetKey.FromPath(address);

        private static AddressableInitializeException InitializeFailure() => new AddressableInitializeException("初期化に失敗しました", null);

        // ---- ゲートが開いているとき ----

        // 開いているときは、ゲートに待機を求めない（await も確保も追加で起きない）
        [Test]
        public async Task LoadAsyncWithOpenGateDoesNotWait()
        {
            var key = Key("gate/open");

            var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);
            Factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            var ticket = await task;

            Assert.AreEqual(0, Gate.WaitCallCount);
            Controller.Release(ticket);
        }

        [Test]
        public async Task LoadByLabelAsyncWithOpenGateDoesNotWait()
        {
            LabelLocations.SetLocations(LABEL, new LabelLocation("gate/label-a", ASSET_TYPE));

            var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual);
            Factory.GetWorker(Key("gate/label-a"), ASSET_TYPE).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            var group = await task;

            Assert.AreEqual(0, Gate.WaitCallCount);
            Controller.Release(group);
        }

        // ---- 閉じているとき：待ってから読み込む ----

        // 閉じている間は何も確保せず、開いたら続きから読み込む
        [Test]
        public async Task LoadAsyncWithClosedGateWaitsThenLoads()
        {
            var key = Key("gate/wait-load");
            Gate.Close();

            var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);

            Assert.AreEqual(1, Gate.WaitCallCount);
            Assert.AreEqual(0, Factory.CreateCallCount, "待っている間は、実ロードを始めないはず");

            Gate.Open();
            await TestAsync.WaitUntil(() => Factory.CreateCallCount == 1);
            var asset = ScriptableObject.CreateInstance<FakeAsset>();
            Factory.GetWorker(key).Complete(asset);
            var ticket = await task;

            Assert.AreSame(asset, ticket.AccAsset);
            Controller.Release(ticket);
        }

        [Test]
        public async Task LoadByLabelAsyncWithClosedGateWaitsBeforeResolvingThenLoads()
        {
            LabelLocations.SetLocations(LABEL, new LabelLocation("gate/label-wait", ASSET_TYPE));
            Gate.Close();

            var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual);

            Assert.AreEqual(1, Gate.WaitCallCount);
            Assert.AreEqual(0, LabelLocations.ResolveCallCount, "待っている間は、ロケーションを解決しないはず");

            Gate.Open();
            await TestAsync.WaitUntil(() => Factory.CreateCallCount == 1);
            Factory.GetWorker(Key("gate/label-wait"), ASSET_TYPE).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            var group = await task;

            Assert.AreEqual(1, group.AccCount);
            Controller.Release(group);
        }

        // ---- 閉じているとき：初期化の失敗 ----

        // 初期化の失敗は、そのまま届く。何も確保されず、Owner の Watcher も登録されない
        [Test]
        public async Task LoadAsyncWithFailedInitializationThrowsInitializeExceptionAndLeavesNothing()
        {
            var owner = new GameObject("GateOwner_Failed");
            try
            {
                Gate.Close();
                var failure = InitializeFailure();
                var load = TestAsync.Catch<AddressableInitializeException>(async () => await Controller.LoadAsync<FakeAsset>(Key("gate/failed"), AssetLifetime.Owner(owner)));

                Gate.Fail(failure);
                var caught = await load;

                Assert.AreSame(failure, caught);
                Assert.AreEqual(0, Factory.CreateCallCount);
                Assert.AreEqual(0, WatcherBinder.BindCallCount, "Owner の Watcher は登録されないはず");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public async Task LoadByLabelAsyncWithFailedInitializationThrowsInitializeExceptionAndLeavesNothing()
        {
            LabelLocations.SetLocations(LABEL, new LabelLocation("gate/label-failed", ASSET_TYPE));
            Gate.Close();
            var failure = InitializeFailure();
            var load = TestAsync.Catch<AddressableInitializeException>(async () => await Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual));

            Gate.Fail(failure);
            var caught = await load;

            Assert.AreSame(failure, caught);
            Assert.AreEqual(0, LabelLocations.ResolveCallCount, "ロケーションの解決まで進まないはず");
            Assert.AreEqual(0, Factory.CreateCallCount);
        }

        // 未開始（WaitForInitialize で InitializeAsync の呼び忘れ）の InvalidOperationException も、そのまま届く
        [Test]
        public async Task LoadAsyncWithNotStartedInitializationThrowsInvalidOperation()
        {
            Gate.Close();
            var load = TestAsync.Catch<InvalidOperationException>(async () => await Controller.LoadAsync<FakeAsset>(Key("gate/not-started"), AssetLifetime.Manual));

            Gate.Fail(new InvalidOperationException("初期化が始まっていません"));
            await load;

            Assert.AreEqual(0, Factory.CreateCallCount);
        }

        // ---- 閉じているとき：キャンセル・破棄・寿命の終了 ----

        // 待機中の ct のキャンセルは OperationCanceledException。何も確保されない
        [Test]
        public async Task LoadAsyncCancelledWhileWaitingThrowsOperationCanceled()
        {
            using var cts = new CancellationTokenSource();
            Gate.Close();
            var load = TestAsync.Catch<OperationCanceledException>(async () => await Controller.LoadAsync<FakeAsset>(Key("gate/cancel"), AssetLifetime.Manual, cts.Token));

            cts.Cancel();
            await load;

            Assert.AreEqual(0, Factory.CreateCallCount);
        }

        [Test]
        public async Task LoadByLabelAsyncCancelledWhileWaitingThrowsOperationCanceled()
        {
            LabelLocations.SetLocations(LABEL, new LabelLocation("gate/label-cancel", ASSET_TYPE));
            using var cts = new CancellationTokenSource();
            Gate.Close();
            var load = TestAsync.Catch<OperationCanceledException>(async () => await Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual, cts.Token));

            cts.Cancel();
            await load;

            Assert.AreEqual(0, LabelLocations.ResolveCallCount);
            Assert.AreEqual(0, Factory.CreateCallCount);
        }

        // 待機中に Manager が破棄されると、ゲートの待機は OperationCanceledException で終わり、Load 側が ObjectDisposedException に変換する
        [Test]
        public async Task LoadAsyncDisposedWhileWaitingThrowsObjectDisposed()
        {
            Gate.Close();
            var load = TestAsync.Catch<ObjectDisposedException>(async () => await Controller.LoadAsync<FakeAsset>(Key("gate/dispose"), AssetLifetime.Manual));

            Controller.Dispose(); // Manager は、Loading を先に Dispose してから、Initialize を Dispose する
            Gate.Cancel();
            await load;

            Assert.AreEqual(0, Factory.CreateCallCount);
        }

        [Test]
        public async Task LoadByLabelAsyncDisposedWhileWaitingThrowsObjectDisposed()
        {
            LabelLocations.SetLocations(LABEL, new LabelLocation("gate/label-dispose", ASSET_TYPE));
            Gate.Close();
            var load = TestAsync.Catch<ObjectDisposedException>(async () => await Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual));

            Controller.Dispose();
            Gate.Cancel();
            await load;

            Assert.AreEqual(0, LabelLocations.ResolveCallCount);
        }

        // ct がキャンセル済みでもあるときは、ct のキャンセル（OperationCanceledException）を優先する
        [Test]
        public async Task LoadAsyncCancelledAndDisposedWhileWaitingPrefersOperationCanceled()
        {
            using var cts = new CancellationTokenSource();
            Gate.Close();
            var load = TestAsync.Catch<OperationCanceledException>(async () => await Controller.LoadAsync<FakeAsset>(Key("gate/both"), AssetLifetime.Manual, cts.Token));

            cts.Cancel();
            Controller.Dispose();
            await load;
        }

        // 待っている間に owner が破棄されたら、引数の誤り（ArgumentException）ではなく OperationCanceledException。何も確保されない
        [Test]
        public async Task LoadAsyncOwnerDestroyedWhileWaitingThrowsOperationCanceled()
        {
            var owner = new GameObject("GateOwner_DestroyedWhileWaiting");
            try
            {
                Gate.Close();
                var load = TestAsync.Catch<OperationCanceledException>(async () => await Controller.LoadAsync<FakeAsset>(Key("gate/owner-destroyed"), AssetLifetime.Owner(owner)));

                UnityEngine.Object.DestroyImmediate(owner);
                Gate.Open();
                await load;

                Assert.AreEqual(0, Factory.CreateCallCount);
                Assert.AreEqual(0, WatcherBinder.BindCallCount);
            }
            finally
            {
                if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public async Task LoadByLabelAsyncOwnerDestroyedWhileWaitingThrowsOperationCanceled()
        {
            var owner = new GameObject("GateOwner_LabelDestroyedWhileWaiting");
            try
            {
                LabelLocations.SetLocations(LABEL, new LabelLocation("gate/label-owner-destroyed", ASSET_TYPE));
                Gate.Close();
                var load = TestAsync.Catch<OperationCanceledException>(async () => await Controller.LoadByLabelAsync(LABEL, AssetLifetime.Owner(owner)));

                UnityEngine.Object.DestroyImmediate(owner);
                Gate.Open();
                await load;

                Assert.AreEqual(0, Factory.CreateCallCount);
                Assert.AreEqual(0, WatcherBinder.BindCallCount);
            }
            finally
            {
                if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        // 待機の前から owner が破棄されていた場合は、これまでどおり ArgumentException
        [Test]
        public async Task LoadAsyncOwnerAlreadyDestroyedBeforeWaitingThrowsArgumentException()
        {
            var owner = new GameObject("GateOwner_AlreadyDestroyed");
            var lifetime = AssetLifetime.Owner(owner);
            UnityEngine.Object.DestroyImmediate(owner);
            Gate.Close();
            var load = TestAsync.Catch<ArgumentException>(async () => await Controller.LoadAsync<FakeAsset>(Key("gate/owner-already-destroyed"), lifetime));

            Gate.Open();
            await load;

            Assert.AreEqual(0, Factory.CreateCallCount);
        }

        // ---- 解放は止めない ----

        // 初期化に失敗した後でも、失敗の前に受け取ったチケットは解放できる
        [Test]
        public async Task ReleaseStillWorksAfterGateCloses()
        {
            var key = Key("gate/release-after-close");
            var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);
            var worker = Factory.GetWorker(key);
            worker.Complete(ScriptableObject.CreateInstance<FakeAsset>());
            var ticket = await task;

            Gate.Close();
            Controller.Release(ticket);

            Assert.AreEqual(1, worker.DisposeCount, "ゲートが閉じていても、解放はできるはず");
        }
    }
}
