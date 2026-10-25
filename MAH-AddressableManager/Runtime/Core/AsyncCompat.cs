using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.ResourceManagement.AsyncOperations;
#if ADDRESSABLE_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#endif

namespace MAH.AddressableManager.Core
{
    /// <summary>
    /// Task と UniTask の差を吸収する薄いヘルパー。Task/UniTask の切り替えはここに集約する。
    /// 呼び出し側は await で使う限り、どちらの型が使われているか意識する必要はない。
    /// </summary>
    internal static class AsyncCompat
    {
        // キャンセル通知で待機を終わらせる delegate。クロージャを毎回作らないよう、static にキャッシュし、状態（tcs）は引数で渡す。
        // Task 版の WithCancellation は、UniTask 使用時にも使うため、シンボルに関わらず常にコンパイルする
        private static readonly Action<object> CANCEL_WAIT = state => ((TaskCompletionSource<bool>)state).TrySetResult(true);

#if ADDRESSABLE_UNITASK_SUPPORT
        internal static UniTask WithCancellation(this UniTask task, CancellationToken ct)
            => task.AttachExternalCancellation(ct);

        internal static UniTask<T> WithCancellation<T>(this UniTask<T> task, CancellationToken ct)
            => task.AttachExternalCancellation(ct);

        // UniTask.Delay は既定で timeScale の影響を受け、ポーズ中（timeScale = 0）は待機が終わらない。
        // Task 版（Task.Delay）と同じく実時間で待つよう、ignoreTimeScale を指定する
        internal static UniTask Delay(int milliseconds, CancellationToken ct)
            => UniTask.Delay(milliseconds, ignoreTimeScale: true, cancellationToken: ct);
#else
        internal static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken ct)
        {
            if (!ct.CanBeCanceled) return await task;

            var tcs = new TaskCompletionSource<bool>();
            using (ct.Register(CANCEL_WAIT, tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                    throw new OperationCanceledException(ct);
            }
            return await task; // 失敗時の例外をそのまま伝える
        }

        internal static Task Delay(int milliseconds, CancellationToken ct) => Task.Delay(milliseconds, ct);

        /// <summary> AsyncOperationHandle を Task に変換する。失敗時は OperationException を伝える </summary>
        internal static Task<T> ToTask<T>(this AsyncOperationHandle<T> handle)
        {
            var tcs = new TaskCompletionSource<T>();
            if (handle.IsDone) { SetResult(tcs, handle); return tcs.Task; }
            handle.Completed += h => SetResult(tcs, h);
            return tcs.Task;
        }

        private static void SetResult<T>(TaskCompletionSource<T> tcs, AsyncOperationHandle<T> handle)
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
                tcs.TrySetResult(handle.Result);
            else
                tcs.TrySetException(handle.OperationException ?? new Exception("Addressables のロードに失敗しました。"));
        }
#endif

        // 複数の呼び出し元が待つ共有タスク（重複ロード・初期化）は、安全性を優先してUniTask使用時もTaskのまま扱うため
        // （UniTaskは何度もawaitすると例外や別処理の状態の読み取りになり得る。設計仕様書3.5.1）、
        // Task版のWithCancellationはシンボルに関わらず常にコンパイルする
        internal static async Task WithCancellation(this Task task, CancellationToken ct)
        {
            if (!ct.CanBeCanceled) { await task; return; }

            var tcs = new TaskCompletionSource<bool>();
            using (ct.Register(CANCEL_WAIT, tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                    throw new OperationCanceledException(ct);
            }
            await task; // 失敗時の例外をそのまま伝える
        }

        /// <summary>
        /// 全件の完了、または最初の失敗（またはキャンセル）で完了する Task を返す。
        /// Task.WhenAll は全件の完了を待つため、1件が失敗しても最も遅いロードが終わるまで待ち続けてしまう。
        /// Label 一括ロードで、失敗したらすぐ全件を巻き戻すために使う。失敗の例外は、最初の1件をそのまま（AggregateException に包まず）伝える。
        /// 共有ロードタスクは常に Task のため、UniTask 使用時もこの Task 版を使う。
        /// </summary>
        internal static Task WaitAllOrFirstFault(Task[] tasks)
        {
            if (tasks == null)
                throw new ArgumentNullException(nameof(tasks));
            if (tasks.Length == 0)
                return Task.CompletedTask;

            var state = new WaitAllState(tasks.Length);
            for (int i = 0; i < tasks.Length; i++)
                tasks[i].ContinueWith(ON_TASK_COMPLETED, state, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return state.AccTask;
        }

        // クロージャを作らないよう、delegate は static にキャッシュし、状態は引数で渡す
        private static readonly Action<Task, object> ON_TASK_COMPLETED = (task, obj) =>
        {
            var state = (WaitAllState)obj;
            if (task.IsFaulted)
                state.TrySetFault(task.Exception.InnerException ?? task.Exception); // Exception を読んで、未観測の例外にもしない
            else if (task.IsCanceled)
                state.TrySetCanceled();
            else
                state.CompleteOne();
        };

        private sealed class WaitAllState
        {
            private readonly TaskCompletionSource<bool> _tcs = new();
            private int _remaining;

            internal Task AccTask => _tcs.Task;

            internal WaitAllState(int count) => _remaining = count;

            internal void CompleteOne()
            {
                if (Interlocked.Decrement(ref _remaining) == 0)
                    _tcs.TrySetResult(true);
            }

            internal void TrySetFault(Exception exception) => _tcs.TrySetException(exception);

            internal void TrySetCanceled() => _tcs.TrySetCanceled();
        }
    }
}
