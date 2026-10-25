using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Download;
using UnityEngine;
using UnityEngine.TestTools;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// DownloadController の単体テスト。偽の Service とゲートで、確認・判定・ダウンロードの流れ・リトライ・キャンセル・最後の通知・初期化待ち・Dispose を確認する。
    /// 戻り値が UniTask / Task のどちらでも書けるよう、待機は await、例外は TestAsync.Catch（async ラムダ）で扱う。
    /// </summary>
    internal sealed class DownloadControllerTests
    {
        private const string LABEL = "test-label";

        private FakeAddressablesDownloadService _service;
        private FakeInitializationGate _gate;
        private DownloadStatusModel _model;
        private RecordingDownloadProgress _progress;
        private DownloadController _controller;

        [SetUp]
        public void SetUp()
        {
            _service = new FakeAddressablesDownloadService();
            _gate = new FakeInitializationGate();
            _model = new DownloadStatusModel();
            _progress = new RecordingDownloadProgress();
            _controller = CreateController(CatalogUpdatePolicy.CheckAndUpdate);
        }

        [TearDown]
        public void TearDown()
        {
            _controller?.Dispose();
        }

        // リトライの待機は 0ms（テストを待たせない）。リトライ回数は既定で 2
        private DownloadController CreateController(
            CatalogUpdatePolicy policy,
            string[] labels = null,
            int max_retry = 2,
            bool auto_clean_bundle_cache = false,
            int retry_delay_ms = 0)
        {
            _controller?.Dispose(); // SetUp で作った Controller の Dispose が、Service の記録に混ざらないようにする
            _service.ResetReleaseCallCount();
            var settings = new AddressableDownloadSettings(policy, labels ?? new[] { LABEL }, max_retry, retry_delay_ms, auto_clean_bundle_cache);
            _controller = new DownloadController(settings, _model, _service, _gate);
            return _controller;
        }

        // 状態がそうなるまで待つ（継続がスレッドプールで走る場合を吸収する）
        private static Task WaitUntil(Func<bool> condition) => TestAsync.WaitUntil(condition);

        private static List<DownloadPhase> Phases(RecordingDownloadProgress progress)
        {
            var phases = new List<DownloadPhase>();
            foreach (var status in progress.Received)
            {
                if (phases.Count == 0 || phases[phases.Count - 1] != status.AccPhase)
                    phases.Add(status.AccPhase);
            }
            return phases;
        }

        // ================= HasUpdateAsync =================

        // カタログに更新があれば、サイズを計算せずに true
        [Test]
        public async Task HasUpdateTrueWithoutCalculatingSizeWhenCatalogHasUpdate()
        {
            _service.CatalogIds.Add("catalog-1");

            var has_update = await _controller.HasUpdateAsync(null, CancellationToken.None);

            Assert.IsTrue(has_update);
            Assert.AreEqual(0, _service.Size.CallCount, "更新のあるカタログがあれば、サイズは計算しないはず");
            Assert.AreEqual(0, _service.Update.CallCount, "判定ではカタログを更新しないはず");
            Assert.AreEqual(DownloadPhase.Completed, _controller.AccStatus.AccPhase);
        }

        // カタログに更新が無くても、ダウンロードが必要なバンドルがあれば true
        [Test]
        public async Task HasUpdateTrueWhenBundlesExistWithoutCatalogUpdate()
        {
            _service.SizeInfo = new DownloadSizeInfo(2, 100);

            var has_update = await _controller.HasUpdateAsync(null, CancellationToken.None);

            Assert.IsTrue(has_update);
            CollectionAssert.AreEqual(new[] { "Check", "Size" }, _service.CallLog);
        }

        [Test]
        public async Task HasUpdateFalseWhenNothingToUpdate()
        {
            var has_update = await _controller.HasUpdateAsync(null, CancellationToken.None);

            Assert.IsFalse(has_update);
            Assert.AreEqual(DownloadPhase.Completed, _controller.AccStatus.AccPhase);
            Assert.IsFalse(_controller.AccStatus.AccIsBusy);
        }

        // どの方針でも、判定では UpdateCatalogs を呼ばない
        [TestCase(CatalogUpdatePolicy.CheckOnly)]
        [TestCase(CatalogUpdatePolicy.CheckAndUpdate)]
        public async Task HasUpdateNeverUpdatesCatalogs(CatalogUpdatePolicy policy)
        {
            CreateController(policy);
            _service.CatalogIds.Add("catalog-1");

            await _controller.HasUpdateAsync(null, CancellationToken.None);

            Assert.AreEqual(0, _service.Update.CallCount);
        }

        // 方針が None なら、カタログを確認せず、今のカタログのバンドルだけを数える
        [Test]
        public async Task HasUpdateWithPolicyNoneSkipsCatalogCheck()
        {
            CreateController(CatalogUpdatePolicy.None);
            _service.CatalogIds.Add("catalog-1");
            _service.SizeInfo = new DownloadSizeInfo(1, 10);

            var has_update = await _controller.HasUpdateAsync(null, CancellationToken.None);

            Assert.IsTrue(has_update);
            Assert.AreEqual(0, _service.Check.CallCount);
            CollectionAssert.AreEqual(new[] { "Size" }, _service.CallLog);
        }

        // ================= CheckUpdateAsync =================

        // CheckAndUpdate：確認で得たカタログ ID を明示して更新し、件数と容量を返す
        [Test]
        public async Task CheckUpdateAndUpdateUpdatesWithExplicitIdsAndReturnsCounts()
        {
            CreateController(CatalogUpdatePolicy.CheckAndUpdate, auto_clean_bundle_cache: true);
            _service.CatalogIds.AddRange(new[] { "catalog-1", "catalog-2" });
            _service.SizeInfo = new DownloadSizeInfo(3, 300);

            var result = await _controller.CheckUpdateAsync(null, CancellationToken.None);

            Assert.AreEqual(2, result.AccCatalogUpdateCount);
            Assert.AreEqual(3, result.AccBundleCount);
            Assert.AreEqual(300L, result.AccTotalBytes);
            Assert.IsFalse(result.AccIsCatalogUpdatePending);
            Assert.IsTrue(result.AccHasDownload);
            CollectionAssert.AreEqual(new[] { "Check", "Update", "Size" }, _service.CallLog);
            CollectionAssert.AreEqual(new[] { "catalog-1", "catalog-2" }, _service.LastUpdatedCatalogIds, "確認で得た ID を明示して渡すはず（null ではない）");
            Assert.IsTrue(_service.LastAutoClean, "設定の autoCleanBundleCache を渡すはず");
            Assert.AreEqual(DownloadPhase.Completed, _controller.AccStatus.AccPhase);
        }

        // CheckAndUpdate でも、更新のあるカタログが無ければ更新しない
        [Test]
        public async Task CheckUpdateAndUpdateWithoutCatalogUpdatesSkipsUpdate()
        {
            var result = await _controller.CheckUpdateAsync(null, CancellationToken.None);

            Assert.AreEqual(0, result.AccCatalogUpdateCount);
            Assert.AreEqual(0, _service.Update.CallCount);
            Assert.IsFalse(result.AccHasDownload);
        }

        // CheckOnly：更新はせず、保留中であることを返す
        [Test]
        public async Task CheckUpdateCheckOnlyDoesNotUpdateAndReportsPending()
        {
            CreateController(CatalogUpdatePolicy.CheckOnly);
            _service.CatalogIds.Add("catalog-1");

            var result = await _controller.CheckUpdateAsync(null, CancellationToken.None);

            Assert.AreEqual(1, result.AccCatalogUpdateCount);
            Assert.IsTrue(result.AccIsCatalogUpdatePending);
            Assert.AreEqual(0, _service.Update.CallCount);
        }

        [Test]
        public async Task CheckUpdateCheckOnlyWithoutUpdatesIsNotPending()
        {
            CreateController(CatalogUpdatePolicy.CheckOnly);

            var result = await _controller.CheckUpdateAsync(null, CancellationToken.None);

            Assert.AreEqual(0, result.AccCatalogUpdateCount);
            Assert.IsFalse(result.AccIsCatalogUpdatePending);
        }

        // None：カタログを確認も更新もせず、今のカタログで件数と容量だけを数える
        [Test]
        public async Task CheckUpdatePolicyNoneSkipsCatalogsAndCountsOnly()
        {
            CreateController(CatalogUpdatePolicy.None);
            _service.CatalogIds.Add("catalog-1");
            _service.SizeInfo = new DownloadSizeInfo(1, 50);

            var result = await _controller.CheckUpdateAsync(null, CancellationToken.None);

            Assert.AreEqual(0, result.AccCatalogUpdateCount);
            Assert.IsFalse(result.AccIsCatalogUpdatePending);
            Assert.AreEqual(1, result.AccBundleCount);
            Assert.AreEqual(50L, result.AccTotalBytes);
            CollectionAssert.AreEqual(new[] { "Size" }, _service.CallLog);
        }

        // 段階が、確認 → 更新 → サイズ計算 → 完了の順に進む（止めて、各段階の状態を確かめる）
        [Test]
        public async Task CheckUpdatePhasesProgressInOrder()
        {
            _service.CatalogIds.Add("catalog-1");
            var check_hold = _service.Check.HoldNext();
            var update_hold = _service.Update.HoldNext();
            var size_hold = _service.Size.HoldNext();

            var pending = _controller.CheckUpdateAsync(null, CancellationToken.None);

            await WaitUntil(() => _service.Check.CallCount == 1);
            Assert.AreEqual(DownloadPhase.CheckingCatalog, _controller.AccStatus.AccPhase);
            Assert.IsTrue(_controller.AccStatus.AccIsBusy);

            check_hold.SetResult(true);
            await WaitUntil(() => _service.Update.CallCount == 1);
            Assert.AreEqual(DownloadPhase.UpdatingCatalog, _controller.AccStatus.AccPhase);

            update_hold.SetResult(true);
            await WaitUntil(() => _service.Size.CallCount == 1);
            Assert.AreEqual(DownloadPhase.CalculatingSize, _controller.AccStatus.AccPhase);

            size_hold.SetResult(true);
            await pending;
            Assert.AreEqual(DownloadPhase.Completed, _controller.AccStatus.AccPhase);
        }

        // ================= DownloadAsync：流れ・進捗・最後の通知 =================

        // 段階が CalculatingSize → Downloading → Completed の順に進み、Tick でバイト数が更新され、最後に割合 1 が届く
        [Test]
        public async Task DownloadReportsPhasesBytesAndFinalCompleted()
        {
            _service.SizeInfo = new DownloadSizeInfo(2, 100);
            var download_hold = _service.Download.HoldNext();

            var pending = _controller.DownloadAsync(null, _progress, CancellationToken.None);
            await WaitUntil(() => _service.Download.CallCount == 1);

            Assert.AreEqual(DownloadPhase.Downloading, _controller.AccStatus.AccPhase);
            Assert.IsTrue(_controller.AccStatus.AccIsDownloading);
            Assert.AreEqual(100L, _controller.AccStatus.AccTotalBytes);

            _service.DownloadedBytes = 40;
            _service.TotalBytes = 100;
            _controller.Tick(0.016f);
            Assert.AreEqual(40L, _controller.AccStatus.AccDownloadedBytes);
            Assert.AreEqual(0.4f, _controller.AccStatus.AccPercent, 0.0001f);
            Assert.AreEqual(40L, _progress.Last.AccDownloadedBytes, "Tick のたびに通知するはず");

            download_hold.SetResult(true);
            await pending;

            var final = _controller.AccStatus;
            Assert.AreEqual(DownloadPhase.Completed, final.AccPhase);
            Assert.AreEqual(1f, final.AccPercent);
            Assert.AreEqual(DownloadPhase.Completed, _progress.Last.AccPhase);
            CollectionAssert.AreEqual(
                new[] { DownloadPhase.CalculatingSize, DownloadPhase.Downloading, DownloadPhase.Completed },
                Phases(_progress));
            Assert.AreEqual(1, _progress.Received.Count(s => s.AccPhase == DownloadPhase.Completed), "最後の通知は1回だけのはず");
        }

        // ダウンロードが必要なバンドルが無ければ、ダウンロードを始めずに Completed で終わる
        [Test]
        public async Task DownloadWithZeroBundlesCompletesWithoutStartingDownload()
        {
            await _controller.DownloadAsync(null, _progress, CancellationToken.None);

            Assert.AreEqual(0, _service.Download.CallCount);
            Assert.AreEqual(DownloadPhase.Completed, _controller.AccStatus.AccPhase);
            Assert.AreEqual(DownloadPhase.Completed, _progress.Last.AccPhase);
        }

        // Tick は、ダウンロード中だけ働く（それ以外では通知もバイト数の更新もしない）
        [Test]
        public async Task TickOnlyUpdatesWhileDownloading()
        {
            _service.DownloadedBytes = 50;
            _service.TotalBytes = 100;
            _controller.Tick(0.016f);
            Assert.AreEqual(DownloadPhase.None, _controller.AccStatus.AccPhase, "処理していないときは何もしないはず");

            _service.SizeInfo = new DownloadSizeInfo(1, 100);
            var size_hold = _service.Size.HoldNext();
            var pending = _controller.DownloadAsync(null, _progress, CancellationToken.None);
            await WaitUntil(() => _service.Size.CallCount == 1);
            var count = _progress.Received.Count;

            _controller.Tick(0.016f);

            Assert.AreEqual(DownloadPhase.CalculatingSize, _controller.AccStatus.AccPhase);
            Assert.AreEqual(0L, _controller.AccStatus.AccDownloadedBytes, "サイズ計算中はバイト数を更新しないはず");
            Assert.AreEqual(count, _progress.Received.Count, "ダウンロード中でなければ通知しないはず");

            size_hold.SetResult(true);
            await pending;
            var count_after = _progress.Received.Count;
            _controller.Tick(0.016f);
            Assert.AreEqual(count_after, _progress.Received.Count, "終わった後は通知しないはず");
        }

        // ダウンロードの開始直後は、handle の合計が 0 のことがある。サイズ計算で確定した合計は、0 に戻さずに維持する
        [Test]
        public async Task TickKeepsConfirmedTotalWhenServiceReportsZeroTotal()
        {
            _service.SizeInfo = new DownloadSizeInfo(1, 100);
            var download_hold = _service.Download.HoldNext();
            var pending = _controller.DownloadAsync(null, _progress, CancellationToken.None);
            await WaitUntil(() => _service.Download.CallCount == 1);
            Assert.AreEqual(100L, _controller.AccStatus.AccTotalBytes, "ダウンロードの開始時には、サイズ計算の合計が入っているはず");

            _service.DownloadedBytes = 0;
            _service.TotalBytes = 0;
            _controller.Tick(0.016f);
            Assert.AreEqual(100L, _controller.AccStatus.AccTotalBytes, "合計が 0 の間は、確定した合計を維持するはず");

            _service.DownloadedBytes = 30;
            _service.TotalBytes = 100;
            _controller.Tick(0.016f);
            Assert.AreEqual(30L, _controller.AccStatus.AccDownloadedBytes);
            Assert.AreEqual(100L, _controller.AccStatus.AccTotalBytes);

            download_hold.SetResult(true);
            await pending;
        }

        // 取得した状態は、その後に処理が進んでも変わらない
        [Test]
        public async Task CapturedStatusDoesNotChangeAfterProcessProgresses()
        {
            _service.SizeInfo = new DownloadSizeInfo(1, 100);
            var download_hold = _service.Download.HoldNext();
            var pending = _controller.DownloadAsync(null, _progress, CancellationToken.None);
            await WaitUntil(() => _service.Download.CallCount == 1);
            _service.DownloadedBytes = 10;
            _service.TotalBytes = 100;
            _controller.Tick(0);
            var captured = _controller.AccStatus;

            _service.DownloadedBytes = 90;
            _controller.Tick(0);
            download_hold.SetResult(true);
            await pending;

            Assert.AreEqual(DownloadPhase.Downloading, captured.AccPhase);
            Assert.AreEqual(10L, captured.AccDownloadedBytes);
        }

        // ================= リトライ・失敗 =================

        // 失敗しても maxRetry 回まではやり直し、リトライのたびに通知される
        [Test]
        public async Task DownloadRetriesFailuresThenSucceeds()
        {
            _service.SizeInfo = new DownloadSizeInfo(1, 100);
            _service.Download.Failure = new Exception("通信エラー");
            _service.Download.FailuresRemaining = 2;

            await _controller.DownloadAsync(null, _progress, CancellationToken.None);

            Assert.AreEqual(3, _service.Download.CallCount);
            Assert.AreEqual(DownloadPhase.Completed, _controller.AccStatus.AccPhase);
            Assert.AreEqual(2, _controller.AccStatus.AccRetryCount);
            CollectionAssert.IsSupersetOf(_progress.Received.Select(s => s.AccRetryCount).ToList(), new[] { 1, 2 });
        }

        // リトライを使い切ると、AddressableDownloadException（段階・回数・元の例外つき）と Failed
        [Test]
        public async Task DownloadExhaustedRetriesThrowsDownloadExceptionAndFailed()
        {
            LogAssert.Expect(LogType.Error, new Regex("リトライ"));
            _service.SizeInfo = new DownloadSizeInfo(1, 100);
            var failure = new Exception("通信エラー");
            _service.Download.Failure = failure;
            _service.Download.FailuresRemaining = 10;

            var exception = await TestAsync.Catch<AddressableDownloadException>(
                async () => await _controller.DownloadAsync(null, _progress, CancellationToken.None));

            Assert.AreEqual(DownloadPhase.Downloading, exception.AccPhase);
            Assert.AreEqual(2, exception.AccRetryCount);
            Assert.AreSame(failure, exception.InnerException);
            Assert.AreEqual(3, _service.Download.CallCount, "最初の1回＋リトライ2回");
            Assert.AreEqual(DownloadPhase.Failed, _controller.AccStatus.AccPhase);
            Assert.AreEqual(DownloadPhase.Failed, _progress.Last.AccPhase);
        }

        // リトライは段階ごとに数える：サイズ計算で使った回数は、ダウンロードのリトライを減らさない
        [Test]
        public async Task RetriesAreCountedPerPhase()
        {
            CreateController(CatalogUpdatePolicy.CheckAndUpdate, max_retry: 1);
            _service.SizeInfo = new DownloadSizeInfo(1, 100);
            _service.Size.Failure = new Exception("サイズ計算の失敗");
            _service.Size.FailuresRemaining = 1;
            _service.Download.Failure = new Exception("ダウンロードの失敗");
            _service.Download.FailuresRemaining = 1;

            await _controller.DownloadAsync(null, _progress, CancellationToken.None);

            Assert.AreEqual(2, _service.Size.CallCount);
            Assert.AreEqual(2, _service.Download.CallCount);
            Assert.AreEqual(DownloadPhase.Completed, _controller.AccStatus.AccPhase);
        }

        // 確認の段階で失敗すると、その段階の AddressableDownloadException になる（リトライ 0 なら、すぐ失敗する）
        [Test]
        public async Task CheckStepFailureThrowsWithCheckingCatalogPhase()
        {
            LogAssert.Expect(LogType.Error, new Regex("リトライ"));
            CreateController(CatalogUpdatePolicy.CheckAndUpdate, max_retry: 0);
            _service.Check.Failure = new Exception("確認の失敗");
            _service.Check.FailuresRemaining = 1;

            var exception = await TestAsync.Catch<AddressableDownloadException>(
                async () => await _controller.CheckUpdateAsync(null, CancellationToken.None));

            Assert.AreEqual(DownloadPhase.CheckingCatalog, exception.AccPhase);
            Assert.AreEqual(0, exception.AccRetryCount);
            Assert.AreEqual(1, _service.Check.CallCount);
            Assert.AreEqual(DownloadPhase.Failed, _controller.AccStatus.AccPhase);
        }

        // カタログの更新が失敗したときも、同じ ID でリトライする
        [Test]
        public async Task UpdateCatalogsFailureRetriesWithSameIds()
        {
            _service.CatalogIds.Add("catalog-1");
            _service.Update.Failure = new Exception("更新の失敗");
            _service.Update.FailuresRemaining = 1;

            await _controller.CheckUpdateAsync(null, CancellationToken.None);

            Assert.AreEqual(2, _service.Update.CallCount);
            CollectionAssert.AreEqual(new[] { "catalog-1" }, _service.LastUpdatedCatalogIds);
        }

        // ================= handle の解放 =================

        // 成功・失敗・キャンセルのどの経路でも、ダウンロードの handle を解放する
        [Test]
        public async Task ReleasesDownloadHandleOnSuccess()
        {
            _service.SizeInfo = new DownloadSizeInfo(1, 100);

            await _controller.DownloadAsync(null, _progress, CancellationToken.None);

            Assert.Greater(_service.ReleaseCallCount, 0);
        }

        [Test]
        public async Task ReleasesDownloadHandleOnFailure()
        {
            LogAssert.Expect(LogType.Error, new Regex("リトライ"));
            _service.SizeInfo = new DownloadSizeInfo(1, 100);
            _service.Download.Failure = new Exception("失敗");
            _service.Download.FailuresRemaining = 10;

            await TestAsync.Catch<AddressableDownloadException>(
                async () => await _controller.DownloadAsync(null, _progress, CancellationToken.None));

            Assert.Greater(_service.ReleaseCallCount, 0);
        }

        // ================= キャンセル =================

        // ct のキャンセルで、OperationCanceledException・Canceled になり、handle を解放し、次の呼び出しができる
        [Test]
        public async Task DownloadCancelledThrowsOperationCanceledAndCanceled()
        {
            _service.SizeInfo = new DownloadSizeInfo(1, 100);
            _service.Download.HoldNext();
            using var cts = new CancellationTokenSource();
            var pending = _controller.DownloadAsync(null, _progress, cts.Token);
            await WaitUntil(() => _service.Download.CallCount == 1);

            cts.Cancel();

            await TestAsync.Catch<OperationCanceledException>(async () => await pending);
            Assert.AreEqual(DownloadPhase.Canceled, _controller.AccStatus.AccPhase);
            Assert.AreEqual(DownloadPhase.Canceled, _progress.Last.AccPhase);
            Assert.Greater(_service.ReleaseCallCount, 0, "待機をやめたら、handle を解放するはず");

            await _controller.DownloadAsync(null, null, CancellationToken.None); // 次の呼び出しができる
            Assert.AreEqual(DownloadPhase.Completed, _controller.AccStatus.AccPhase);
        }

        // 呼ぶ前にキャンセル済みの ct を渡すと、Service を呼ばずに Canceled になる
        [Test]
        public async Task AlreadyCancelledTokenThrowsWithoutCallingService()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await TestAsync.Catch<OperationCanceledException>(async () => await _controller.CheckUpdateAsync(null, cts.Token));

            Assert.AreEqual(0, _service.CallLog.Count);
            Assert.AreEqual(DownloadPhase.Canceled, _controller.AccStatus.AccPhase);
        }

        // カタログの更新中は、ct のキャンセルで待機を打ち切らない（更新は途中で止められず、Canceled を返した後に裏で更新されるのを避けるため）。
        // 更新が終わってから OperationCanceledException・Canceled になり、次の段階（サイズ計算）は始めない
        [Test]
        public async Task CancelDuringUpdatingCatalogWaitsForUpdateThenCanceled()
        {
            _service.CatalogIds.Add("catalog-1");
            var update_hold = _service.Update.HoldNext();
            using var cts = new CancellationTokenSource();
            var finished = false;
            var run = TestAsync.Catch<OperationCanceledException>(async () =>
            {
                try
                {
                    await _controller.CheckUpdateAsync(null, cts.Token);
                }
                finally
                {
                    finished = true;
                }
            });
            await WaitUntil(() => _service.Update.CallCount == 1);

            cts.Cancel();
            await Task.Delay(50);
            Assert.IsFalse(finished, "更新が終わるまでは、呼び出し元の待機を打ち切らないはず");
            Assert.AreEqual(DownloadPhase.UpdatingCatalog, _controller.AccStatus.AccPhase);

            update_hold.SetResult(true);
            await run;

            Assert.AreEqual(DownloadPhase.Canceled, _controller.AccStatus.AccPhase);
            Assert.AreEqual(0, _service.Size.CallCount, "キャンセルされたら、サイズ計算は始めないはず");
            CollectionAssert.AreEqual(new[] { "catalog-1" }, _service.LastUpdatedCatalogIds, "更新は最後まで行われているはず");
            Assert.IsFalse(_model.AccIsRunning);
        }

        // 更新の前にキャンセル済みなら、更新を始めない
        [Test]
        public async Task CancelBeforeUpdateStartsDoesNotCallUpdateCatalogs()
        {
            _service.CatalogIds.Add("catalog-1");
            var check_hold = _service.Check.HoldNext();
            using var cts = new CancellationTokenSource();
            var run = TestAsync.Catch<OperationCanceledException>(async () => await _controller.CheckUpdateAsync(null, cts.Token));
            await WaitUntil(() => _service.Check.CallCount == 1);

            cts.Cancel();
            check_hold.SetResult(true);
            await run;

            Assert.AreEqual(0, _service.Update.CallCount, "キャンセル済みなら、更新を始めないはず");
            Assert.AreEqual(DownloadPhase.Canceled, _controller.AccStatus.AccPhase);
        }

        // カタログの更新中でも、Manager の破棄（Dispose）では、更新の完了を待たずに終わる
        [Test]
        public async Task DisposeDuringUpdatingCatalogEndsWithoutWaitingForUpdate()
        {
            _service.CatalogIds.Add("catalog-1");
            _service.Update.HoldNext();
            var run = TestAsync.Catch<OperationCanceledException>(async () => await _controller.CheckUpdateAsync(null, CancellationToken.None));
            await WaitUntil(() => _service.Update.CallCount == 1);

            _controller.Dispose();

            await run;
            Assert.AreEqual(DownloadPhase.Canceled, _controller.AccStatus.AccPhase);
            Assert.IsFalse(_model.AccIsRunning);
        }

        // カタログの更新が失敗して、リトライの待機中なら、ct のキャンセルですぐに終わる（待機中は Addressables の操作が走っていないため）
        [Test]
        public async Task CancelDuringUpdateRetryDelayEndsImmediately()
        {
            CreateController(CatalogUpdatePolicy.CheckAndUpdate, retry_delay_ms: 60000);
            _service.CatalogIds.Add("catalog-1");
            _service.Update.Failure = new Exception("更新の失敗");
            _service.Update.FailuresRemaining = 1;
            using var cts = new CancellationTokenSource();
            var run = TestAsync.Catch<OperationCanceledException>(async () => await _controller.CheckUpdateAsync(null, cts.Token));
            await WaitUntil(() => _controller.AccStatus.AccRetryCount == 1); // 失敗して、リトライの待機に入った

            cts.Cancel();

            await run; // 60 秒待たずに終わる（待機を打ち切れないと、WaitUntil ではなくテスト全体が止まる）
            Assert.AreEqual(DownloadPhase.Canceled, _controller.AccStatus.AccPhase);
            Assert.AreEqual(1, _service.Update.CallCount, "待機中に打ち切ったら、やり直さないはず");
        }

        // ================= 最後の通知と通知先の解除 =================

        // 最後の通知は、呼び出し元に例外が返るより前に届く（受け取る側が、失敗と区別できる）
        [Test]
        public async Task FinalFailedNotificationIsDeliveredBeforeExceptionReturns()
        {
            LogAssert.Expect(LogType.Error, new Regex("リトライ"));
            _service.SizeInfo = new DownloadSizeInfo(1, 100);
            _service.Download.Failure = new Exception("失敗");
            _service.Download.FailuresRemaining = 10;
            var exception_returned = false;
            var failed_before_return = false;
            _progress.OnReport = status =>
            {
                if (status.AccPhase == DownloadPhase.Failed)
                    failed_before_return = !exception_returned;
            };

            await TestAsync.Catch<AddressableDownloadException>(
                async () => await _controller.DownloadAsync(null, _progress, CancellationToken.None));
            exception_returned = true;

            Assert.IsTrue(failed_before_return, "Failed の通知は、例外が返るより前に届くはず");
            Assert.AreEqual(1, _progress.Received.Count(s => s.AccPhase == DownloadPhase.Failed));
            Assert.AreEqual(DownloadPhase.Failed, _progress.Last.AccPhase, "最後の通知の後には、何も届かないはず");
        }

        // 前の DownloadAsync に渡した IProgress は、次の処理の間に呼ばれない
        [Test]
        public async Task PreviousProgressIsNotCalledByNextProcesses()
        {
            _service.SizeInfo = new DownloadSizeInfo(1, 100);
            await _controller.DownloadAsync(null, _progress, CancellationToken.None);
            var count = _progress.Received.Count;
            var next_progress = new RecordingDownloadProgress();

            await _controller.DownloadAsync(null, next_progress, CancellationToken.None);
            await _controller.CheckUpdateAsync(null, CancellationToken.None);
            await _controller.HasUpdateAsync(null, CancellationToken.None);
            _controller.Tick(0.016f);

            Assert.AreEqual(count, _progress.Received.Count, "前の通知先には、もう届かないはず");
            Assert.Greater(next_progress.Received.Count, 0);
        }

        // 結果は、次の処理が始まるまで残り、次の処理の開始でバイト数・リトライ回数が 0 に戻る
        [Test]
        public async Task ResultRemainsUntilNextProcessStartsThenResets()
        {
            _service.SizeInfo = new DownloadSizeInfo(1, 100);
            _service.Download.Failure = new Exception("失敗");
            _service.Download.FailuresRemaining = 1;
            await _controller.DownloadAsync(null, _progress, CancellationToken.None);
            Assert.AreEqual(DownloadPhase.Completed, _controller.AccStatus.AccPhase);
            Assert.AreEqual(1, _controller.AccStatus.AccRetryCount);

            var hold = _service.Size.HoldNext();
            var pending = _controller.HasUpdateAsync(null, CancellationToken.None);
            await WaitUntil(() => _service.Size.CallCount == 2);

            var status = _controller.AccStatus;
            Assert.AreEqual(DownloadPhase.CalculatingSize, status.AccPhase);
            Assert.AreEqual(0L, status.AccDownloadedBytes);
            Assert.AreEqual(0, status.AccRetryCount);

            hold.SetResult(true);
            await pending;
        }

        // 進捗の通知先が例外を投げても、ダウンロードは止まらず完了する
        [Test]
        public async Task ThrowingProgressDoesNotStopDownload()
        {
            _service.SizeInfo = new DownloadSizeInfo(1, 100);
            _progress.ThrowOnReport = new Exception("通知先の例外");
            LogAssert.ignoreFailingMessages = true; // 通知のたびに、例外がログに出る（回数は問わない）
            try
            {
                await _controller.DownloadAsync(null, _progress, CancellationToken.None);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.AreEqual(DownloadPhase.Completed, _controller.AccStatus.AccPhase);
        }

        // ================= 同時実行 =================

        // 実行中に別の処理を呼ぶと InvalidOperationException。実行中の処理は影響を受けない
        [Test]
        public async Task CallWhileRunningThrowsInvalidOperationAndKeepsRunning()
        {
            _service.SizeInfo = new DownloadSizeInfo(1, 100);
            var hold = _service.Download.HoldNext();
            var pending = _controller.DownloadAsync(null, _progress, CancellationToken.None);
            await WaitUntil(() => _service.Download.CallCount == 1);

            await TestAsync.Catch<InvalidOperationException>(async () => await _controller.CheckUpdateAsync(null, CancellationToken.None));
            await TestAsync.Catch<InvalidOperationException>(async () => await _controller.HasUpdateAsync(null, CancellationToken.None));
            await TestAsync.Catch<InvalidOperationException>(async () => await _controller.DownloadAsync(null, null, CancellationToken.None));

            Assert.AreEqual(DownloadPhase.Downloading, _controller.AccStatus.AccPhase);
            Assert.AreEqual(1, _service.Download.CallCount, "実行中の処理は影響を受けないはず");
            hold.SetResult(true);
            await pending;
            Assert.AreEqual(DownloadPhase.Completed, _controller.AccStatus.AccPhase);
        }

        // ================= Label =================

        // 引数なし（null）なら SO の Label、引数で渡せばそちらが優先される
        [Test]
        public async Task ExplicitLabelsOverrideSettingsLabels()
        {
            CreateController(CatalogUpdatePolicy.None, new[] { "settings-label" });

            await _controller.CheckUpdateAsync(null, CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "settings-label" }, _service.LastSizeLabels);

            await _controller.CheckUpdateAsync(new[] { "explicit-label" }, CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "explicit-label" }, _service.LastSizeLabels);
        }

        // 引数なしで SO の Label が空なら InvalidOperationException。状態は変えず、Service も呼ばない
        [Test]
        public async Task NoArgumentWithEmptySettingsLabelsThrowsInvalidOperationWithoutChangingState()
        {
            CreateController(CatalogUpdatePolicy.CheckAndUpdate, new string[0]);

            await TestAsync.Catch<InvalidOperationException>(async () => await _controller.HasUpdateAsync(null, CancellationToken.None));
            await TestAsync.Catch<InvalidOperationException>(async () => await _controller.CheckUpdateAsync(null, CancellationToken.None));
            await TestAsync.Catch<InvalidOperationException>(async () => await _controller.DownloadAsync(null, _progress, CancellationToken.None));

            Assert.AreEqual(DownloadPhase.None, _controller.AccStatus.AccPhase);
            Assert.AreEqual(0, _progress.Received.Count);
            Assert.AreEqual(0, _service.CallLog.Count);
        }

        // 引数の Label が空・空文字を含むなら ArgumentException。状態は変えない
        [Test]
        public async Task InvalidExplicitLabelsThrowArgumentWithoutChangingState()
        {
            await TestAsync.Catch<ArgumentException>(async () => await _controller.CheckUpdateAsync(new string[0], CancellationToken.None));
            await TestAsync.Catch<ArgumentException>(async () => await _controller.CheckUpdateAsync(new[] { "ok", "" }, CancellationToken.None));
            await TestAsync.Catch<ArgumentException>(async () => await _controller.DownloadAsync(new[] { null, "ok" }, _progress, CancellationToken.None));

            Assert.AreEqual(DownloadPhase.None, _controller.AccStatus.AccPhase);
            Assert.AreEqual(0, _progress.Received.Count);
            Assert.AreEqual(0, _service.CallLog.Count);
        }

        // ================= 初期化の待機 =================

        // 初期化が成功するまで、Service を呼ばずに待つ。待機中も、処理中の段階（最初の段階）になる
        [Test]
        public async Task WaitsForInitializationBeforeCallingService()
        {
            _gate.BeginInitializing();
            _service.SizeInfo = new DownloadSizeInfo(1, 100);

            var pending = _controller.DownloadAsync(null, _progress, CancellationToken.None);

            await WaitUntil(() => _gate.WaitInitializedCallCount == 1);
            Assert.AreEqual(0, _service.CallLog.Count, "初期化の成功まで、Service を呼ばないはず");
            Assert.AreEqual(DownloadPhase.CalculatingSize, _controller.AccStatus.AccPhase);
            Assert.IsTrue(_controller.AccStatus.AccIsBusy, "待機中も、処理中として見えるはず");

            _gate.CompleteInitialization();
            await pending;

            Assert.AreEqual(1, _service.Size.CallCount);
            Assert.AreEqual(DownloadPhase.Completed, _controller.AccStatus.AccPhase);
        }

        // 初期化が未開始なら InvalidOperationException。段階は Failed になり、最後の通知が届き、Service は呼ばれない
        [Test]
        public async Task InitializationNotStartedThrowsInvalidOperationAndFailed()
        {
            _gate.BeginInitializing();
            _gate.InitializationException = new InvalidOperationException("初期化が始まっていません");

            await TestAsync.Catch<InvalidOperationException>(
                async () => await _controller.DownloadAsync(null, _progress, CancellationToken.None));

            Assert.AreEqual(DownloadPhase.Failed, _controller.AccStatus.AccPhase);
            Assert.AreEqual(DownloadPhase.Failed, _progress.Last.AccPhase);
            Assert.AreEqual(0, _service.CallLog.Count);
        }

        // 初期化の失敗は、AddressableDownloadException に包まず、そのまま返す
        [Test]
        public async Task InitializationFailureIsReturnedAsIs()
        {
            var failure = new AddressableInitializeException("初期化の失敗", new Exception());
            _gate.BeginInitializing();
            _gate.InitializationException = failure;

            var exception = await TestAsync.Catch<AddressableInitializeException>(
                async () => await _controller.CheckUpdateAsync(null, CancellationToken.None));

            Assert.AreSame(failure, exception);
            Assert.AreEqual(DownloadPhase.Failed, _controller.AccStatus.AccPhase);
        }

        // 初期化の待機中に ct をキャンセルすると OperationCanceledException・Canceled
        [Test]
        public async Task CancellingDuringInitializationWaitThrowsOperationCanceled()
        {
            _gate.BeginInitializing();
            using var cts = new CancellationTokenSource();
            var pending = _controller.HasUpdateAsync(null, cts.Token);
            await WaitUntil(() => _gate.WaitInitializedCallCount == 1);

            cts.Cancel();

            await TestAsync.Catch<OperationCanceledException>(async () => await pending);
            Assert.AreEqual(DownloadPhase.Canceled, _controller.AccStatus.AccPhase);
            Assert.AreEqual(0, _service.CallLog.Count);
        }

        // ================= Dispose =================

        // 実行中の Dispose：Canceled を通知してから通知先を外し、待機中の呼び出し元は OperationCanceledException で終わる
        [Test]
        public async Task DisposeWhileRunningNotifiesCanceledAndEndsWaiter()
        {
            _service.SizeInfo = new DownloadSizeInfo(1, 100);
            _service.Download.HoldNext();
            var pending = _controller.DownloadAsync(null, _progress, CancellationToken.None);
            await WaitUntil(() => _service.Download.CallCount == 1);

            _controller.Dispose();

            await TestAsync.Catch<OperationCanceledException>(async () => await pending);
            Assert.AreEqual(DownloadPhase.Canceled, _controller.AccStatus.AccPhase);
            Assert.AreEqual(DownloadPhase.Canceled, _progress.Last.AccPhase);
            Assert.AreEqual(1, _progress.Received.Count(s => s.AccPhase == DownloadPhase.Canceled), "最後の通知は1回だけのはず");
            Assert.IsFalse(_model.AccIsRunning);
            Assert.Greater(_service.ReleaseCallCount, 0);
        }

        // 破棄後の呼び出しは ObjectDisposedException。Tick は何もしない
        [Test]
        public async Task CallsAfterDisposeThrowObjectDisposed()
        {
            _controller.Dispose();

            await TestAsync.Catch<ObjectDisposedException>(async () => await _controller.HasUpdateAsync(null, CancellationToken.None));
            await TestAsync.Catch<ObjectDisposedException>(async () => await _controller.CheckUpdateAsync(null, CancellationToken.None));
            await TestAsync.Catch<ObjectDisposedException>(async () => await _controller.DownloadAsync(null, _progress, CancellationToken.None));
            Assert.DoesNotThrow(() => _controller.Tick(0.016f));
            Assert.DoesNotThrow(() => _controller.Dispose(), "二重の Dispose は何もしないはず");
        }

        // 何も処理していないときの Dispose は、通知も状態の変更もしない
        [Test]
        public void DisposeWhenIdleDoesNothing()
        {
            _controller.Dispose();

            Assert.AreEqual(DownloadPhase.None, _controller.AccStatus.AccPhase);
        }
    }
}
