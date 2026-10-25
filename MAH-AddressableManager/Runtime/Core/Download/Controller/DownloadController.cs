using System;
using System.Collections.Generic;
using System.Threading;
#if ADDRESSABLE_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#else
using System.Threading.Tasks;
#endif

namespace MAH.AddressableManager.Core.Download
{
    /// <summary>
    /// カタログの更新確認・更新と、更新分のアセットのダウンロードを行う Controller。
    /// 状態データは DownloadStatusModel（Manager から注入）、Addressables の呼び出しは Service に任せる。
    /// 初期化の成功は IInitializationGate を介して待つ（InitializeController・LoadingController は参照しない）。
    /// 確認・判定・ダウンロードは同時に1つだけ実行でき、状態（段階・バイト数）は Model が持つ。
    /// 処理の終わりは、① 結果の記録 → ② 最後の通知 → ③ 通知先の解除（End）→ ④ 呼び出し元へ完了・例外 の順で行う。
    /// Manager の LateUpdate から Tick が呼ばれ、ダウンロード中だけバイト数を更新する。
    /// </summary>
    internal sealed class DownloadController : IDisposable
    {
        private readonly AddressableDownloadSettings _settings;
        private readonly DownloadStatusModel _statusModel;
        private readonly IAddressablesDownloadService _service;
        private readonly IInitializationGate _gate;
        private readonly CancellationTokenSource _disposeCts = new(); // 実行中の処理を止められるのは、Dispose だけ（呼び出し元の ct は、その呼び出しの待機だけ）
        private bool _isDisposed;

        /// <summary> 現在の状態（その時点の値のコピー）。一度も処理していなければ AccPhase == None </summary>
        internal DownloadStatus AccStatus => _statusModel.AccStatus;

        internal DownloadController(
            AddressableDownloadSettings settings,
            DownloadStatusModel status_model,
            IAddressablesDownloadService service,
            IInitializationGate gate)
        {
            _settings = settings;
            _statusModel = status_model;
            _service = service;
            _gate = gate;
        }

        /// <summary>
        /// 更新データがあるかどうかだけを判定する。カタログは更新しない（方針に関係なく UpdateCatalogs を呼ばない）。
        /// labels が null なら SO の Label を使う（Handler の引数なし版）。
        /// 方針が None ならカタログを確認せず、今のカタログのバンドルだけを数える。更新のあるカタログがあれば、サイズを計算せずに true
        /// </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        internal async UniTask<bool> HasUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct)
#else
        internal async Task<bool> HasUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct)
#endif
        {
            ThrowIfDisposed();
            var target_labels = ResolveLabels(labels);
            var check_catalog = _settings.AccCatalogPolicy != CatalogUpdatePolicy.None;
            BeginProcess(check_catalog ? DownloadPhase.CheckingCatalog : DownloadPhase.CalculatingSize, null);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            var token = linked.Token;
            try
            {
                token.ThrowIfCancellationRequested();
                await _gate.WaitUntilInitializedAsync(token);

                var has_update = false;
                if (check_catalog)
                {
                    var catalog_ids = await CheckCatalogsAsync(token);
                    has_update = catalog_ids.Count > 0; // 更新のあるカタログがあれば、サイズを計算せずに true
                }

                if (!has_update)
                {
                    var size = await CalculateSizeAsync(target_labels, token);
                    has_update = size.AccBundleCount > 0;
                }

                _statusModel.MarkCompleted();
                return has_update;
            }
            catch (OperationCanceledException)
            {
                _statusModel.MarkCanceled();
                throw;
            }
            catch (Exception)
            {
                _statusModel.MarkFailed();
                throw;
            }
            finally
            {
                _statusModel.End();
            }
        }

        /// <summary>
        /// カタログを方針に従って確認・更新し、ダウンロードが必要な件数と容量を返す。labels が null なら SO の Label を使う。
        /// 更新に渡すカタログ ID は、確認で得たものを明示する（null を渡さない）
        /// </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        internal async UniTask<DownloadCheckResult> CheckUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct)
#else
        internal async Task<DownloadCheckResult> CheckUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct)
#endif
        {
            ThrowIfDisposed();
            var target_labels = ResolveLabels(labels);
            var policy = _settings.AccCatalogPolicy;
            BeginProcess(policy != CatalogUpdatePolicy.None ? DownloadPhase.CheckingCatalog : DownloadPhase.CalculatingSize, null);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            var token = linked.Token;
            try
            {
                token.ThrowIfCancellationRequested();
                await _gate.WaitUntilInitializedAsync(token);

                var catalog_update_count = 0;
                var is_catalog_update_pending = false;
                if (policy != CatalogUpdatePolicy.None)
                {
                    var catalog_ids = await CheckCatalogsAsync(token);
                    catalog_update_count = catalog_ids.Count;
                    if (catalog_update_count > 0)
                    {
                        if (policy == CatalogUpdatePolicy.CheckAndUpdate)
                        {
                            await UpdateCatalogsAsync(catalog_ids, token);
                        }
                        else
                        {
                            is_catalog_update_pending = true; // CheckOnly：更新を見送った
                        }
                    }
                }

                var size = await CalculateSizeAsync(target_labels, token);

                _statusModel.MarkCompleted();
                return new DownloadCheckResult(catalog_update_count, size.AccBundleCount, size.AccTotalBytes, is_catalog_update_pending);
            }
            catch (OperationCanceledException)
            {
                _statusModel.MarkCanceled();
                throw;
            }
            catch (Exception)
            {
                _statusModel.MarkFailed();
                throw;
            }
            finally
            {
                _statusModel.End();
            }
        }

        /// <summary>
        /// 対象 Label のアセットをダウンロードする（バンドルをキャッシュに保存するだけで、アセットは読み込まない）。カタログは更新しない。
        /// labels が null なら SO の Label を使う。progress には、段階・バイト数・リトライ回数が届く（null でもよい）。
        /// 最後の通知（Completed / Failed / Canceled）は、通知先を外す前に1回だけ届く
        /// </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        internal async UniTask DownloadAsync(IReadOnlyList<string> labels, IProgress<DownloadStatus> progress, CancellationToken ct)
#else
        internal async Task DownloadAsync(IReadOnlyList<string> labels, IProgress<DownloadStatus> progress, CancellationToken ct)
#endif
        {
            ThrowIfDisposed();
            var target_labels = ResolveLabels(labels);
            BeginProcess(DownloadPhase.CalculatingSize, progress);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            var token = linked.Token;
            try
            {
                token.ThrowIfCancellationRequested();
                await _gate.WaitUntilInitializedAsync(token);

                // 総量を確定させる。ダウンロードが必要なバンドルが無ければ、ダウンロードを始めずに完了する
                var size = await CalculateSizeAsync(target_labels, token);
                _statusModel.SetBytes(0, size.AccTotalBytes);
                if (size.AccBundleCount > 0)
                {
                    _statusModel.MarkPhase(DownloadPhase.Downloading);
                    await DownloadWithRetryAsync(target_labels, token);
                }

                _statusModel.MarkCompleted();
            }
            catch (OperationCanceledException)
            {
                _statusModel.MarkCanceled();
                throw;
            }
            catch (Exception)
            {
                _statusModel.MarkFailed();
                throw;
            }
            finally
            {
                _service.ReleaseDownloadHandle(); // キャンセル・破棄で待機をやめた場合に、handle を解放する（解放済みなら何もしない）
                _statusModel.End();
            }
        }

        /// <summary> Manager の LateUpdate から呼ばれる。ダウンロード中だけ、バイト数を読んで状態を更新し、通知先へ通知する </summary>
        internal void Tick(float delta_time)
        {
            if (_isDisposed) return;
            if (_statusModel.AccStatus.AccPhase != DownloadPhase.Downloading) return;

            _service.GetDownloadBytes(out var downloaded_bytes, out var total_bytes);

            // DownloadDependenciesAsync は、ロケーションの取得が終わるまで合計が 0 のため、サイズ計算で確定した合計を維持する（表示が一瞬 0 に戻らないようにする）
            if (total_bytes <= 0)
                total_bytes = _statusModel.AccStatus.AccTotalBytes;
            _statusModel.SetBytes(downloaded_bytes, total_bytes);
        }

        /// <summary>
        /// 実行中の処理を、キャンセル（Canceled）として終わらせる。最後の通知を届けてから、通知先を外す。
        /// 待機中の呼び出し元は OperationCanceledException で終わる。破棄後の状態は変化しない
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // 呼び出し元の継続の実行順に頼らず、最後の通知と通知先の解除を、ここで確実に行う（継続側の Mark* / End は、何も変えずに戻る）
            _disposeCts.Cancel();
            _statusModel.MarkCanceled();
            _statusModel.End();
            _service.ReleaseDownloadHandle();
            _disposeCts.Dispose();
        }

        // 呼び出し元の指定を、対象の Label に解決する。labels が null なら SO の Label（空なら設定漏れとして InvalidOperationException）。
        // 引数で渡された Label が空・空文字を含むなら ArgumentException。検証は、状態を変える前に行う
        private IReadOnlyList<string> ResolveLabels(IReadOnlyList<string> labels)
        {
            if (labels == null)
            {
                var default_labels = _settings.AccLabels;
                if (default_labels == null || default_labels.Count == 0)
                    throw new InvalidOperationException("対象の Label が設定されていません。Runtime SO の「ダウンロード」の Label を設定するか、引数で Label を渡してください。");
                if (ContainsEmptyLabel(default_labels))
                    throw new InvalidOperationException("Runtime SO の「ダウンロード」の Label に、空の要素が含まれています。");
                return default_labels;
            }

            if (labels.Count == 0)
                throw new ArgumentException("対象の Label が空です。", nameof(labels));
            if (ContainsEmptyLabel(labels))
                throw new ArgumentException("対象の Label に、null または空文字の要素が含まれています。", nameof(labels));
            return labels;
        }

        private static bool ContainsEmptyLabel(IReadOnlyList<string> labels)
        {
            for (int i = 0; i < labels.Count; i++) // IReadOnlyList の foreach は列挙子がボックス化されるため、for で回す
            {
                if (string.IsNullOrEmpty(labels[i]))
                    return true;
            }
            return false;
        }

        // 処理を始める。実行中なら InvalidOperationException（実行中の処理は影響を受けない）。
        // 破棄との間に await を挟まないため、ThrowIfDisposed の後に呼べば、破棄済みの状態で始まらない
        private void BeginProcess(DownloadPhase first_phase, IProgress<DownloadStatus> progress)
        {
            if (!_statusModel.TryBegin(first_phase, progress))
                throw new InvalidOperationException("ダウンロード機能の別の処理（更新の確認・判定・ダウンロード）が実行中です。完了してから呼んでください。");
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(DownloadController));
        }

        // ---- 段階ごとの処理。失敗したら、その段階を maxRetry 回までやり直す（リトライ回数は段階ごとに数える） ----

#if ADDRESSABLE_UNITASK_SUPPORT
        private async UniTask<IReadOnlyList<string>> CheckCatalogsAsync(CancellationToken token)
#else
        private async Task<IReadOnlyList<string>> CheckCatalogsAsync(CancellationToken token)
#endif
        {
            _statusModel.MarkPhase(DownloadPhase.CheckingCatalog);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return await _service.CheckForCatalogUpdatesAsync().WithCancellation(token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    await HandleFailureAsync(DownloadPhase.CheckingCatalog, attempt, exception, token);
                }
            }
        }

#if ADDRESSABLE_UNITASK_SUPPORT
        private async UniTask UpdateCatalogsAsync(IReadOnlyList<string> catalog_ids, CancellationToken token)
#else
        private async Task UpdateCatalogsAsync(IReadOnlyList<string> catalog_ids, CancellationToken token)
#endif
        {
            _statusModel.MarkPhase(DownloadPhase.UpdatingCatalog);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    // Addressables の更新は途中で止められず、呼び出し元の ct で待機だけをやめると、Canceled を返した後に裏でカタログが更新される。
                    // 利用者の判断と実際の状態がずれないよう、更新の待機は ct では打ち切らず、Manager の破棄（_disposeCts）でだけ打ち切る。
                    // 更新を始める前に ct がキャンセルされていたら、更新を始めずに終える。更新が終わった後にキャンセルされていたら、次の段階へ進まずに Canceled で終える（カタログは更新済み）。
                    // リトライ前の待機（HandleFailureAsync）は、Addressables の操作が走っていないため、ct で打ち切れる
                    token.ThrowIfCancellationRequested();
                    await _service.UpdateCatalogsAsync(catalog_ids, _settings.AccAutoCleanBundleCache).WithCancellation(_disposeCts.Token);
                    token.ThrowIfCancellationRequested();
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    await HandleFailureAsync(DownloadPhase.UpdatingCatalog, attempt, exception, token);
                }
            }
        }

#if ADDRESSABLE_UNITASK_SUPPORT
        private async UniTask<DownloadSizeInfo> CalculateSizeAsync(IReadOnlyList<string> labels, CancellationToken token)
#else
        private async Task<DownloadSizeInfo> CalculateSizeAsync(IReadOnlyList<string> labels, CancellationToken token)
#endif
        {
            _statusModel.MarkPhase(DownloadPhase.CalculatingSize);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return await _service.CalculateDownloadSizeAsync(labels).WithCancellation(token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    await HandleFailureAsync(DownloadPhase.CalculatingSize, attempt, exception, token);
                }
            }
        }

#if ADDRESSABLE_UNITASK_SUPPORT
        private async UniTask DownloadWithRetryAsync(IReadOnlyList<string> labels, CancellationToken token)
#else
        private async Task DownloadWithRetryAsync(IReadOnlyList<string> labels, CancellationToken token)
#endif
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await _service.StartDownloadAsync(labels).WithCancellation(token);
                    return;
                }
                catch (OperationCanceledException)
                {
                    _service.ReleaseDownloadHandle(); // 待機をやめるため、handle を解放する（開始済みの通信は、止まらない場合がある）
                    throw;
                }
                catch (Exception exception)
                {
                    await HandleFailureAsync(DownloadPhase.Downloading, attempt, exception, token);
                }
            }
        }

        // 段階の処理が失敗したときの扱い。リトライを使い切っていれば AddressableDownloadException、残っていれば
        // リトライ回数を増やして通知し、待機してから戻る（呼び出し元がやり直す）。待機は実時間で行う（AsyncCompat.Delay）
#if ADDRESSABLE_UNITASK_SUPPORT
        private async UniTask HandleFailureAsync(DownloadPhase phase, int attempt, Exception exception, CancellationToken token)
#else
        private async Task HandleFailureAsync(DownloadPhase phase, int attempt, Exception exception, CancellationToken token)
#endif
        {
            var max_retry = _settings.AccMaxRetry;
            if (attempt >= max_retry)
            {
                var message = $"ダウンロード機能の処理（{phase}）が、リトライ（{max_retry}回）を使い切って失敗しました。";
                AddressableLogger.LogError(message);
                throw new AddressableDownloadException(phase, attempt, message, exception);
            }

            AddressableLogger.LogWarning($"ダウンロード機能の処理（{phase}）に失敗しました。リトライします（{attempt + 1}/{max_retry}）。{exception.Message}");
            _statusModel.IncrementRetry();
            await AsyncCompat.Delay(_settings.AccRetryDelayMs, token);
        }
    }
}
