using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Core.Initialize;
using UnityEngine;
using UnityEngine.TestTools;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// InitializeController が実装する IInitializationGate の判定（仕様 7.4 の表の各マス）。
    /// </summary>
    internal sealed class InitializeControllerGateTests
    {
        private FakeAddressablesInitializeService _service;
        private InitializeController _controller;

        [SetUp]
        public void SetUp()
        {
            _service = new FakeAddressablesInitializeService();
        }

        [TearDown]
        public void TearDown()
        {
            _controller?.Dispose();
        }

        private IInitializationGate CreateGate(LoadBeforeInitializePolicy policy)
        {
            var settings = new AddressableInitializeSettings(InitializeMode.Manual, policy, true, false);
            _controller = new InitializeController(settings, new InitializeStateModel(), _service);
            return _controller;
        }

        // 状態を Running にする。完了は _service.Succeed / Fail で行う
        private void StartRunning() => _ = _controller.InitializeAsync(null, CancellationToken.None);

        // 状態を Succeeded にする
        private async Task MakeSucceeded()
        {
            var task = _controller.InitializeAsync(null, CancellationToken.None);
            _service.Succeed();
            await task;
        }

        // 状態を Failed にする（失敗の LogError を期待する）
        private async Task MakeFailed()
        {
            LogAssert.Expect(LogType.Error, new Regex("Addressables の初期化に失敗しました"));
            var task = _controller.InitializeAsync(null, CancellationToken.None);
            _service.Fail(new Exception("失敗"));
            await TestAsync.Catch<AddressableInitializeException>(task);
        }

        // ---- NotStarted ----

        [Test]
        public void NotStartedPassThroughIsOpen()
        {
            var gate = CreateGate(LoadBeforeInitializePolicy.PassThrough);

            Assert.IsTrue(gate.AccIsOpen);
        }

        [Test]
        public async Task NotStartedWaitForInitializeIsClosedAndWaitThrowsInvalidOperation()
        {
            var gate = CreateGate(LoadBeforeInitializePolicy.WaitForInitialize);

            Assert.IsFalse(gate.AccIsOpen);
            await TestAsync.Catch<InvalidOperationException>(gate.WaitUntilOpenAsync(CancellationToken.None));
        }

        // ---- Running ----

        [Test]
        public void RunningPassThroughIsOpen()
        {
            var gate = CreateGate(LoadBeforeInitializePolicy.PassThrough);
            StartRunning();

            Assert.IsTrue(gate.AccIsOpen);
        }

        // WaitForInitialize では閉じていて、初期化の完了で待機が終わる
        [Test]
        public async Task RunningWaitForInitializeIsClosedAndWaitCompletesOnSuccess()
        {
            var gate = CreateGate(LoadBeforeInitializePolicy.WaitForInitialize);
            StartRunning();

            Assert.IsFalse(gate.AccIsOpen);
            var wait = gate.WaitUntilOpenAsync(CancellationToken.None);
            Assert.IsFalse(wait.IsCompleted);

            _service.Succeed();
            await wait;

            Assert.IsTrue(gate.AccIsOpen);
        }

        // 初期化が失敗すると、待機中の Load にも AddressableInitializeException が届く
        [Test]
        public async Task RunningWaitForInitializeWaitThrowsInitializeExceptionOnFailure()
        {
            LogAssert.Expect(LogType.Error, new Regex("Addressables の初期化に失敗しました"));
            var gate = CreateGate(LoadBeforeInitializePolicy.WaitForInitialize);
            StartRunning();
            var wait = gate.WaitUntilOpenAsync(CancellationToken.None);

            _service.Fail(new Exception("失敗"));

            await TestAsync.Catch<AddressableInitializeException>(wait);
        }

        // 待機中に ct をキャンセルすると、待機だけが OperationCanceledException で終わり、初期化は続く
        [Test]
        public async Task RunningWaitCancelledThrowsOperationCanceledAndKeepsRunning()
        {
            var gate = CreateGate(LoadBeforeInitializePolicy.WaitForInitialize);
            StartRunning();
            using var cts = new CancellationTokenSource();
            var wait = gate.WaitUntilOpenAsync(cts.Token);

            cts.Cancel();

            await TestAsync.Catch<OperationCanceledException>(wait);
            Assert.AreEqual(InitializeState.Running, _controller.AccState);
        }

        // 待機中に Dispose すると、待機が OperationCanceledException で終わる（Loading 側が ObjectDisposedException に変換する）
        [Test]
        public async Task RunningWaitThrowsOperationCanceledOnDispose()
        {
            var gate = CreateGate(LoadBeforeInitializePolicy.WaitForInitialize);
            StartRunning();
            var wait = gate.WaitUntilOpenAsync(CancellationToken.None);

            _controller.Dispose();

            await TestAsync.Catch<OperationCanceledException>(wait);
        }

        // ---- Succeeded ----

        [TestCase(LoadBeforeInitializePolicy.PassThrough)]
        [TestCase(LoadBeforeInitializePolicy.WaitForInitialize)]
        public async Task SucceededIsOpenAndWaitCompletesImmediately(LoadBeforeInitializePolicy policy)
        {
            var gate = CreateGate(policy);
            await MakeSucceeded();

            Assert.IsTrue(gate.AccIsOpen);
            Assert.IsTrue(gate.WaitUntilOpenAsync(CancellationToken.None).IsCompletedSuccessfully);
        }

        // ---- Failed ----

        // 失敗した後は、設定に関係なく閉じていて、待機はすぐ同じ例外を返す
        [TestCase(LoadBeforeInitializePolicy.PassThrough)]
        [TestCase(LoadBeforeInitializePolicy.WaitForInitialize)]
        public async Task FailedIsClosedAndWaitThrowsSameInitializeException(LoadBeforeInitializePolicy policy)
        {
            var gate = CreateGate(policy);
            await MakeFailed();

            Assert.IsFalse(gate.AccIsOpen);
            var ex1 = await TestAsync.Catch<AddressableInitializeException>(gate.WaitUntilOpenAsync(CancellationToken.None));
            var ex2 = await TestAsync.Catch<AddressableInitializeException>(gate.WaitUntilOpenAsync(CancellationToken.None));
            Assert.AreSame(ex1, ex2);
            Assert.AreEqual(1, _service.StartCallCount, "ゲートの問い合わせで初期化をやり直さないはず");
        }

        // ---- WaitUntilInitializedAsync（Download 用。LoadBeforeInitializePolicy に関係なく、初期化の成功まで待つ） ----

        // 未開始：初期化を始めず、InvalidOperationException（Manual で InitializeAsync の呼び忘れにすぐ気づけるようにする）
        [TestCase(LoadBeforeInitializePolicy.PassThrough)]
        [TestCase(LoadBeforeInitializePolicy.WaitForInitialize)]
        public async Task InitializedNotStartedThrowsInvalidOperationWithoutStarting(LoadBeforeInitializePolicy policy)
        {
            var gate = CreateGate(policy);

            await TestAsync.Catch<InvalidOperationException>(gate.WaitUntilInitializedAsync(CancellationToken.None));

            Assert.AreEqual(InitializeState.NotStarted, _controller.AccState);
            Assert.AreEqual(0, _service.StartCallCount, "待機で初期化を始めないはず");
        }

        // 実行中：PassThrough でも待ち、初期化の成功で終わる（WaitUntilOpenAsync は PassThrough では待たない点が異なる）
        [TestCase(LoadBeforeInitializePolicy.PassThrough)]
        [TestCase(LoadBeforeInitializePolicy.WaitForInitialize)]
        public async Task InitializedRunningWaitsUntilSuccessRegardlessOfPolicy(LoadBeforeInitializePolicy policy)
        {
            var gate = CreateGate(policy);
            StartRunning();

            var wait = gate.WaitUntilInitializedAsync(CancellationToken.None);
            Assert.IsFalse(wait.IsCompleted, "初期化が成功するまで待つはず");

            _service.Succeed();
            await wait;

            Assert.AreEqual(InitializeState.Succeeded, _controller.AccState);
        }

        // 実行中に初期化が失敗すると、待機中の呼び出し元に AddressableInitializeException が届く
        [TestCase(LoadBeforeInitializePolicy.PassThrough)]
        [TestCase(LoadBeforeInitializePolicy.WaitForInitialize)]
        public async Task InitializedRunningThrowsInitializeExceptionOnFailure(LoadBeforeInitializePolicy policy)
        {
            LogAssert.Expect(LogType.Error, new Regex("Addressables の初期化に失敗しました"));
            var gate = CreateGate(policy);
            StartRunning();
            var wait = gate.WaitUntilInitializedAsync(CancellationToken.None);

            _service.Fail(new Exception("失敗"));

            await TestAsync.Catch<AddressableInitializeException>(wait);
        }

        // 待機中に ct をキャンセルすると、待機だけが OperationCanceledException で終わり、初期化は続く
        [Test]
        public async Task InitializedRunningWaitCancelledThrowsOperationCanceledAndKeepsRunning()
        {
            var gate = CreateGate(LoadBeforeInitializePolicy.PassThrough);
            StartRunning();
            using var cts = new CancellationTokenSource();
            var wait = gate.WaitUntilInitializedAsync(cts.Token);

            cts.Cancel();

            await TestAsync.Catch<OperationCanceledException>(wait);
            Assert.AreEqual(InitializeState.Running, _controller.AccState);
        }

        // 待機中に Dispose すると、待機が OperationCanceledException で終わる
        [Test]
        public async Task InitializedRunningWaitThrowsOperationCanceledOnDispose()
        {
            var gate = CreateGate(LoadBeforeInitializePolicy.PassThrough);
            StartRunning();
            var wait = gate.WaitUntilInitializedAsync(CancellationToken.None);

            _controller.Dispose();

            await TestAsync.Catch<OperationCanceledException>(wait);
        }

        // 成功済み：すぐ完了する（待機中の確保もしない）
        [TestCase(LoadBeforeInitializePolicy.PassThrough)]
        [TestCase(LoadBeforeInitializePolicy.WaitForInitialize)]
        public async Task InitializedSucceededCompletesImmediately(LoadBeforeInitializePolicy policy)
        {
            var gate = CreateGate(policy);
            await MakeSucceeded();

            Assert.IsTrue(gate.WaitUntilInitializedAsync(CancellationToken.None).IsCompletedSuccessfully);
        }

        // 失敗済み：すぐ同じ例外を返し、初期化をやり直さない
        [TestCase(LoadBeforeInitializePolicy.PassThrough)]
        [TestCase(LoadBeforeInitializePolicy.WaitForInitialize)]
        public async Task InitializedFailedThrowsSameInitializeExceptionWithoutRestarting(LoadBeforeInitializePolicy policy)
        {
            var gate = CreateGate(policy);
            await MakeFailed();

            var ex1 = await TestAsync.Catch<AddressableInitializeException>(gate.WaitUntilInitializedAsync(CancellationToken.None));
            var ex2 = await TestAsync.Catch<AddressableInitializeException>(gate.WaitUntilInitializedAsync(CancellationToken.None));

            Assert.AreSame(ex1, ex2);
            Assert.AreEqual(1, _service.StartCallCount, "ゲートの問い合わせで初期化をやり直さないはず");
        }
    }
}
