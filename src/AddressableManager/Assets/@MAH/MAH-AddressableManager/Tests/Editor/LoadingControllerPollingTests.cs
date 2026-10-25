using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// Tick によるポーリングのテスト。owner のポーリング間隔・有効無効を個別に検証するため、
    /// LoadingControllerTestBase は使わずテストごとに専用の Controller を作る。
    /// </summary>
    internal sealed class LoadingControllerPollingTests
    {
        private static LoadingController CreateController(
            bool enable_polling, float interval_sec,
            out FakeAssetLoaderServiceFactory factory, out FakeAssetLifetimeWatcherBinder binder)
        {
            var settings = new AddressableLoadingSettings(enable_polling, interval_sec, 3, 1000);
            factory = new FakeAssetLoaderServiceFactory();
            binder = new FakeAssetLifetimeWatcherBinder();
            return new LoadingController(settings, factory, new FakeLabelLocationService(), binder, new FakeInitializationGate(), new AssetRegistryModel(), new IssuedTicketRegistryModel(), new OwnerWatchRegistryModel(), new LeakRegistryModel(), new OwnerReclaimRegistryModel());
        }

        // 間隔未経過なら走査しない
        [Test]
        public async Task TickBeforeIntervalDoesNotScan()
        {
            using var controller = CreateController(true, 3f, out var factory, out _);
            var owner = new GameObject("PollingOwner_NotYet");

            var key = AssetKey.FromPath("test/poll-not-yet");
            var task = controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));
            factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            await task;

            // FakeAssetLifetimeWatcherBinderは実際にAddComponentしないため、
            // ここでの破棄はWatcherのOnDestroyでは検知されず、ポーリングでのみ検知できる
            Object.DestroyImmediate(owner);

            controller.Tick(1f); // 間隔（3秒）未満

            Assert.AreEqual(0, factory.GetWorker(key).DisposeCount, "間隔未経過なので回収されないはず");
        }

        // 間隔経過後に偽nullのownerを回収する
        [Test]
        public async Task TickAfterIntervalReclaimsFakeNullOwner()
        {
            using var controller = CreateController(true, 3f, out var factory, out _);
            var owner = new GameObject("PollingOwner_Detect");

            var key = AssetKey.FromPath("test/poll-detect");
            var task = controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));
            factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            await task;

            Object.DestroyImmediate(owner);

            controller.Tick(3.5f); // 間隔（3秒）を超える

            Assert.AreEqual(1, factory.GetWorker(key).DisposeCount, "偽nullになったownerがポーリングで回収されるはず");
        }

        // ポーリング無効なら回収されない
        [Test]
        public async Task TickPollingDisabledDoesNotReclaim()
        {
            using var controller = CreateController(false, 3f, out var factory, out _);
            var owner = new GameObject("PollingOwner_Disabled");

            var key = AssetKey.FromPath("test/poll-disabled");
            var task = controller.LoadAsync<FakeAsset>(key, AssetLifetime.Owner(owner));
            factory.GetWorker(key).Complete(ScriptableObject.CreateInstance<FakeAsset>());
            await task;

            Object.DestroyImmediate(owner);

            controller.Tick(10f); // 十分に大きい経過時間でも

            Assert.AreEqual(0, factory.GetWorker(key).DisposeCount, "ポーリングが無効なので回収されないはず");
        }

        // owner登録が0件なら何もしない
        [Test]
        public void TickNoRegisteredOwnersDoesNothing()
        {
            using var controller = CreateController(true, 3f, out _, out _);
            Assert.DoesNotThrow(() => controller.Tick(100f));
        }
    }
}
