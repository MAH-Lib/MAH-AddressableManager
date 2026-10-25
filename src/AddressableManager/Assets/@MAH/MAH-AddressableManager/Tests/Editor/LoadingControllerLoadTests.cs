using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class LoadingControllerLoadTests : LoadingControllerTestBase
    {
        // 同じキーを2回Loadすると、実ロードは1回だけでRefCountが2になる
        [Test]
        public async Task LoadAsyncSameKeyTwiceLoadsOnceAndRefCountBecomesTwo()
        {
            var key = AssetKey.FromPath("test/duplicate");

            var task1 = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);
            var task2 = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);

            var asset = ScriptableObject.CreateInstance<FakeAsset>();
            Factory.GetWorker(key).Complete(asset);

            var ticket1 = await task1;
            var ticket2 = await task2;

            Assert.AreEqual(1, Factory.CreateCallCount, "実ロードは1回だけのはず");
            Assert.AreSame(asset, ticket1.AccAsset);
            Assert.AreSame(asset, ticket2.AccAsset);

            Controller.Release(ticket1);
            Controller.Release(ticket2);
        }

        // 同じキーを違う型でLoadすると、別のエントリとして読み込まれる（例外にならない）
        [Test]
        public async Task LoadAsyncSameKeyDifferentTypeLoadsAsSeparateEntries()
        {
            var key = AssetKey.FromPath("test/type-different");
            var task1 = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);
            var task2 = Controller.LoadAsync<GameObject>(key, AssetLifetime.Manual);

            Assert.AreEqual(2, Factory.CreateCallCount, "型が違えば別のエントリなので、実ロードはそれぞれ1回ずつのはず");

            var asset = ScriptableObject.CreateInstance<FakeAsset>();
            var game_object = new GameObject("test/type-different");
            var worker1 = Factory.GetWorker(key, typeof(FakeAsset));
            var worker2 = Factory.GetWorker(key, typeof(GameObject));
            worker1.Complete(asset);
            worker2.Complete(game_object);

            var ticket1 = await task1;
            var ticket2 = await task2;
            Assert.AreSame(asset, ticket1.AccAsset);
            Assert.AreSame(game_object, ticket2.AccAsset);

            // 片方を解放しても、もう片方は残る
            Controller.Release(ticket1);
            Assert.AreEqual(1, worker1.DisposeCount, "FakeAsset側は解放されるはず");
            Assert.AreEqual(0, worker2.DisposeCount, "GameObject側はまだ解放されないはず");

            Controller.Release(ticket2);
            Assert.AreEqual(1, worker2.DisposeCount);

            UnityEngine.Object.DestroyImmediate(game_object);
        }

        // 失敗を受け取ってリトライする呼び出し元がいても、他の待機者の巻き戻しが、
        // リトライで新しく作られたエントリの RefCount を減らさない（Worker が早期に Dispose されない）
        [Test]
        public async Task LoadAsyncRetryAfterFailureDoesNotDisposeNewWorkerPrematurely()
        {
            var key = AssetKey.FromPath("test/retry-after-failure");
            var retrying = LoadRetryingOnce(key); // 失敗したら、同じキーでもう一度 Load する呼び出し元
            var waiting = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual); // 同じエントリを待つ別の呼び出し元
            var first_worker = Factory.GetWorker(key);

            first_worker.Fail(new InvalidOperationException("fake failure"));
            try { await waiting; } catch (InvalidOperationException) { }

            // リトライによって、新しいエントリ（新しい Worker）が作られるまで待つ
            await TestAsync.WaitUntil(() => Factory.CreateCallCount == 2);
            var second_worker = Factory.GetWorker(key);
            Assert.AreNotSame(first_worker, second_worker);
            Assert.AreEqual(1, first_worker.DisposeCount, "失敗したエントリのWorkerは1回だけ解放されるはず");

            second_worker.Complete(ScriptableObject.CreateInstance<FakeAsset>());
            var ticket = await retrying;
            Assert.AreEqual(0, second_worker.DisposeCount, "リトライで読み込んだ分は、まだ解放されないはず");

            Controller.Release(ticket);
            Assert.AreEqual(1, second_worker.DisposeCount, "明示解放でRefCountが0になり、解放されるはず");
        }

        // 失敗を受け取ったら1回だけ、同じキーでもう一度Loadする呼び出し元
        private async Task<AssetTicket<FakeAsset>> LoadRetryingOnce(AssetKey key)
        {
            try
            {
                return await Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);
            }
            catch (InvalidOperationException)
            {
                return await Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);
            }
        }

        // 明示解放を2回すると、2回目のReleaseでWorkerがDisposeされる
        [Test]
        public async Task ReleaseExplicitReleaseTwiceDisposesWorkerOnSecondRelease()
        {
            var key = AssetKey.FromPath("test/explicit-release");
            var task1 = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);
            var task2 = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);
            Factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            var ticket1 = await task1;
            var ticket2 = await task2;

            Controller.Release(ticket1);
            Assert.AreEqual(0, Factory.GetWorker(key).DisposeCount, "1回目のReleaseではまだ解放されないはず");

            Controller.Release(ticket2);
            Assert.AreEqual(1, Factory.GetWorker(key).DisposeCount, "2回目のReleaseでRefCountが0になり解放されるはず");
        }

        // 同じチケットを2回Releaseしても、RefCountは1しか減らない
        [Test]
        public async Task ReleaseSameTicketTwiceDecrementsOnlyOnce()
        {
            var key = AssetKey.FromPath("test/double-release");
            var task1 = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);
            var task2 = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);
            Factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            var ticket1 = await task1;
            await task2; // RefCount = 2 のままにしておく

            Controller.Release(ticket1);
            Controller.Release(ticket1); // 同じチケットの2回目。警告のみで例外にはならないはず

            Assert.AreEqual(0, Factory.GetWorker(key).DisposeCount, "2回目のReleaseは無視され、RefCountはまだ1残っているはず");
        }

        // defaultのチケットは無視される
        [Test]
        public void ReleaseDefaultTicketIgnored()
        {
            Assert.DoesNotThrow(() => Controller.Release(default(AssetTicket<FakeAsset>)));
        }

        // ロード中にキャンセルすると、呼び出し元だけ待機が止まりRefCountが戻る
        [Test]
        public async Task LoadAsyncCancelWhileLoadingStopsCallerAndRestoresRefCount()
        {
            var key = AssetKey.FromPath("test/cancel");
            using var cts = new CancellationTokenSource();

            var task = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual, cts.Token);
            cts.Cancel();

            OperationCanceledException caught = null;
            try
            {
                await task;
            }
            catch (OperationCanceledException ex)
            {
                caught = ex;
            }
            Assert.IsNotNull(caught);

            var worker = Factory.GetWorker(key);
            Assert.AreEqual(0, worker.DisposeCount, "実ロードは止まっていないため、まだDisposeされないはず");

            // 実ロードは後から完了する。完了後にDisposeされるはず（RefCountは既に0）
            worker.Complete(ScriptableObject.CreateInstance<FakeAsset>());
            await TestAsync.WaitUntil(() => worker.DisposeCount == 1);
        }

        // 失敗すると、待機中の全員に同じ例外が届きエントリが消える
        [Test]
        public async Task LoadAsyncFailurePropagatesToAllWaitersAndRemovesEntry()
        {
            var key = AssetKey.FromPath("test/failure");
            var task1 = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);
            var task2 = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);

            var exception = new InvalidOperationException("fake failure");
            Factory.GetWorker(key).Fail(exception);

            Exception caught1 = null;
            Exception caught2 = null;
            try { await task1; } catch (Exception ex) { caught1 = ex; }
            try { await task2; } catch (Exception ex) { caught2 = ex; }

            Assert.AreSame(exception, caught1);
            Assert.AreSame(exception, caught2);

            // エントリが即削除されているため、次のLoadで新しくWorkerが作られるはず
            var before = Factory.CreateCallCount;
            var task3 = Controller.LoadAsync<FakeAsset>(key, AssetLifetime.Manual);
            Assert.AreEqual(before + 1, Factory.CreateCallCount);

            Factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            var ticket3 = await task3;
            Controller.Release(ticket3);
        }
    }
}
