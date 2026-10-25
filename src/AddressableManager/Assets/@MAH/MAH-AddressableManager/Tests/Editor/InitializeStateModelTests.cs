using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Initialize;
using UnityEngine;
using UnityEngine.TestTools;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class InitializeStateModelTests
    {
        private InitializeStateModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = new InitializeStateModel();
        }

        // 初期状態は NotStarted で、共有タスクも失敗の例外も無い
        [Test]
        public void NewModelIsNotStarted()
        {
            Assert.AreEqual(InitializeState.NotStarted, _model.AccState);
            Assert.IsNull(_model.AccSharedTask);
            Assert.IsNull(_model.AccFailure);
        }

        // NotStarted → Running で、共有タスクを記録する
        [Test]
        public void MarkRunningFromNotStartedSetsRunningAndSharedTask()
        {
            var task = new TaskCompletionSource<bool>().Task;

            Assert.IsTrue(_model.MarkRunning(task));

            Assert.AreEqual(InitializeState.Running, _model.AccState);
            Assert.AreSame(task, _model.AccSharedTask);
        }

        // 2回目の MarkRunning は無視され、最初の共有タスクが残る
        [Test]
        public void MarkRunningTwiceIsIgnored()
        {
            var first = new TaskCompletionSource<bool>().Task;
            var second = new TaskCompletionSource<bool>().Task;
            _model.MarkRunning(first);

            Assert.IsFalse(_model.MarkRunning(second));

            Assert.AreEqual(InitializeState.Running, _model.AccState);
            Assert.AreSame(first, _model.AccSharedTask);
        }

        // Running → Succeeded
        [Test]
        public void MarkSucceededFromRunningSetsSucceeded()
        {
            _model.MarkRunning(Task.CompletedTask);

            Assert.IsTrue(_model.MarkSucceeded());

            Assert.AreEqual(InitializeState.Succeeded, _model.AccState);
            Assert.IsNull(_model.AccFailure);
        }

        // Running → Failed で、失敗の例外を記録する
        [Test]
        public void MarkFailedFromRunningSetsFailedAndFailure()
        {
            var exception = new AddressableInitializeException("失敗", new Exception());
            _model.MarkRunning(Task.CompletedTask);

            Assert.IsTrue(_model.MarkFailed(exception));

            Assert.AreEqual(InitializeState.Failed, _model.AccState);
            Assert.AreSame(exception, _model.AccFailure);
        }

        // NotStarted からは、成功にも失敗にも遷移しない
        [Test]
        public void MarkSucceededOrFailedFromNotStartedIsIgnored()
        {
            Assert.IsFalse(_model.MarkSucceeded());
            Assert.IsFalse(_model.MarkFailed(new AddressableInitializeException("失敗", null)));

            Assert.AreEqual(InitializeState.NotStarted, _model.AccState);
            Assert.IsNull(_model.AccFailure);
        }

        // Succeeded は終端。Failed にも Running にも変わらない
        [Test]
        public void SucceededIsTerminal()
        {
            _model.MarkRunning(Task.CompletedTask);
            _model.MarkSucceeded();

            Assert.IsFalse(_model.MarkFailed(new AddressableInitializeException("失敗", null)));
            Assert.IsFalse(_model.MarkSucceeded());
            Assert.IsFalse(_model.MarkRunning(Task.CompletedTask));

            Assert.AreEqual(InitializeState.Succeeded, _model.AccState);
            Assert.IsNull(_model.AccFailure);
        }

        // Failed は終端。Succeeded にも Running にも変わらず、最初の失敗の例外が残る
        [Test]
        public void FailedIsTerminalAndKeepsFirstFailure()
        {
            var first = new AddressableInitializeException("最初の失敗", null);
            _model.MarkRunning(Task.CompletedTask);
            _model.MarkFailed(first);

            Assert.IsFalse(_model.MarkSucceeded());
            Assert.IsFalse(_model.MarkFailed(new AddressableInitializeException("2回目の失敗", null)));
            Assert.IsFalse(_model.MarkRunning(Task.CompletedTask));

            Assert.AreEqual(InitializeState.Failed, _model.AccState);
            Assert.AreSame(first, _model.AccFailure);
        }

        // null の引数は、プログラムの誤りとして例外にする
        [Test]
        public void NullArgumentsThrow()
        {
            Assert.Throws<ArgumentNullException>(() => _model.MarkRunning(null));

            _model.MarkRunning(Task.CompletedTask);
            Assert.Throws<ArgumentNullException>(() => _model.MarkFailed(null));
            Assert.AreEqual(InitializeState.Running, _model.AccState); // 例外のときは状態を変えない
        }

        // 登録した通知先すべてに、同じ値が届く
        [Test]
        public void ReportProgressReachesAllTargets()
        {
            var first = new RecordingProgress();
            var second = new RecordingProgress();
            _model.AddProgressTarget(first);
            _model.AddProgressTarget(second);

            _model.ReportProgress(0.5f);

            CollectionAssert.AreEqual(new[] { 0.5f }, first.Values);
            CollectionAssert.AreEqual(new[] { 0.5f }, second.Values);
        }

        // 外した通知先には、以降の値が届かない
        [Test]
        public void RemovedTargetNoLongerReceivesProgress()
        {
            var removed = new RecordingProgress();
            var kept = new RecordingProgress();
            _model.AddProgressTarget(removed);
            _model.AddProgressTarget(kept);

            _model.RemoveProgressTarget(removed);
            _model.ReportProgress(1f);

            Assert.IsEmpty(removed.Values);
            CollectionAssert.AreEqual(new[] { 1f }, kept.Values);
        }

        // 同じ通知先が2回登録されていれば、1回の解除では1つだけ外れる（2つの呼び出し元が同じ IProgress を渡した場合）
        [Test]
        public void RemoveProgressTargetRemovesOnlyOneOfDuplicates()
        {
            var shared = new RecordingProgress();
            _model.AddProgressTarget(shared);
            _model.AddProgressTarget(shared);

            _model.RemoveProgressTarget(shared);
            _model.ReportProgress(0.25f);

            CollectionAssert.AreEqual(new[] { 0.25f }, shared.Values);
        }

        // null の通知先は登録も解除も無視し、通知で例外にならない
        [Test]
        public void NullProgressTargetIsIgnored()
        {
            _model.AddProgressTarget(null);
            _model.RemoveProgressTarget(null);

            Assert.DoesNotThrow(() => _model.ReportProgress(1f));
        }

        // 通知先が無いときの通知は何も起きない
        [Test]
        public void ReportProgressWithoutTargetsDoesNothing()
        {
            Assert.DoesNotThrow(() => _model.ReportProgress(0.1f));
        }

        // 通知先が例外を投げても、例外は外に出ず、後ろの通知先にも値が届く（例外はログになる）
        [Test]
        public void ReportProgressContinuesAfterThrowingTarget()
        {
            var after = new RecordingProgress();
            _model.AddProgressTarget(new ThrowingProgress());
            _model.AddProgressTarget(after);
            LogAssert.Expect(LogType.Exception, new Regex("通知先の例外"));

            Assert.DoesNotThrow(() => _model.ReportProgress(0.5f));

            CollectionAssert.AreEqual(new[] { 0.5f }, after.Values);
        }

        private sealed class ThrowingProgress : IProgress<float>
        {
            public void Report(float value) => throw new Exception("通知先の例外");
        }

        // 値を記録するだけの IProgress（Progress<T> は SynchronizationContext に投げるため使わない）
        private sealed class RecordingProgress : IProgress<float>
        {
            internal readonly System.Collections.Generic.List<float> Values = new();

            public void Report(float value) => Values.Add(value);
        }
    }
}
