using NUnit.Framework;
using MAH.AddressableManager.Core.Loading;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// LoadingController のテスト共通セットアップ。偽の Service ファクトリ・Watcher バインダーを注入する。
    /// </summary>
    internal abstract class LoadingControllerTestBase
    {
        protected LoadingController Controller { get; private set; }
        protected FakeAssetLoaderServiceFactory Factory { get; private set; }
        protected FakeLabelLocationService LabelLocations { get; private set; }
        protected FakeAssetLifetimeWatcherBinder WatcherBinder { get; private set; }
        protected LeakRegistryModel Leaks { get; private set; } // リークの記録を調べるテストが読む
        protected OwnerWatchRegistryModel OwnerWatch { get; private set; } // 監視中の Owner を調べるテストが読む
        protected OwnerReclaimRegistryModel OwnerReclaims { get; private set; } // Owner の回収の記録を調べるテストが読む
        protected FakeInitializationGate Gate { get; private set; } // 既定は常に開いている。初期化の待機を調べるテストだけが閉じる

        [SetUp]
        public void BaseSetUp()
        {
            var settings = new AddressableLoadingSettings(true, 3f, 3, 1000);
            Factory = new FakeAssetLoaderServiceFactory();
            LabelLocations = new FakeLabelLocationService();
            WatcherBinder = new FakeAssetLifetimeWatcherBinder();
            Gate = new FakeInitializationGate();
            Leaks = new LeakRegistryModel();
            OwnerWatch = new OwnerWatchRegistryModel();
            OwnerReclaims = new OwnerReclaimRegistryModel();
            Controller = new LoadingController(settings, Factory, LabelLocations, WatcherBinder, Gate, new AssetRegistryModel(), new IssuedTicketRegistryModel(), OwnerWatch, Leaks, OwnerReclaims);
        }

        [TearDown]
        public void BaseTearDown()
        {
            Controller.Dispose();
        }
    }
}
