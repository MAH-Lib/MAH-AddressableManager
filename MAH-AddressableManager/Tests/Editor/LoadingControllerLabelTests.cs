using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// Label 一括ロード（LoadByLabelAsync / Release(AssetGroupTicket)）の Controller のテスト。
    /// 偽の ILabelLocationService と偽の Worker を使い、実際の Addressables は呼ばない。
    /// 型が混ざった Label は、同じアドレスの FakeAsset と FakeOtherAsset（画像の Texture2D と Sprite に相当）、別アドレスの FakeAsset で表す。
    /// </summary>
    internal sealed class LoadingControllerLabelTests : LoadingControllerTestBase
    {
        private const string LABEL = "test-label";
        private static readonly Type ASSET_TYPE = typeof(FakeAsset);
        private static readonly Type OTHER_TYPE = typeof(FakeOtherAsset);

        private static LabelLocation Location(string address, Type type) => new LabelLocation(address, type);

        private static AssetKey Key(string address) => AssetKey.FromPath(address);

        private FakeAssetLoaderWorker Worker(string address, Type type) => Factory.GetWorker(Key(address), type);

        /// <summary> 指定の Worker のロードを成功させ、読み込んだアセットを返す </summary>
        private UnityEngine.Object CompleteWorker(string address, Type type)
        {
            var asset = ScriptableObject.CreateInstance(type);
            Worker(address, type).Complete(asset);
            return asset;
        }

        // 型が混ざった Label：a（FakeAsset と FakeOtherAsset の2件）・b（FakeAsset）
        private void SetUpMixedLabel()
        {
            LabelLocations.SetLocations(LABEL, Location("a", ASSET_TYPE), Location("a", OTHER_TYPE), Location("b", ASSET_TYPE));
        }

        // ----- 読み込みと取り出し -----

        // 型が混ざった Label でも、全件が読み込まれ、型ごとに取り出せる
        [Test]
        public async Task LoadByLabelAsyncMixedTypesLoadsAllAndGetsByExactType()
        {
            SetUpMixedLabel();

            var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual);
            Assert.AreEqual(3, Factory.CreateCallCount, "(アドレス, 型) の組ごとに、実ロードが1回ずつ起動するはず");
            var asset_a = CompleteWorker("a", ASSET_TYPE);
            var other_a = CompleteWorker("a", OTHER_TYPE);
            var asset_b = CompleteWorker("b", ASSET_TYPE);
            var group = await task;

            Assert.AreEqual(LABEL, group.AccLabel);
            Assert.AreEqual(3, group.AccCount);
            Assert.IsFalse(group.AccIsReleased);

            Assert.IsTrue(group.TryGetAsset<FakeAsset>("a", out var found_asset_a));
            Assert.AreSame(asset_a, found_asset_a);
            Assert.IsTrue(group.TryGetAsset<FakeOtherAsset>("a", out var found_other_a));
            Assert.AreSame(other_a, found_other_a);
            Assert.IsFalse(group.TryGetAsset<FakeOtherAsset>("b", out var missing), "b に FakeOtherAsset は無いので false のはず");
            Assert.IsNull(missing);

            CollectionAssert.AreEquivalent(new[] { asset_a, asset_b }, group.GetAssets<FakeAsset>());
            Assert.AreEqual(1, group.GetAssets<FakeOtherAsset>().Count);
            Assert.AreEqual(0, group.GetAssets<UnityEngine.Object>().Count, "基底型では探さないので、空のはず");

            Controller.Release(group);
        }

        // 該当0件の Label は、警告を出して、空のまとめチケットを返す（例外にならない）
        [Test]
        public async Task LoadByLabelAsyncNoLocationsReturnsEmptyGroupWithWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("該当するアセットがありません"));

            var group = await Controller.LoadByLabelAsync("no-such-label", AssetLifetime.Manual);

            Assert.AreEqual(0, group.AccCount);
            Assert.IsFalse(group.AccIsReleased);
            Assert.AreEqual(0, group.GetAssets<FakeAsset>().Count);
            Assert.AreEqual(0, Factory.CreateCallCount, "実ロードは起動しないはず");

            Controller.Release(group); // 空のまとめチケットも解放できる（警告なし・例外なし）
            Assert.IsTrue(group.AccIsReleased);
        }

        // Label が null または空なら例外
        [Test]
        public async Task LoadByLabelAsyncNullOrEmptyLabelThrowsArgumentException()
        {
            foreach (var label in new string[] { null, string.Empty })
            {
                ArgumentException caught = null;
                try
                {
                    await Controller.LoadByLabelAsync(label, AssetLifetime.Manual);
                }
                catch (ArgumentException ex)
                {
                    caught = ex;
                }
                Assert.IsNotNull(caught, $"Label が「{label ?? "null"}」のときは ArgumentException になるはず");
            }
            Assert.AreEqual(0, LabelLocations.ResolveCallCount, "ロケーションの解決には進まないはず");
        }

        // 単体の Load と、同じアドレス・同じ型ならエントリを共有し、型が違えば別のエントリになる
        [Test]
        public async Task LoadByLabelAsyncSharesEntryWithSingleLoadForSameAddressAndType()
        {
            var single_task = Controller.LoadAsync<FakeAsset>(Key("a"), AssetLifetime.Manual);
            LabelLocations.SetLocations(LABEL, Location("a", ASSET_TYPE), Location("a", OTHER_TYPE));

            var group_task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual);

            Assert.AreEqual(2, Factory.CreateCallCount, "a の FakeAsset は共有され、FakeOtherAsset だけが新しく起動するはず");
            var asset_a = CompleteWorker("a", ASSET_TYPE);
            CompleteWorker("a", OTHER_TYPE);
            var single_ticket = await single_task;
            var group = await group_task;
            Assert.AreSame(asset_a, single_ticket.AccAsset);
            Assert.IsTrue(group.TryGetAsset<FakeAsset>("a", out var shared));
            Assert.AreSame(asset_a, shared, "同じエントリなので、同じアセットのはず");

            // 単体の Load を解放しても、まとめチケットが保持している間は解放されない
            Controller.Release(single_ticket);
            Assert.AreEqual(0, Worker("a", ASSET_TYPE).DisposeCount);
            Controller.Release(group);
            Assert.AreEqual(1, Worker("a", ASSET_TYPE).DisposeCount);
            Assert.AreEqual(1, Worker("a", OTHER_TYPE).DisposeCount);
        }

        // ----- 解放 -----

        // まとめチケットを解放すると、全件の Worker が解放され、まとめチケットは使えなくなる
        [Test]
        public async Task ReleaseGroupDisposesAllWorkersAndInvalidatesGroup()
        {
            SetUpMixedLabel();
            var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual);
            CompleteWorker("a", ASSET_TYPE);
            CompleteWorker("a", OTHER_TYPE);
            CompleteWorker("b", ASSET_TYPE);
            var group = await task;

            Controller.Release(group);

            Assert.AreEqual(1, Worker("a", ASSET_TYPE).DisposeCount);
            Assert.AreEqual(1, Worker("a", OTHER_TYPE).DisposeCount);
            Assert.AreEqual(1, Worker("b", ASSET_TYPE).DisposeCount);
            Assert.IsTrue(group.AccIsReleased);
            Assert.AreEqual(0, group.GetAssets<FakeAsset>().Count, "解放後は何も取り出せないはず");
            Assert.IsFalse(group.TryGetAsset<FakeAsset>("a", out _));
        }

        // 同じまとめチケットの2回目の解放と、null の解放は、警告だけで、他の確保分を減らさない
        [Test]
        public async Task ReleaseGroupTwiceAndNullOnlyWarnAndKeepOtherReferences()
        {
            var single_task = Controller.LoadAsync<FakeAsset>(Key("a"), AssetLifetime.Manual); // 同じエントリを、別に確保しておく
            LabelLocations.SetLocations(LABEL, Location("a", ASSET_TYPE));
            var group_task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual);
            CompleteWorker("a", ASSET_TYPE);
            var single_ticket = await single_task;
            var group = await group_task;

            Controller.Release(group);
            LogAssert.Expect(LogType.Warning, new Regex("解放済みまたは無効なまとめチケット"));
            Controller.Release(group); // 2回目
            LogAssert.Expect(LogType.Warning, new Regex("解放済みまたは無効なまとめチケット"));
            Controller.Release((AssetGroupTicket)null);

            Assert.AreEqual(0, Worker("a", ASSET_TYPE).DisposeCount, "単体の確保分が残っているので、まだ解放されないはず");
            Controller.Release(single_ticket);
            Assert.AreEqual(1, Worker("a", ASSET_TYPE).DisposeCount);
        }

        // 同じ Label への並行した LoadByLabelAsync は、別々のまとめチケットになり、独立して解放できる
        [Test]
        public async Task LoadByLabelAsyncConcurrentSameLabelGroupsAreIndependent()
        {
            LabelLocations.SetLocations(LABEL, Location("a", ASSET_TYPE), Location("b", ASSET_TYPE));

            var task1 = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual);
            var task2 = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual);
            Assert.AreEqual(2, Factory.CreateCallCount, "同じエントリを共有するので、実ロードは件数分だけのはず");
            CompleteWorker("a", ASSET_TYPE);
            CompleteWorker("b", ASSET_TYPE);
            var group1 = await task1;
            var group2 = await task2;
            Assert.AreNotSame(group1, group2);
            Assert.AreEqual(2, group1.GetAssets<FakeAsset>().Count, "ロケーションのバッファが混ざらず、どちらも2件のはず");
            Assert.AreEqual(2, group2.GetAssets<FakeAsset>().Count);

            Controller.Release(group1);
            Assert.AreEqual(0, Worker("a", ASSET_TYPE).DisposeCount, "もう一方が保持しているので、解放されないはず");
            Assert.IsFalse(group2.AccIsReleased);
            Controller.Release(group2);
            Assert.AreEqual(1, Worker("a", ASSET_TYPE).DisposeCount);
            Assert.AreEqual(1, Worker("b", ASSET_TYPE).DisposeCount);
        }

        // ----- 寿命による自動回収 -----

        // Owner 寿命の Label ロードは、Watcher の登録が1回だけで、全件の解放後に登録が外れる
        [Test]
        public async Task LoadByLabelAsyncOwnerLifetimeBindsOnceAndUnbindsAfterRelease()
        {
            var owner = new GameObject("LoadingControllerLabelTests_Owner");
            try
            {
                SetUpMixedLabel();
                var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Owner(owner));
                Assert.AreEqual(1, WatcherBinder.BindCallCount, "件数に関わらず、Bind は1回だけのはず");
                CompleteWorker("a", ASSET_TYPE);
                CompleteWorker("a", OTHER_TYPE);
                CompleteWorker("b", ASSET_TYPE);
                var group = await task;
                Assert.AreEqual(1, WatcherBinder.BoundCount);

                Controller.Release(group);

                Assert.AreEqual(1, WatcherBinder.UnbindCallCount, "全件が解放されたら、Unbind されるはず");
                Assert.AreEqual(0, WatcherBinder.BoundCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        // Owner が破棄されると、全件がまとめて回収され、リーク警告は1件だけ。まとめチケットは使えなくなる
        [Test]
        public async Task OwnerDestroyedReclaimsWholeGroupWithSingleLeakWarning()
        {
            var owner = new GameObject("LoadingControllerLabelTests_Owner");
            try
            {
                SetUpMixedLabel();
                var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Owner(owner));
                CompleteWorker("a", ASSET_TYPE);
                CompleteWorker("a", OTHER_TYPE);
                CompleteWorker("b", ASSET_TYPE);
                var group = await task;

                using var counter = new WarningCounter("まとめチケット");
                WatcherBinder.SimulateOwnerDestroyed(owner);

                Assert.AreEqual(1, counter.AccCount, "件数分ではなく、まとめチケット1枚につき1件だけ警告するはず");
                Assert.AreEqual(1, Worker("a", ASSET_TYPE).DisposeCount);
                Assert.AreEqual(1, Worker("a", OTHER_TYPE).DisposeCount);
                Assert.AreEqual(1, Worker("b", ASSET_TYPE).DisposeCount);
                Assert.IsTrue(group.AccIsReleased);
                Assert.AreEqual(0, group.GetAssets<FakeAsset>().Count, "回収後のまとめチケットは、何も取り出せないはず");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        // Scene がアンロードされると、全件がまとめて回収される
        [Test]
        public async Task HandleSceneUnloadedReclaimsWholeGroup()
        {
            var scene = SceneManager.GetActiveScene();
            SetUpMixedLabel();
            var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Scene(scene));
            CompleteWorker("a", ASSET_TYPE);
            CompleteWorker("a", OTHER_TYPE);
            CompleteWorker("b", ASSET_TYPE);
            var group = await task;

            using var counter = new WarningCounter("まとめチケット");
            Controller.HandleSceneUnloaded(scene);

            Assert.AreEqual(1, counter.AccCount);
            Assert.AreEqual(1, Worker("a", ASSET_TYPE).DisposeCount);
            Assert.AreEqual(1, Worker("a", OTHER_TYPE).DisposeCount);
            Assert.AreEqual(1, Worker("b", ASSET_TYPE).DisposeCount);
            Assert.IsTrue(group.AccIsReleased);
        }

        // 寿命による回収の後に Release(group) しても、警告だけで、二重に解放されない
        [Test]
        public async Task ReleaseGroupAfterLifetimeReclaimOnlyWarns()
        {
            var scene = SceneManager.GetActiveScene();
            LabelLocations.SetLocations(LABEL, Location("a", ASSET_TYPE));
            var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Scene(scene));
            CompleteWorker("a", ASSET_TYPE);
            var group = await task;
            Controller.HandleSceneUnloaded(scene);
            Assert.AreEqual(1, Worker("a", ASSET_TYPE).DisposeCount);

            LogAssert.Expect(LogType.Warning, new Regex("解放済みまたは無効なまとめチケット"));
            Assert.DoesNotThrow(() => Controller.Release(group));

            Assert.AreEqual(1, Worker("a", ASSET_TYPE).DisposeCount, "二重に解放されないはず");
        }

        // ----- 異常系 -----

        // 1件でも失敗すると、残りのロードを待たずに全件が巻き戻され、最初の例外がそのまま届く
        [Test]
        public async Task LoadByLabelAsyncOneFailureRollsBackAllWithoutWaitingForOthers()
        {
            LabelLocations.SetLocations(LABEL, Location("a", ASSET_TYPE), Location("b", ASSET_TYPE));
            var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual);

            var exception = new InvalidOperationException("fake failure");
            Worker("a", ASSET_TYPE).Fail(exception); // b は、まだロード中のまま

            Exception caught = null;
            try { await task; } catch (Exception ex) { caught = ex; }

            Assert.AreSame(exception, caught, "最初の失敗が、そのまま届くはず（b の完了は待たない）");
            Assert.AreEqual(1, Worker("a", ASSET_TYPE).DisposeCount, "失敗したエントリの Worker は、すぐ解放されるはず");
            Assert.AreEqual(0, Worker("b", ASSET_TYPE).DisposeCount, "b はロード中のため、完了までは解放されないはず");

            // 全件が巻き戻されて RefCount が 0 に戻っているので、b の Worker は、ロード完了後に解放される
            CompleteWorker("b", ASSET_TYPE);
            await TestAsync.WaitUntil(() => Worker("b", ASSET_TYPE).DisposeCount == 1);

            // エントリが残っていないので、次の Load では新しく実ロードが始まる
            var before = Factory.CreateCallCount;
            var retry_task = Controller.LoadAsync<FakeAsset>(Key("b"), AssetLifetime.Manual);
            Assert.AreEqual(before + 1, Factory.CreateCallCount);
            CompleteWorker("b", ASSET_TYPE);
            Controller.Release(await retry_task);
        }

        // ct をキャンセルすると、全件が巻き戻され、OperationCanceledException が届く
        [Test]
        public async Task LoadByLabelAsyncCancelRollsBackAll()
        {
            LabelLocations.SetLocations(LABEL, Location("a", ASSET_TYPE), Location("b", ASSET_TYPE));
            using var cts = new CancellationTokenSource();
            var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual, cts.Token);

            cts.Cancel();

            OperationCanceledException caught = null;
            try { await task; } catch (OperationCanceledException ex) { caught = ex; }
            Assert.IsNotNull(caught);
            Assert.AreEqual(0, Worker("a", ASSET_TYPE).DisposeCount, "実ロードは止まっていないため、まだ解放されないはず");

            // 実ロードは後から完了する。RefCount は既に 0 なので、完了後に解放される
            CompleteWorker("a", ASSET_TYPE);
            CompleteWorker("b", ASSET_TYPE);
            await TestAsync.WaitUntil(() => Worker("a", ASSET_TYPE).DisposeCount == 1 && Worker("b", ASSET_TYPE).DisposeCount == 1);
        }

        // ロケーションの解決中にキャンセルすると、OperationCanceledException になり、何も確保しない
        [Test]
        public async Task LoadByLabelAsyncCancelDuringResolveThrowsAndReservesNothing()
        {
            LabelLocations.SetLocations(LABEL, Location("a", ASSET_TYPE));
            LabelLocations.HoldResolve();
            using var cts = new CancellationTokenSource();
            var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual, cts.Token);

            cts.Cancel();

            OperationCanceledException caught = null;
            try { await task; } catch (OperationCanceledException ex) { caught = ex; }
            Assert.IsNotNull(caught);
            Assert.AreEqual(0, Factory.CreateCallCount, "ロケーションの解決が終わる前なので、実ロードは起動しないはず");
        }

        // キャンセルした後に、ロードが失敗しても、OperationCanceledException が届く（キャンセルが優先される）
        [Test]
        public async Task LoadByLabelAsyncCancelThenFailureStillThrowsOperationCanceled()
        {
            LabelLocations.SetLocations(LABEL, Location("a", ASSET_TYPE));
            using var cts = new CancellationTokenSource();
            var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual, cts.Token);

            cts.Cancel();
            Worker("a", ASSET_TYPE).Fail(new InvalidOperationException("fake failure"));

            Exception caught = null;
            try { await task; } catch (Exception ex) { caught = ex; }

            Assert.IsInstanceOf<OperationCanceledException>(caught, "キャンセルが優先されるはず");
        }

        // ロード中に寿命が終わると、全件が回収され、OperationCanceledException が届く。
        // 呼び出し元はまだまとめチケットを受け取っておらず、Release を呼びようがないので、リーク警告は出ない
        [Test]
        public async Task LoadByLabelAsyncLifetimeEndsWhileLoadingThrowsOperationCanceled()
        {
            var owner = new GameObject("LoadingControllerLabelTests_Owner");
            try
            {
                LabelLocations.SetLocations(LABEL, Location("a", ASSET_TYPE), Location("b", ASSET_TYPE));
                using var counter = new WarningCounter("まとめチケット");
                var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Owner(owner));

                WatcherBinder.SimulateOwnerDestroyed(owner); // ロード中に寿命が終わる
                CompleteWorker("a", ASSET_TYPE);
                CompleteWorker("b", ASSET_TYPE);

                OperationCanceledException caught = null;
                try { await task; } catch (OperationCanceledException ex) { caught = ex; }
                Assert.IsNotNull(caught, "寿命の終了は OperationCanceledException になるはず");
                Assert.AreEqual(0, counter.AccCount, "受け取り前に寿命が終わった分は、リーク警告を出さないはず");
                await TestAsync.WaitUntil(() => Worker("a", ASSET_TYPE).DisposeCount == 1 && Worker("b", ASSET_TYPE).DisposeCount == 1);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        // ロード中に Dispose すると、全件が強制解放され、待機中の呼び出し元には OperationCanceledException が届く
        [Test]
        public async Task LoadByLabelAsyncDisposeWhileLoadingReleasesAllAndThrowsOperationCanceled()
        {
            LabelLocations.SetLocations(LABEL, Location("a", ASSET_TYPE), Location("b", ASSET_TYPE));
            var task = Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual);

            Controller.Dispose();
            Assert.AreEqual(1, Worker("a", ASSET_TYPE).DisposeCount, "Dispose で全件が強制解放されるはず");
            Assert.AreEqual(1, Worker("b", ASSET_TYPE).DisposeCount);

            CompleteWorker("a", ASSET_TYPE);
            CompleteWorker("b", ASSET_TYPE);
            OperationCanceledException caught = null;
            try { await task; } catch (OperationCanceledException ex) { caught = ex; }

            Assert.IsNotNull(caught);
            Assert.AreEqual(1, Worker("a", ASSET_TYPE).DisposeCount, "二重に解放されないはず");
            Assert.AreEqual(1, Worker("b", ASSET_TYPE).DisposeCount);
        }

        // ロケーションの解決に失敗すると、その例外が届き、何も確保しない
        [Test]
        public async Task LoadByLabelAsyncResolveFailurePropagatesAndReservesNothing()
        {
            var exception = new AssetLoadException(Key(LABEL), "fake resolve failure", null);
            LabelLocations.SetFailure(LABEL, exception);

            Exception caught = null;
            try { await Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual); } catch (Exception ex) { caught = ex; }

            Assert.AreSame(exception, caught);
            Assert.AreEqual(0, Factory.CreateCallCount);
        }

        // 確保の途中で例外が出ても、確保済みの分だけが巻き戻される
        [Test]
        public async Task LoadByLabelAsyncReservationFailureRollsBackReservedEntries()
        {
            // 2件目のアドレスが空文字のため、AssetKey.FromPath が例外を投げる
            LabelLocations.SetLocations(LABEL, Location("a", ASSET_TYPE), Location(string.Empty, ASSET_TYPE));

            ArgumentException caught = null;
            try { await Controller.LoadByLabelAsync(LABEL, AssetLifetime.Manual); } catch (ArgumentException ex) { caught = ex; }

            Assert.IsNotNull(caught);
            Assert.AreEqual(1, Factory.CreateCallCount, "1件目だけが確保されていたはず");
            Assert.AreEqual(0, Worker("a", ASSET_TYPE).DisposeCount, "ロード中のため、完了までは解放されないはず");

            // 確保済みの分は巻き戻されて RefCount が 0 に戻っているので、ロード完了後に解放される
            CompleteWorker("a", ASSET_TYPE);
            await TestAsync.WaitUntil(() => Worker("a", ASSET_TYPE).DisposeCount == 1);
        }
    }
}
