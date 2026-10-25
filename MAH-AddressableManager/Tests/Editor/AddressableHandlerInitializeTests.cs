using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Core.Download;
using MAH.AddressableManager.Core.Initialize;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;
using UnityEngine.TestTools;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// AddressableHandler の初期化まわり（状態の参照・イベントの購読と解除・InitializeAsync）のテスト。
    /// Handler は転送だけで状態を持たないため、偽の Service を持つ InitializeController を挟んで、転送が正しく届くことを確かめる。
    /// </summary>
    internal sealed class AddressableHandlerInitializeTests
    {
        // 失敗のときに出る LogError の一部（Console の Error を、テストの失敗にしないために期待する）
        private static readonly Regex FAILURE_LOG = new Regex("Addressables の初期化に失敗しました");

        private FakeAddressablesInitializeService _service;
        private InitializeController _initializeController;
        private DownloadController _downloadController;
        private LoadingController _loadingController;
        private IAddressableHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _service = new FakeAddressablesInitializeService();
            var initialize_settings = new AddressableInitializeSettings(InitializeMode.Manual, LoadBeforeInitializePolicy.PassThrough, true, false);
            _initializeController = new InitializeController(initialize_settings, new InitializeStateModel(), _service);

            // Download・Loading は、この Handler のテストでは使わない。コンストラクタに渡すためだけに、偽の部品で作る
            _downloadController = new DownloadController(
                new AddressableDownloadSettings(),
                new DownloadStatusModel(),
                new FakeAddressablesDownloadService(),
                new FakeInitializationGate());
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

        // 対象が Task を返す環境でも UniTask を返す環境でも同じ書き方で待てるよう、Task にそろえて呼ぶ
        private static async Task InitializeAsync(IAddressableHandler handler, IProgress<float> progress, CancellationToken ct)
            => await handler.InitializeAsync(progress, ct);

        // 初期状態の転送：NotStarted で、初期化にはまだ成功していない
        [Test]
        public void InitialStateIsNotStartedAndNotInitialized()
        {
            Assert.AreEqual(InitializeState.NotStarted, _handler.AccInitializeState);
            Assert.IsFalse(_handler.AccIsInitialized);
        }

        // 状態は Controller の状態と一致して進む：Running → Succeeded
        [Test]
        public async Task StateFollowsControllerThroughRunningToSucceeded()
        {
            var task = InitializeAsync(_handler, null, CancellationToken.None);

            Assert.AreEqual(InitializeState.Running, _handler.AccInitializeState);
            Assert.AreEqual(_initializeController.AccState, _handler.AccInitializeState);
            Assert.IsFalse(_handler.AccIsInitialized);

            _service.Succeed();
            await task;

            Assert.AreEqual(InitializeState.Succeeded, _handler.AccInitializeState);
            Assert.IsTrue(_handler.AccIsInitialized);
        }

        // 失敗後の状態は Failed。AccIsInitialized は false のまま
        [Test]
        public async Task StateBecomesFailedAfterFailure()
        {
            LogAssert.Expect(LogType.Error, FAILURE_LOG);
            var task = InitializeAsync(_handler, null, CancellationToken.None);
            _service.Fail(new Exception("失敗"));

            await TestAsync.Catch<AddressableInitializeException>(task);

            Assert.AreEqual(InitializeState.Failed, _handler.AccInitializeState);
            Assert.IsFalse(_handler.AccIsInitialized);
        }

        // Handler で購読した OnInitialized は、成功したときに1回だけ呼ばれる（OnInitializeFailed は呼ばれない）
        [Test]
        public async Task OnInitializedSubscribedViaHandlerIsRaisedOnceOnSuccess()
        {
            var succeeded = 0;
            var failed = 0;
            _handler.OnInitialized += () => succeeded++;
            _handler.OnInitializeFailed += _ => failed++;

            var task = InitializeAsync(_handler, null, CancellationToken.None);
            _service.Succeed();
            await task;

            Assert.AreEqual(1, succeeded);
            Assert.AreEqual(0, failed);
        }

        // 完了済みの後に InitializeAsync を呼び直しても、イベントは再発火しない
        [Test]
        public async Task OnInitializedIsNotRaisedAgainByLaterInitializeAsync()
        {
            var succeeded = 0;
            _handler.OnInitialized += () => succeeded++;
            var first = InitializeAsync(_handler, null, CancellationToken.None);
            _service.Succeed();
            await first;

            await InitializeAsync(_handler, null, CancellationToken.None);

            Assert.AreEqual(1, succeeded);
        }

        // Handler で購読した OnInitializeFailed は、失敗したときに、待っている呼び出し元へ届く例外と同じものを1回だけ受け取る
        [Test]
        public async Task OnInitializeFailedSubscribedViaHandlerIsRaisedOnceWithSameException()
        {
            LogAssert.Expect(LogType.Error, FAILURE_LOG);
            var succeeded = 0;
            var failed = 0;
            AddressableInitializeException raised_exception = null;
            _handler.OnInitialized += () => succeeded++;
            _handler.OnInitializeFailed += ex => { failed++; raised_exception = ex; };

            var task = InitializeAsync(_handler, null, CancellationToken.None);
            _service.Fail(new InvalidOperationException("元の失敗"));
            var awaited_exception = await TestAsync.Catch<AddressableInitializeException>(task);

            Assert.AreEqual(0, succeeded);
            Assert.AreEqual(1, failed);
            Assert.AreSame(awaited_exception, raised_exception);
        }

        // 購読を解除（remove）した後は、Controller のイベントが発火しても呼ばれない
        [Test]
        public async Task UnsubscribedHandlersAreNotCalled()
        {
            var succeeded = 0;
            Action on_initialized = () => succeeded++;
            _handler.OnInitialized += on_initialized;
            _handler.OnInitialized -= on_initialized;

            var task = InitializeAsync(_handler, null, CancellationToken.None);
            _service.Succeed();
            await task;

            Assert.AreEqual(0, succeeded);
        }

        // 失敗側も同じく、解除すると呼ばれない
        [Test]
        public async Task UnsubscribedFailureHandlersAreNotCalled()
        {
            LogAssert.Expect(LogType.Error, FAILURE_LOG);
            var failed = 0;
            Action<AddressableInitializeException> on_failed = _ => failed++;
            _handler.OnInitializeFailed += on_failed;
            _handler.OnInitializeFailed -= on_failed;

            var task = InitializeAsync(_handler, null, CancellationToken.None);
            _service.Fail(new Exception("失敗"));
            await TestAsync.Catch<AddressableInitializeException>(task);

            Assert.AreEqual(0, failed);
        }

        // 購読の解除は、同じ購読者だけを外す（ほかの購読者には影響しない）
        [Test]
        public async Task UnsubscribingOneHandlerKeepsOthers()
        {
            var kept = 0;
            var removed = 0;
            Action on_kept = () => kept++;
            Action on_removed = () => removed++;
            _handler.OnInitialized += on_kept;
            _handler.OnInitialized += on_removed;
            _handler.OnInitialized -= on_removed;

            var task = InitializeAsync(_handler, null, CancellationToken.None);
            _service.Succeed();
            await task;

            Assert.AreEqual(1, kept);
            Assert.AreEqual(0, removed);
        }

        // 2回以上呼んでも、初期化は1回だけで、全員が同じ処理を待つ
        [Test]
        public async Task InitializeAsyncCalledTwiceInitializesOnce()
        {
            var task1 = InitializeAsync(_handler, null, CancellationToken.None);
            var task2 = InitializeAsync(_handler, null, CancellationToken.None);

            Assert.AreEqual(1, _service.StartCallCount);

            _service.Succeed();
            await task1;
            await task2;

            Assert.AreEqual(InitializeState.Succeeded, _handler.AccInitializeState);
            Assert.AreEqual(1, _service.StartCallCount);
        }

        // 呼び出し元の ct のキャンセルは、その呼び出し元の待機だけを OperationCanceledException で終わらせ、初期化も他の呼び出し元も止めない
        [Test]
        public async Task CancelledCallerDoesNotAffectInitializationOrOtherCallers()
        {
            using var cts = new CancellationTokenSource();
            var cancelled_task = InitializeAsync(_handler, null, cts.Token);
            var other_task = InitializeAsync(_handler, null, CancellationToken.None);

            cts.Cancel();
            await TestAsync.Catch<OperationCanceledException>(cancelled_task);
            Assert.AreEqual(InitializeState.Running, _handler.AccInitializeState, "キャンセルしても初期化は続くはず");

            _service.Succeed();
            await other_task;

            Assert.AreEqual(InitializeState.Succeeded, _handler.AccInitializeState);
        }

        // progress は Controller へ渡り、完了時には必ず 1 が届く
        [Test]
        public async Task ProgressPassedToHandlerReceivesOneOnCompletion()
        {
            var progress = new RecordingProgress();

            var task = InitializeAsync(_handler, progress, CancellationToken.None);
            _service.Succeed();
            await task;

            Assert.IsNotEmpty(progress.Values);
            Assert.AreEqual(1f, progress.Values[progress.Values.Count - 1], 0.0001f);
        }

        // 失敗した後の InitializeAsync は、Service を呼ばずに、毎回同じ例外を返す
        [Test]
        public async Task InitializeAsyncAfterFailureReturnsSameExceptionWithoutCallingService()
        {
            LogAssert.Expect(LogType.Error, FAILURE_LOG);
            var first = InitializeAsync(_handler, null, CancellationToken.None);
            _service.Fail(new Exception("失敗"));
            var expected = await TestAsync.Catch<AddressableInitializeException>(first);

            var again = await TestAsync.Catch<AddressableInitializeException>(InitializeAsync(_handler, null, CancellationToken.None));

            Assert.AreSame(expected, again);
            Assert.AreEqual(1, _service.StartCallCount);
        }

        /// <summary> 届いた進捗を記録するだけの IProgress（Progress&lt;T&gt; は同期コンテキストに投げ直すため、テストでは使わない） </summary>
        private sealed class RecordingProgress : IProgress<float>
        {
            internal List<float> Values { get; } = new();

            public void Report(float value) => Values.Add(value);
        }
    }
}
