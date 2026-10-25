using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MAH.AddressableManager.Core.Initialize
{
    /// <summary>
    /// 初期化の状態・失敗の例外・共有タスク・進捗の通知先という状態データを保持するModel。
    /// Succeeded / Failed を終端とする遷移の制約は、遷移メソッドが守る（Controller が誤って遷移させても状態は変わらない）。
    /// Addressables・設定は知らない。触るのは InitializeController だけで、LoadingController は IInitializationGate を通す。
    /// </summary>
    internal sealed class InitializeStateModel
    {
        private readonly List<IProgress<float>> _progressTargets = new(); // 待機中の呼び出し元の通知先。使い回す
        private InitializeState _state = InitializeState.NotStarted;
        private AddressableInitializeException _failure;
        private Task _sharedTask;

        /// <summary> 初期化の状態 </summary>
        internal InitializeState AccState => _state;
        /// <summary> Failed になったときの例外。Failed 以外では null </summary>
        internal AddressableInitializeException AccFailure => _failure;
        /// <summary> 初期化1回分の処理を表す共有タスク。NotStarted では null </summary>
        internal Task AccSharedTask => _sharedTask;

        /// <summary> NotStarted → Running。共有タスクを記録する。条件に合わないときは何も変えず false を返す </summary>
        internal bool MarkRunning(Task shared_task)
        {
            if (_state != InitializeState.NotStarted) return false;
            if (shared_task == null) throw new ArgumentNullException(nameof(shared_task));

            _sharedTask = shared_task;
            _state = InitializeState.Running;
            return true;
        }

        /// <summary> Running → Succeeded（終端）。条件に合わないときは何も変えず false を返す </summary>
        internal bool MarkSucceeded()
        {
            if (_state != InitializeState.Running) return false;

            _state = InitializeState.Succeeded;
            return true;
        }

        /// <summary> Running → Failed（終端）。失敗の例外を記録する。条件に合わないときは何も変えず false を返す </summary>
        internal bool MarkFailed(AddressableInitializeException exception)
        {
            if (_state != InitializeState.Running) return false;
            if (exception == null) throw new ArgumentNullException(nameof(exception));

            _failure = exception;
            _state = InitializeState.Failed;
            return true;
        }

        /// <summary> 待機を始めた呼び出し元の進捗通知先を登録する。null は登録しない </summary>
        internal void AddProgressTarget(IProgress<float> progress)
        {
            if (progress == null) return;
            _progressTargets.Add(progress);
        }

        /// <summary> 待機が終わった（完了・キャンセルとも）呼び出し元の進捗通知先を外す。同じ通知先が複数あれば1つだけ外す </summary>
        internal void RemoveProgressTarget(IProgress<float> progress)
        {
            if (progress == null) return;
            _progressTargets.Remove(progress);
        }

        /// <summary>
        /// 登録されている通知先すべてに進捗を通知する。
        /// 通知先は利用者のコードのため、例外を投げても受け止めてログにし、残りの通知先への通知を続ける。
        /// 成功時の完了通知（1）は共有タスクの完了より前に行うため、ここで止まると待機中の呼び出し元が終わらなくなる。
        /// Tick からも呼ばれ、例外が OnLateUpdate に出ると後ろの購読先（LoadingController.Tick）が止まる
        /// </summary>
        internal void ReportProgress(float value)
        {
            // foreach の代わりに添字で回す。通知先の処理中に登録・解除されても範囲外にならないよう、毎回 Count を見る
            for (int i = 0; i < _progressTargets.Count; i++)
            {
                try
                {
                    _progressTargets[i].Report(value);
                }
                catch (Exception exception)
                {
                    AddressableLogger.LogException(exception);
                }
            }
        }
    }
}
