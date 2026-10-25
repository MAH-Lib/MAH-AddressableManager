using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Initialize;
using UnityEngine;
using UnityEngine.TestTools;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class InitializeControllerTests
    {
        // 失敗のときに出る LogError の一部（Console の Error を、テストの失敗にしないために期待する）
        private static readonly Regex FAILURE_LOG = new Regex("Addressables の初期化に失敗しました");

        private FakeAddressablesInitializeService _service;
        private InitializeStateModel _model;
        private InitializeController _controller;

        [SetUp]
        public void SetUp()
        {
            _service = new FakeAddressablesInitializeService();
            _model = new InitializeStateModel();
            _controller = CreateController(auto_release_handle: true);
        }

        [TearDown]
        public void TearDown()
        {
            _controller.Dispose();
        }

        private InitializeController CreateController(bool auto_release_handle)
        {
            var settings = new AddressableInitializeSettings(InitializeMode.Manual, LoadBeforeInitializePolicy.PassThrough, auto_release_handle, false);
            return new InitializeController(settings, _model, _service);
        }

        // 初期状態は NotStarted で、Service はまだ呼ばれていない
        [Test]
        public void NewControllerIsNotStartedAndDoesNotCallService()
        {
            Assert.AreEqual(InitializeState.NotStarted, _controller.AccState);
            Assert.IsFalse(_controller.AccIsInitialized);
            Assert.AreEqual(0, _service.StartCallCount);
        }

        // 初期化を始めると Running になり、Service に autoReleaseHandle の設定値が渡る。完了すると Succeeded になって待機が終わる
        [Test]
        public async Task InitializeAsyncRunsThenSucceeds()
        {
            var task = _controller.InitializeAsync(null, CancellationToken.None);

            Assert.AreEqual(InitializeState.Running, _controller.AccState);
            Assert.AreEqual(1, _service.StartCallCount);
            Assert.IsTrue(_service.LastAutoReleaseHandle);
            Assert.IsFalse(task.IsCompleted);

            _service.Succeed();
            await task;

            Assert.AreEqual(InitializeState.Succeeded, _controller.AccState);
            Assert.IsTrue(_controller.AccIsInitialized);
        }

        // 2回（以上）呼んでも初期化は1回だけで、全員が同じ処理を待つ
        [Test]
        public async Task InitializeAsyncCalledTwiceInitializesOnce()
        {
            var task1 = _controller.InitializeAsync(null, CancellationToken.None);
            var task2 = _controller.InitializeAsync(null, CancellationToken.None);

            Assert.AreEqual(1, _service.StartCallCount);

            _service.Succeed();
            await task1;
            await task2;

            Assert.AreEqual(1, _service.StartCallCount);
        }

        // 成功した後の InitializeAsync は、Service を呼ばずにすぐ完了する
        [Test]
        public async Task InitializeAsyncAfterSucceededCompletesImmediately()
        {
            var first = _controller.InitializeAsync(null, CancellationToken.None);
            _service.Succeed();
            await first;

            var task = _controller.InitializeAsync(null, CancellationToken.None);

            Assert.IsTrue(task.IsCompletedSuccessfully);
            Assert.AreEqual(1, _service.StartCallCount);
        }

        // Addressables が初期化済みで、始めた時点で完了する場合も、Running を経由して Succeeded になり、成功が通知される
        [Test]
        public async Task InitializeAsyncCompletingImmediatelyBecomesSucceededAndRaisesEvent()
        {
            _service.CompleteImmediately = true;
            var raised = 0;
            _controller.OnSucceeded += () => raised++;

            await _controller.InitializeAsync(null, CancellationToken.None);

            Assert.AreEqual(InitializeState.Succeeded, _controller.AccState);
            Assert.AreEqual(1, raised);
        }

        // 成功すると OnSucceeded が1回だけ発火し、OnFailed は発火しない
        [Test]
        public async Task SuccessRaisesOnSucceededOnce()
        {
            var succeeded = 0;
            var failed = 0;
            _controller.OnSucceeded += () => succeeded++;
            _controller.OnFailed += _ => failed++;

            var task = _controller.InitializeAsync(null, CancellationToken.None);
            _service.Succeed();
            await task;

            Assert.AreEqual(1, succeeded);
            Assert.AreEqual(0, failed);
        }

        // 失敗は、待っている全員に AddressableInitializeException（InnerException に元の例外）で届き、OnFailed も1回発火する
        [Test]
        public async Task FailureReachesAllWaitersAndRaisesOnFailedOnce()
        {
            LogAssert.Expect(LogType.Error, FAILURE_LOG);
            var inner = new InvalidOperationException("元の失敗");
            AddressableInitializeException raised_exception = null;
            var raised = 0;
            _controller.OnFailed += ex => { raised++; raised_exception = ex; };

            var task1 = _controller.InitializeAsync(null, CancellationToken.None);
            var task2 = _controller.InitializeAsync(null, CancellationToken.None);
            _service.Fail(inner);

            var ex1 = await TestAsync.Catch<AddressableInitializeException>(task1);
            var ex2 = await TestAsync.Catch<AddressableInitializeException>(task2);

            Assert.AreEqual(InitializeState.Failed, _controller.AccState);
            Assert.AreSame(inner, ex1.InnerException);
            Assert.AreSame(ex1, ex2, "全員に同じ例外が届くはず");
            Assert.AreEqual(1, raised);
            Assert.AreSame(ex1, raised_exception);
        }

        // 失敗した後の InitializeAsync は、Service を呼ばずに、毎回同じ例外をすぐ返す
        [Test]
        public async Task InitializeAsyncAfterFailureReturnsSameExceptionWithoutCallingService()
        {
            LogAssert.Expect(LogType.Error, FAILURE_LOG);
            var first = _controller.InitializeAsync(null, CancellationToken.None);
            _service.Fail(new Exception("失敗"));
            var expected = await TestAsync.Catch<AddressableInitializeException>(first);

            var ex1 = await TestAsync.Catch<AddressableInitializeException>(_controller.InitializeAsync(null, CancellationToken.None));
            var ex2 = await TestAsync.Catch<AddressableInitializeException>(_controller.InitializeAsync(null, CancellationToken.None));

            Assert.AreSame(expected, ex1);
            Assert.AreSame(expected, ex2);
            Assert.AreEqual(1, _service.StartCallCount, "失敗した後は Addressables（Service）を呼ばないはず");
        }

        // 自動モードの開始：待たずに初期化が始まり、失敗しても状態が Failed になり、後から呼んだ InitializeAsync に同じ例外が届く
        [Test]
        public async Task StartAutoInitializeStartsAndObservesFailure()
        {
            LogAssert.Expect(LogType.Error, FAILURE_LOG);

            _controller.StartAutoInitialize();

            Assert.AreEqual(InitializeState.Running, _controller.AccState);
            Assert.AreEqual(1, _service.StartCallCount);

            _service.Fail(new Exception("失敗"));
            await TestAsync.WaitUntil(() => _controller.AccState == InitializeState.Failed);

            var ex = await TestAsync.Catch<AddressableInitializeException>(_controller.InitializeAsync(null, CancellationToken.None));
            Assert.AreSame(_model.AccFailure, ex);
            Assert.AreEqual(1, _service.StartCallCount, "失敗した後は Addressables（Service）を呼ばないはず");
        }

        // Service の同期の例外（失敗した Task）も Failed になる
        [Test]
        public async Task SynchronousFailureFromServiceBecomesFailed()
        {
            LogAssert.Expect(LogType.Error, FAILURE_LOG);
            var inner = new ArgumentOutOfRangeException("index");
            _service.FailOnStart = inner;

            var ex = await TestAsync.Catch<AddressableInitializeException>(_controller.InitializeAsync(null, CancellationToken.None));

            Assert.AreEqual(InitializeState.Failed, _controller.AccState);
            Assert.AreSame(inner, ex.InnerException);
        }

        // 呼び出し元のキャンセルは、その呼び出し元の待機だけを終わらせ、初期化も他の呼び出し元も止めない
        [Test]
        public async Task CancelledCallerDoesNotAffectInitializationOrOtherCallers()
        {
            using var cts = new CancellationTokenSource();
            var cancelled_task = _controller.InitializeAsync(null, cts.Token);
            var other_task = _controller.InitializeAsync(null, CancellationToken.None);

            cts.Cancel();
            await TestAsync.Catch<OperationCanceledException>(cancelled_task);
            Assert.AreEqual(InitializeState.Running, _controller.AccState, "キャンセルしても初期化は続くはず");

            _service.Succeed();
            await other_task;

            Assert.AreEqual(InitializeState.Succeeded, _controller.AccState);
        }

        // 進捗は、待っている呼び出し元に Tick ごとに届き、完了時には必ず 1 が届く。待機が終わった後は届かない
        [Test]
        public async Task ProgressIsReportedOnTickAndCompletesWithOne()
        {
            var progress = new RecordingProgress();
            var task = _controller.InitializeAsync(progress, CancellationToken.None);

            _service.Percent = 0.4f;
            _controller.Tick(0.016f);
            _service.Percent = 0.8f;
            _controller.Tick(0.016f);
            _service.Succeed();
            await task;
            _controller.Tick(0.016f); // 成功の後は何も届かない

            CollectionAssert.AreEqual(new[] { 0.4f, 0.8f, 1f }, progress.Values);
        }

        // 後から合流した呼び出し元にも、合流した時点以降の進捗が届く
        [Test]
        public async Task LateJoinerReceivesProgressFromJoinOn()
        {
            var first = new RecordingProgress();
            var late = new RecordingProgress();
            var task1 = _controller.InitializeAsync(first, CancellationToken.None);
            _service.Percent = 0.3f;
            _controller.Tick(0.016f);

            var task2 = _controller.InitializeAsync(late, CancellationToken.None);
            _service.Percent = 0.6f;
            _controller.Tick(0.016f);
            _service.Succeed();
            await task1;
            await task2;

            CollectionAssert.AreEqual(new[] { 0.3f, 0.6f, 1f }, first.Values);
            CollectionAssert.AreEqual(new[] { 0.6f, 1f }, late.Values);
        }

        // Addressables が初期化済みで、始めた時点で同期的に完了する場合も、完了時の 1 が届く（通知先を始める前に登録しているため）
        [Test]
        public async Task ProgressReceivesOneWhenInitializationCompletesSynchronously()
        {
            _service.CompleteImmediately = true;
            var progress = new RecordingProgress();

            await _controller.InitializeAsync(progress, CancellationToken.None);

            CollectionAssert.AreEqual(new[] { 1f }, progress.Values);
        }

        // 成功済みの後に呼んだ場合も、完了時の 1 がすぐ届く
        [Test]
        public async Task ProgressReceivesOneWhenAlreadySucceeded()
        {
            var first = _controller.InitializeAsync(null, CancellationToken.None);
            _service.Succeed();
            await first;
            var progress = new RecordingProgress();

            await _controller.InitializeAsync(progress, CancellationToken.None);

            CollectionAssert.AreEqual(new[] { 1f }, progress.Values);
        }

        // キャンセルした呼び出し元の進捗通知先は外れ、以降の Tick では届かない
        [Test]
        public async Task CancelledCallerNoLongerReceivesProgress()
        {
            using var cts = new CancellationTokenSource();
            var cancelled_progress = new RecordingProgress();
            var cancelled_task = _controller.InitializeAsync(cancelled_progress, cts.Token);
            var other_task = _controller.InitializeAsync(null, CancellationToken.None);
            cts.Cancel();
            await TestAsync.Catch<OperationCanceledException>(cancelled_task);

            _service.Percent = 0.5f;
            _controller.Tick(0.016f);
            _service.Succeed();
            await other_task;

            Assert.IsEmpty(cancelled_progress.Values);
        }

        // 初期化中でないとき（未開始・成功後）の Tick は、Service の進捗を読まない
        [Test]
        public async Task TickOutsideRunningDoesNothing()
        {
            var progress = new RecordingProgress();
            _model.AddProgressTarget(progress);
            _service.Percent = 0.9f;

            _controller.Tick(0.016f); // NotStarted

            var task = _controller.InitializeAsync(null, CancellationToken.None);
            _service.Succeed();
            await task;
            _controller.Tick(0.016f); // Succeeded

            Assert.IsEmpty(progress.Values.FindAll(value => value != 1f), "Running 以外では通知されないはず");
        }

        // autoReleaseHandle=false のときだけ、Dispose で handle を解放する
        [Test]
        public void DisposeReleasesHandleOnlyWhenAutoReleaseIsOff()
        {
            _controller.Dispose();
            Assert.AreEqual(0, _service.ReleaseHandleCallCount, "autoReleaseHandle=true では Controller が解放しないはず");

            var manual_service = new FakeAddressablesInitializeService();
            var settings = new AddressableInitializeSettings(InitializeMode.Manual, LoadBeforeInitializePolicy.PassThrough, false, false);
            var manual_controller = new InitializeController(settings, new InitializeStateModel(), manual_service);
            manual_controller.Dispose();
            manual_controller.Dispose(); // 2回目は何もしない

            Assert.AreEqual(1, manual_service.ReleaseHandleCallCount);
        }

        // autoReleaseHandle=false の設定値が Service に渡る
        [Test]
        public void AutoReleaseHandleSettingIsPassedToService()
        {
            var settings = new AddressableInitializeSettings(InitializeMode.Manual, LoadBeforeInitializePolicy.PassThrough, false, false);
            var controller = new InitializeController(settings, new InitializeStateModel(), _service);

            _ = controller.InitializeAsync(null, CancellationToken.None);

            Assert.IsFalse(_service.LastAutoReleaseHandle);
            controller.Dispose();
        }

        // Dispose すると、待機中の InitializeAsync が OperationCanceledException で終わり、状態は変わらない
        [Test]
        public async Task DisposeCancelsWaitingCallersAndKeepsState()
        {
            var task = _controller.InitializeAsync(null, CancellationToken.None);

            _controller.Dispose();

            await TestAsync.Catch<OperationCanceledException>(task);
            Assert.AreEqual(InitializeState.Running, _controller.AccState);

            _service.Succeed(); // Dispose の後に Addressables が完了しても、状態は変わらない
            await Task.Delay(50); // 誤って続行する処理があれば、ここで走る
            Assert.AreEqual(InitializeState.Running, _controller.AccState);
        }

        // Dispose の後の新しい InitializeAsync は ObjectDisposedException
        [Test]
        public async Task InitializeAsyncAfterDisposeThrowsObjectDisposed()
        {
            _controller.Dispose();

            await TestAsync.Catch<ObjectDisposedException>(_controller.InitializeAsync(null, CancellationToken.None));
        }

        // イベントの購読側が例外を投げても、状態遷移と待機の完了は止まらない
        [Test]
        public async Task ThrowingEventHandlerDoesNotBreakInitialization()
        {
            LogAssert.Expect(LogType.Exception, new Regex("購読側の例外"));
            _controller.OnSucceeded += () => throw new Exception("購読側の例外");

            var task = _controller.InitializeAsync(null, CancellationToken.None);
            _service.Succeed();
            await task;

            Assert.AreEqual(InitializeState.Succeeded, _controller.AccState);
        }

        // 完了時の 1 の通知で通知先が例外を投げても、状態は Succeeded になり、イベントが発火し、待機が終わる
        [Test]
        public async Task ThrowingProgressDoesNotBreakInitialization()
        {
            LogAssert.Expect(LogType.Exception, new Regex("通知先の例外"));
            var raised = 0;
            _controller.OnSucceeded += () => raised++;

            var task = _controller.InitializeAsync(new ThrowingProgress(), CancellationToken.None);
            _service.Succeed();
            await task;

            Assert.AreEqual(InitializeState.Succeeded, _controller.AccState);
            Assert.AreEqual(1, raised);
        }

        // Tick で通知先が例外を投げても、例外は Tick の外に出ず、他の通知先に値が届く
        [Test]
        public async Task ThrowingProgressOnTickDoesNotStopOtherTargets()
        {
            LogAssert.Expect(LogType.Exception, new Regex("通知先の例外"));
            var other = new RecordingProgress();
            var task1 = _controller.InitializeAsync(new ThrowingProgress(), CancellationToken.None);
            var task2 = _controller.InitializeAsync(other, CancellationToken.None);

            _service.Percent = 0.5f;
            Assert.DoesNotThrow(() => _controller.Tick(0.016f));
            LogAssert.Expect(LogType.Exception, new Regex("通知先の例外")); // 完了時の 1 でも投げる
            _service.Succeed();
            await task1;
            await task2;

            CollectionAssert.AreEqual(new[] { 0.5f, 1f }, other.Values);
        }

        private sealed class ThrowingProgress : IProgress<float>
        {
            public void Report(float value) => throw new Exception("通知先の例外");
        }

        // 値を記録するだけの IProgress（Progress<T> は SynchronizationContext に投げるため使わない）
        private sealed class RecordingProgress : IProgress<float>
        {
            internal readonly List<float> Values = new();

            public void Report(float value) => Values.Add(value);
        }
    }
}
