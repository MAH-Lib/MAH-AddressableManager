using System;

namespace MAH.AddressableManager.Core.Download
{
    /// <summary>
    /// ダウンロード機能の状態（段階・バイト数・リトライ回数）・実行中フラグ・進捗の通知先を保持する Model。
    /// 状態は、遷移のたびに DownloadStatus（readonly struct）を作り直して保持する。利用者へは、その時点の値のコピーとして渡す。
    /// 同時に実行できるのは1つだけで、実行中かどうかは TryBegin / End で管理する。
    /// 遷移メソッドは、実行中でないとき・すでに結果（Completed / Failed / Canceled）が記録されているときは、何も変えず false を返す
    /// （最後の通知が1回だけになるように、Model の側で保証する）。
    /// Addressables・設定は知らない。触るのは DownloadController だけ。
    /// </summary>
    internal sealed class DownloadStatusModel
    {
        private DownloadStatus _status; // 遷移のたびに作り直す。default は「まだ何も処理していない」状態
        private DownloadPhase _phase = DownloadPhase.None;
        private long _downloadedBytes;
        private long _totalBytes;
        private int _retryCount;
        private bool _isRunning;
        private IProgress<DownloadStatus> _progressTarget;

        /// <summary> 現在の状態（その時点の値のコピー）。取得した値は後から変わらない </summary>
        internal DownloadStatus AccStatus => _status;
        /// <summary> 処理（確認・判定・ダウンロード）の実行中か。TryBegin から End までの間 </summary>
        internal bool AccIsRunning => _isRunning;

        /// <summary>
        /// 処理を始める。実行中なら何も変えず false を返す。
        /// 成功すると、バイト数とリトライ回数を 0 に戻し、段階を first_phase にして、通知先を置き換える（前の処理の通知先は残さない）。
        /// 開始（段階の変化）を、新しい通知先へ1回通知する。first_phase は、処理中の段階（None・Completed・Failed・Canceled は不可）
        /// </summary>
        internal bool TryBegin(DownloadPhase first_phase, IProgress<DownloadStatus> progress)
        {
            if (!IsBusyPhase(first_phase))
                throw new ArgumentException($"処理の開始時の段階には、処理中の段階を指定してください（{first_phase}）。", nameof(first_phase));
            if (_isRunning) return false;

            _isRunning = true;
            _progressTarget = progress;
            _phase = first_phase;
            _downloadedBytes = 0;
            _totalBytes = 0;
            _retryCount = 0;
            Publish();
            return true;
        }

        /// <summary>
        /// 処理中の段階を進める。段階が変わると、リトライ回数を 0 に戻して通知する。同じ段階を指定したときは何もしない（通知もしない）。
        /// 実行中でない、または結果が記録済みなら false
        /// </summary>
        internal bool MarkPhase(DownloadPhase phase)
        {
            if (!IsBusyPhase(phase))
                throw new ArgumentException($"処理中の段階を指定してください（{phase}）。結果は MarkCompleted / MarkFailed / MarkCanceled で記録します。", nameof(phase));
            if (!CanTransition()) return false;
            if (phase == _phase) return true;

            _phase = phase;
            _retryCount = 0;
            Publish();
            return true;
        }

        /// <summary> ダウンロード済み・合計のバイト数を記録して通知する。Tick のたび（毎フレーム）に呼ばれる。実行中でない、または結果が記録済みなら false </summary>
        internal bool SetBytes(long downloaded_bytes, long total_bytes)
        {
            if (!CanTransition()) return false;

            _downloadedBytes = Math.Max(0L, downloaded_bytes);
            _totalBytes = Math.Max(0L, total_bytes);
            Publish();
            return true;
        }

        /// <summary> 現在の段階でのリトライ回数を 1 増やして通知する。実行中でない、または結果が記録済みなら false </summary>
        internal bool IncrementRetry()
        {
            if (!CanTransition()) return false;

            _retryCount++;
            Publish();
            return true;
        }

        /// <summary> 成功を記録して通知する。合計バイト数が分かっていれば、ダウンロード済みを合計に合わせる。実行中でない、または結果が記録済みなら false </summary>
        internal bool MarkCompleted()
        {
            if (!CanTransition()) return false;

            if (_totalBytes > 0)
                _downloadedBytes = _totalBytes;
            _phase = DownloadPhase.Completed;
            Publish();
            return true;
        }

        /// <summary> 失敗を記録して通知する。バイト数はそのまま残す。実行中でない、または結果が記録済みなら false </summary>
        internal bool MarkFailed()
        {
            if (!CanTransition()) return false;

            _phase = DownloadPhase.Failed;
            Publish();
            return true;
        }

        /// <summary> キャンセルを記録して通知する。バイト数はそのまま残す。実行中でない、または結果が記録済みなら false </summary>
        internal bool MarkCanceled()
        {
            if (!CanTransition()) return false;

            _phase = DownloadPhase.Canceled;
            Publish();
            return true;
        }

        /// <summary>
        /// 処理を終える。通知先を外し、実行中を解除する。結果（Completed / Failed / Canceled）は、次の TryBegin まで残る。
        /// 最後の通知を届けた後に、必ず呼ぶ（finally から）。外す前に実行中を解除しないのは、最後の通知の中で次の処理が始められ、
        /// その通知先を、ここで外してしまうことを避けるため
        /// </summary>
        internal void End()
        {
            _progressTarget = null;
            _isRunning = false;
        }

        // 実行中で、まだ結果が記録されていないときだけ、遷移できる（最後の通知を1回だけにするため）
        private bool CanTransition() => _isRunning && IsBusyPhase(_phase);

        private static bool IsBusyPhase(DownloadPhase phase)
        {
            return phase == DownloadPhase.CheckingCatalog
                || phase == DownloadPhase.UpdatingCatalog
                || phase == DownloadPhase.CalculatingSize
                || phase == DownloadPhase.Downloading;
        }

        // 状態を作り直して、通知先へ届ける。
        // 通知先は利用者のコードのため、Report が例外を投げても受け止めてログにする
        // （最後の通知で止まると、呼び出し元に完了・例外が返らなくなる。Tick で例外が出ると、同じ OnLateUpdate の後ろの購読先も止まる）
        private void Publish()
        {
            _status = new DownloadStatus(_phase, _downloadedBytes, _totalBytes, _retryCount);
            if (_progressTarget == null) return;

            try
            {
                _progressTarget.Report(_status);
            }
            catch (Exception exception)
            {
                AddressableLogger.LogException(exception);
            }
        }
    }
}
