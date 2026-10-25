using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Download;
using UnityEngine.TestTools;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// DownloadStatusModel の遷移と通知。取得した値がコピーであること、最後の通知が1回だけになること、通知先の解除と例外の受け止めを確認する。
    /// </summary>
    internal sealed class DownloadStatusModelTests
    {
        private DownloadStatusModel _model;
        private RecordingDownloadProgress _progress;

        [SetUp]
        public void SetUp()
        {
            _model = new DownloadStatusModel();
            _progress = new RecordingDownloadProgress();
        }

        // ---- 初期状態 ----

        // 何も処理していない状態は None で、実行中ではない（default の DownloadStatus と同じ）
        [Test]
        public void NewModelIsNoneAndNotRunning()
        {
            Assert.AreEqual(DownloadPhase.None, _model.AccStatus.AccPhase);
            Assert.IsFalse(_model.AccIsRunning);
            Assert.IsFalse(_model.AccStatus.AccIsDownloading);
            Assert.IsFalse(_model.AccStatus.AccIsBusy);
            Assert.AreEqual(0f, _model.AccStatus.AccPercent);
        }

        // ---- TryBegin ----

        // 開始すると、段階が first_phase になり、実行中になり、開始が通知先へ1回通知される
        [Test]
        public void TryBeginSetsPhaseRunningAndNotifiesOnce()
        {
            Assert.IsTrue(_model.TryBegin(DownloadPhase.CheckingCatalog, _progress));

            Assert.AreEqual(DownloadPhase.CheckingCatalog, _model.AccStatus.AccPhase);
            Assert.IsTrue(_model.AccIsRunning);
            Assert.IsTrue(_model.AccStatus.AccIsBusy);
            Assert.AreEqual(1, _progress.Received.Count);
            Assert.AreEqual(DownloadPhase.CheckingCatalog, _progress.Last.AccPhase);
        }

        // 通知先が null でも開始できる（HasUpdateAsync / CheckUpdateAsync は通知先を持たない）
        [Test]
        public void TryBeginWithoutProgressStillUpdatesStatus()
        {
            Assert.IsTrue(_model.TryBegin(DownloadPhase.CalculatingSize, null));

            Assert.AreEqual(DownloadPhase.CalculatingSize, _model.AccStatus.AccPhase);
            Assert.DoesNotThrow(() => _model.SetBytes(1, 2));
        }

        // 実行中にもう一度開始しようとすると false になり、実行中の処理の状態・通知先は変わらない
        [Test]
        public void TryBeginWhileRunningReturnsFalseAndKeepsState()
        {
            _model.TryBegin(DownloadPhase.Downloading, _progress);
            _model.SetBytes(30, 100);
            var other = new RecordingDownloadProgress();

            Assert.IsFalse(_model.TryBegin(DownloadPhase.CheckingCatalog, other));

            Assert.AreEqual(DownloadPhase.Downloading, _model.AccStatus.AccPhase);
            Assert.AreEqual(30L, _model.AccStatus.AccDownloadedBytes);
            Assert.AreEqual(0, other.Received.Count, "失敗した開始の通知先には通知しないはず");
            _model.SetBytes(40, 100);
            Assert.AreEqual(0, other.Received.Count, "失敗した開始の通知先は登録されないはず");
            Assert.AreEqual(40L, _progress.Last.AccDownloadedBytes);
        }

        // 処理中ではない段階では開始できない
        [TestCase(DownloadPhase.None)]
        [TestCase(DownloadPhase.Completed)]
        [TestCase(DownloadPhase.Failed)]
        [TestCase(DownloadPhase.Canceled)]
        public void TryBeginWithNonBusyPhaseThrows(DownloadPhase phase)
        {
            Assert.Throws<ArgumentException>(() => _model.TryBegin(phase, _progress));
            Assert.IsFalse(_model.AccIsRunning);
        }

        // 終わった後の次の開始で、バイト数・リトライ回数が 0 に戻り、通知先が新しいものに置き換わる
        [Test]
        public void TryBeginAfterEndResetsValuesAndReplacesTarget()
        {
            _model.TryBegin(DownloadPhase.Downloading, _progress);
            _model.SetBytes(50, 100);
            _model.IncrementRetry();
            _model.MarkFailed();
            _model.End();
            var next = new RecordingDownloadProgress();

            Assert.IsTrue(_model.TryBegin(DownloadPhase.CalculatingSize, next));

            var status = _model.AccStatus;
            Assert.AreEqual(DownloadPhase.CalculatingSize, status.AccPhase);
            Assert.AreEqual(0L, status.AccDownloadedBytes);
            Assert.AreEqual(0L, status.AccTotalBytes);
            Assert.AreEqual(0, status.AccRetryCount);
            Assert.AreEqual(1, next.Received.Count);
            var count_before = _progress.Received.Count;
            _model.SetBytes(1, 2);
            Assert.AreEqual(count_before, _progress.Received.Count, "前の処理の通知先には、もう通知しないはず");
        }

        // ---- MarkPhase ----

        // 段階を進めると通知され、リトライ回数が 0 に戻る。同じ段階では通知しない
        [Test]
        public void MarkPhaseChangesPhaseResetsRetryAndNotifiesOnlyOnChange()
        {
            _model.TryBegin(DownloadPhase.CheckingCatalog, _progress);
            _model.IncrementRetry();
            _model.IncrementRetry();
            Assert.AreEqual(2, _model.AccStatus.AccRetryCount);
            var count = _progress.Received.Count;

            Assert.IsTrue(_model.MarkPhase(DownloadPhase.CheckingCatalog));
            Assert.AreEqual(count, _progress.Received.Count, "同じ段階では通知しないはず");
            Assert.AreEqual(2, _model.AccStatus.AccRetryCount, "同じ段階ではリトライ回数を戻さないはず");

            Assert.IsTrue(_model.MarkPhase(DownloadPhase.UpdatingCatalog));
            Assert.AreEqual(count + 1, _progress.Received.Count);
            Assert.AreEqual(DownloadPhase.UpdatingCatalog, _model.AccStatus.AccPhase);
            Assert.AreEqual(0, _model.AccStatus.AccRetryCount, "段階が進むとリトライ回数は 0 に戻るはず");
        }

        // 実行中でなければ段階を進められない
        [Test]
        public void MarkPhaseWhenNotRunningReturnsFalse()
        {
            Assert.IsFalse(_model.MarkPhase(DownloadPhase.Downloading));

            Assert.AreEqual(DownloadPhase.None, _model.AccStatus.AccPhase);
        }

        // 結果の段階や None は MarkPhase では指定できない（結果は Mark* で記録する）
        [TestCase(DownloadPhase.None)]
        [TestCase(DownloadPhase.Completed)]
        [TestCase(DownloadPhase.Failed)]
        [TestCase(DownloadPhase.Canceled)]
        public void MarkPhaseWithNonBusyPhaseThrows(DownloadPhase phase)
        {
            _model.TryBegin(DownloadPhase.CheckingCatalog, _progress);

            Assert.Throws<ArgumentException>(() => _model.MarkPhase(phase));
        }

        // ---- SetBytes / IncrementRetry ----

        // バイト数を記録すると、割合と一緒に通知される。負の値は 0 にそろえる
        [Test]
        public void SetBytesUpdatesBytesAndPercentAndNotifies()
        {
            _model.TryBegin(DownloadPhase.Downloading, _progress);

            Assert.IsTrue(_model.SetBytes(25, 100));

            var status = _model.AccStatus;
            Assert.AreEqual(25L, status.AccDownloadedBytes);
            Assert.AreEqual(100L, status.AccTotalBytes);
            Assert.AreEqual(0.25f, status.AccPercent, 0.0001f);
            Assert.AreEqual(25L, _progress.Last.AccDownloadedBytes);
            Assert.IsTrue(_model.SetBytes(-5, -1));
            Assert.AreEqual(0L, _model.AccStatus.AccDownloadedBytes);
            Assert.AreEqual(0L, _model.AccStatus.AccTotalBytes);
        }

        [Test]
        public void IncrementRetryIncreasesCountAndNotifies()
        {
            _model.TryBegin(DownloadPhase.Downloading, _progress);
            var count = _progress.Received.Count;

            Assert.IsTrue(_model.IncrementRetry());
            Assert.IsTrue(_model.IncrementRetry());

            Assert.AreEqual(2, _model.AccStatus.AccRetryCount);
            Assert.AreEqual(count + 2, _progress.Received.Count);
            Assert.AreEqual(2, _progress.Last.AccRetryCount);
        }

        // ---- 結果の記録と最後の通知 ----

        // 成功を記録すると、割合が 1 になり、ダウンロード済みが合計に合う。通知は1回
        [Test]
        public void MarkCompletedSetsCompletedFullPercentAndNotifiesOnce()
        {
            _model.TryBegin(DownloadPhase.Downloading, _progress);
            _model.SetBytes(80, 100);
            var count = _progress.Received.Count;

            Assert.IsTrue(_model.MarkCompleted());

            var status = _model.AccStatus;
            Assert.AreEqual(DownloadPhase.Completed, status.AccPhase);
            Assert.AreEqual(1f, status.AccPercent);
            Assert.AreEqual(100L, status.AccDownloadedBytes);
            Assert.IsFalse(status.AccIsBusy);
            Assert.AreEqual(count + 1, _progress.Received.Count);
            Assert.AreEqual(DownloadPhase.Completed, _progress.Last.AccPhase);
        }

        // 合計バイト数が 0 のまま（確認・判定・0 件のダウンロード）でも、Completed の割合は 1
        [Test]
        public void MarkCompletedWithoutBytesIsFullPercent()
        {
            _model.TryBegin(DownloadPhase.CalculatingSize, _progress);

            _model.MarkCompleted();

            Assert.AreEqual(DownloadPhase.Completed, _model.AccStatus.AccPhase);
            Assert.AreEqual(1f, _model.AccStatus.AccPercent);
        }

        // 失敗・キャンセルは、バイト数を残したまま、その結果の段階になる
        [Test]
        public void MarkFailedKeepsBytes()
        {
            _model.TryBegin(DownloadPhase.Downloading, _progress);
            _model.SetBytes(40, 100);
            _model.IncrementRetry();

            Assert.IsTrue(_model.MarkFailed());

            var status = _model.AccStatus;
            Assert.AreEqual(DownloadPhase.Failed, status.AccPhase);
            Assert.AreEqual(40L, status.AccDownloadedBytes);
            Assert.AreEqual(1, status.AccRetryCount);
            Assert.AreEqual(DownloadPhase.Failed, _progress.Last.AccPhase);
        }

        [Test]
        public void MarkCanceledKeepsBytes()
        {
            _model.TryBegin(DownloadPhase.Downloading, _progress);
            _model.SetBytes(40, 100);

            Assert.IsTrue(_model.MarkCanceled());

            Assert.AreEqual(DownloadPhase.Canceled, _model.AccStatus.AccPhase);
            Assert.AreEqual(40L, _model.AccStatus.AccDownloadedBytes);
            Assert.AreEqual(DownloadPhase.Canceled, _progress.Last.AccPhase);
        }

        // 結果が記録された後は、どの遷移も無視される（最後の通知は1回だけ）
        [Test]
        public void TransitionsAfterResultAreIgnoredAndDoNotNotify()
        {
            _model.TryBegin(DownloadPhase.Downloading, _progress);
            _model.MarkFailed();
            var count = _progress.Received.Count;

            Assert.IsFalse(_model.MarkCanceled(), "Dispose と失敗が重なっても、最後の通知は1回");
            Assert.IsFalse(_model.MarkCompleted());
            Assert.IsFalse(_model.MarkFailed());
            Assert.IsFalse(_model.MarkPhase(DownloadPhase.CalculatingSize));
            Assert.IsFalse(_model.SetBytes(1, 2));
            Assert.IsFalse(_model.IncrementRetry());

            Assert.AreEqual(DownloadPhase.Failed, _model.AccStatus.AccPhase);
            Assert.AreEqual(count, _progress.Received.Count);
        }

        // 実行中でなければ、結果は記録できない
        [Test]
        public void MarkResultWhenNotRunningReturnsFalse()
        {
            Assert.IsFalse(_model.MarkCompleted());
            Assert.IsFalse(_model.MarkFailed());
            Assert.IsFalse(_model.MarkCanceled());

            Assert.AreEqual(DownloadPhase.None, _model.AccStatus.AccPhase);
        }

        // ---- End ----

        // 終えると、通知先が外れ、実行中が解除される。結果は次の処理が始まるまで残る
        [Test]
        public void EndClearsTargetAndRunningButKeepsResult()
        {
            _model.TryBegin(DownloadPhase.Downloading, _progress);
            _model.MarkCompleted();
            var count = _progress.Received.Count;

            _model.End();

            Assert.IsFalse(_model.AccIsRunning);
            Assert.AreEqual(DownloadPhase.Completed, _model.AccStatus.AccPhase, "結果は次の処理が始まるまで残るはず");
            Assert.IsFalse(_model.SetBytes(1, 2), "終えた後は遷移できないはず");
            Assert.AreEqual(count, _progress.Received.Count, "外れた後の通知先には届かないはず");
        }

        // 最後の通知の中で次の処理を始めようとしても、終えるまでは始められない（通知先を、次の処理のものと取り違えて外さないため）
        [Test]
        public void TryBeginInsideFinalNotificationIsRejectedUntilEnd()
        {
            _model.TryBegin(DownloadPhase.Downloading, _progress);
            var next = new RecordingDownloadProgress();
            var started_inside = true;
            _progress.OnReport = status =>
            {
                if (status.AccPhase == DownloadPhase.Completed)
                    started_inside = _model.TryBegin(DownloadPhase.CheckingCatalog, next);
            };

            _model.MarkCompleted();
            _model.End();

            Assert.IsFalse(started_inside, "最後の通知の中では、次の処理を始められないはず");
            Assert.IsFalse(_model.AccIsRunning);
            Assert.AreEqual(0, next.Received.Count);
        }

        // ---- 値のコピー ----

        // 取得した状態と、通知で届いた値は、その後の遷移で変わらない
        [Test]
        public void CapturedStatusAndReceivedValuesDoNotChange()
        {
            _model.TryBegin(DownloadPhase.Downloading, _progress);
            _model.SetBytes(10, 100);
            var captured = _model.AccStatus;
            var received = _progress.Last;

            _model.SetBytes(90, 100);
            _model.MarkCompleted();

            Assert.AreEqual(DownloadPhase.Downloading, captured.AccPhase);
            Assert.AreEqual(10L, captured.AccDownloadedBytes);
            Assert.AreEqual(0.1f, captured.AccPercent, 0.0001f);
            Assert.AreEqual(10L, received.AccDownloadedBytes);
            Assert.AreEqual(DownloadPhase.Downloading, _progress.Received[_progress.Received.Count - 3].AccPhase);
        }

        // ---- 通知先の例外 ----

        // 通知先が例外を投げても、遷移は止まらず、状態は更新され、ログに出る
        [Test]
        public void ThrowingProgressTargetDoesNotBreakTransitions()
        {
            _progress.ThrowOnReport = new Exception("通知先の例外");
            LogAssert.Expect(UnityEngine.LogType.Exception, new Regex("通知先の例外"));

            Assert.DoesNotThrow(() => _model.TryBegin(DownloadPhase.Downloading, _progress));

            LogAssert.Expect(UnityEngine.LogType.Exception, new Regex("通知先の例外"));
            Assert.DoesNotThrow(() => _model.MarkCompleted());
            Assert.AreEqual(DownloadPhase.Completed, _model.AccStatus.AccPhase);
        }
    }
}
