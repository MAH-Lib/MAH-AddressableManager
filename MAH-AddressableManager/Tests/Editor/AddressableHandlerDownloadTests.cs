using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Core.Download;
using MAH.AddressableManager.Core.Initialize;
using MAH.AddressableManager.Core.Loading;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// AddressableHandler のダウンロードまわり（転送）のテスト。
    /// Handler は転送だけで状態を持たないため、偽の Service を持つ DownloadController を挟んで、
    /// 引数なし版が SO の Label を使うこと・Label を渡す版が優先されること・null の Label が ArgumentNullException になることを確かめる。
    /// </summary>
    internal sealed class AddressableHandlerDownloadTests
    {
        private const string SETTINGS_LABEL = "settings-label";

        private FakeAddressablesDownloadService _service;
        private InitializeController _initializeController;
        private DownloadController _downloadController;
        private LoadingController _loadingController;
        private IAddressableHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _service = new FakeAddressablesDownloadService();
            var initialize_settings = new AddressableInitializeSettings(InitializeMode.Manual, LoadBeforeInitializePolicy.PassThrough, true, false);
            _initializeController = new InitializeController(initialize_settings, new InitializeStateModel(), new FakeAddressablesInitializeService());

            // 初期化の待機は、この Handler のテストでは使わない（常に初期化済みの偽のゲートを渡す）
            var download_settings = new AddressableDownloadSettings(CatalogUpdatePolicy.None, new[] { SETTINGS_LABEL }, 0, 0, false);
            _downloadController = new DownloadController(download_settings, new DownloadStatusModel(), _service, new FakeInitializationGate());

            var loading_settings = new AddressableLoadingSettings(true, 3f, 3, 1000);
            _loadingController = new LoadingController(
                loading_settings,
                new FakeAssetLoaderServiceFactory(),
                new FakeLabelLocationService(),
                new FakeAssetLifetimeWatcherBinder(),
                new FakeInitializationGate(),
                new AssetRegistryModel(),
                new IssuedTicketRegistryModel(),
                new OwnerWatchRegistryModel(),
                new LeakRegistryModel(),
                new OwnerReclaimRegistryModel());

            _handler = new AddressableHandler(_initializeController, _downloadController, _loadingController);
        }

        [TearDown]
        public void TearDown()
        {
            _loadingController.Dispose();
            _downloadController.Dispose();
            _initializeController.Dispose();
        }

        // 何も処理していないときの状態は None で、Controller の状態と一致する
        [Test]
        public void DownloadStatusIsNoneInitiallyAndFollowsController()
        {
            Assert.AreEqual(DownloadPhase.None, _handler.AccDownloadStatus.AccPhase);
            Assert.IsFalse(_handler.AccDownloadStatus.AccIsBusy);
        }

        // 処理の後の状態は、Controller の状態と一致する（取得し直すと最新の値）
        [Test]
        public async Task DownloadStatusFollowsControllerAfterProcess()
        {
            await _handler.HasUpdateAsync();

            Assert.AreEqual(DownloadPhase.Completed, _handler.AccDownloadStatus.AccPhase);
            Assert.AreEqual(_downloadController.AccStatus.AccPhase, _handler.AccDownloadStatus.AccPhase);
        }

        // Label を渡さない版は、SO の Label を対象にする
        [Test]
        public async Task NoArgumentVersionsUseSettingsLabels()
        {
            await _handler.HasUpdateAsync();
            CollectionAssert.AreEqual(new[] { SETTINGS_LABEL }, _service.LastSizeLabels);

            await _handler.CheckUpdateAsync();
            CollectionAssert.AreEqual(new[] { SETTINGS_LABEL }, _service.LastSizeLabels);

            _service.SizeInfo = new DownloadSizeInfo(1, 10);
            await _handler.DownloadAsync();
            CollectionAssert.AreEqual(new[] { SETTINGS_LABEL }, _service.LastDownloadLabels);
        }

        // Label を渡す版は、渡した Label を対象にする（SO の設定を上書きする）
        [Test]
        public async Task LabelVersionsOverrideSettingsLabels()
        {
            var labels = new[] { "explicit-label" };

            await _handler.HasUpdateAsync(labels);
            CollectionAssert.AreEqual(labels, _service.LastSizeLabels);

            await _handler.CheckUpdateAsync(labels);
            CollectionAssert.AreEqual(labels, _service.LastSizeLabels);

            _service.SizeInfo = new DownloadSizeInfo(1, 10);
            await _handler.DownloadAsync(labels);
            CollectionAssert.AreEqual(labels, _service.LastDownloadLabels);
        }

        // Label を渡す版に null を渡すと ArgumentNullException（Controller では null が「SO の Label を使う」を意味するため、Handler で止める）。
        // 呼び出し時には投げず、await の時点で届く（Controller の検証エラーと同じ）
        [Test]
        public async Task NullLabelsThrowArgumentNullWhenAwaited()
        {
            var null_labels = (System.Collections.Generic.IReadOnlyList<string>)null;

            await TestAsync.Catch<ArgumentNullException>(async () => await _handler.HasUpdateAsync(null_labels));
            await TestAsync.Catch<ArgumentNullException>(async () => await _handler.CheckUpdateAsync(null_labels));
            await TestAsync.Catch<ArgumentNullException>(async () => await _handler.DownloadAsync(null_labels));

            Assert.AreEqual(DownloadPhase.None, _handler.AccDownloadStatus.AccPhase, "検証で止まる場合は、状態を変えないはず");
        }

        // 呼び出した時点では例外を投げない（await するまで届かない）
        [Test]
        public void NullLabelsDoNotThrowAtCallTime()
        {
            var null_labels = (System.Collections.Generic.IReadOnlyList<string>)null;

            Assert.DoesNotThrow(() => _handler.HasUpdateAsync(null_labels));
            Assert.DoesNotThrow(() => _handler.CheckUpdateAsync(null_labels));
            Assert.DoesNotThrow(() => _handler.DownloadAsync(null_labels));
        }

        // 結果と進捗が、そのまま呼び出し元へ届く
        [Test]
        public async Task ResultsAndProgressReachCaller()
        {
            _service.CatalogIds.Add("catalog-1");
            _service.SizeInfo = new DownloadSizeInfo(2, 200);
            CreateHandlerWithPolicy(CatalogUpdatePolicy.CheckOnly);

            var result = await _handler.CheckUpdateAsync();
            Assert.AreEqual(1, result.AccCatalogUpdateCount);
            Assert.AreEqual(2, result.AccBundleCount);
            Assert.AreEqual(200L, result.AccTotalBytes);
            Assert.IsTrue(result.AccIsCatalogUpdatePending);

            var progress = new RecordingDownloadProgress();
            await _handler.DownloadAsync(progress);
            Assert.AreEqual(DownloadPhase.Completed, progress.Last.AccPhase);
        }

        // カタログ更新の方針だけを差し替えた DownloadController と Handler を作り直す
        private void CreateHandlerWithPolicy(CatalogUpdatePolicy policy)
        {
            _downloadController.Dispose();
            var settings = new AddressableDownloadSettings(policy, new[] { SETTINGS_LABEL }, 0, 0, false);
            _downloadController = new DownloadController(settings, new DownloadStatusModel(), _service, new FakeInitializationGate());
            _handler = new AddressableHandler(_initializeController, _downloadController, _loadingController);
        }
    }
}
