using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// テストで非同期の完了待ちに使う共通ヘルパー。
    /// TaskCompletionSource の継続がスレッドプール側で走るケースを吸収するため、
    /// 即座に条件を満たさなくても一定時間ポーリングする。
    /// </summary>
    internal static class TestAsync
    {
        /// <summary>
        /// タスクを待ち、指定した型（派生型を含む）の例外で終わったことを確認して、その例外を返す。
        /// 例外が出なかった場合・別の型の例外の場合はテストを失敗させる。
        /// Assert.ThrowsAsync は、同期コンテキストの上で待機が詰まる恐れがあるため使わず、await で待つ。
        /// </summary>
        internal static async Task<TException> Catch<TException>(Task task) where TException : Exception
        {
            try
            {
                await task;
            }
            catch (TException exception)
            {
                return exception;
            }
            Assert.Fail($"{typeof(TException).Name} が発生しませんでした。");
            return null;
        }

        /// <summary>
        /// 処理を呼び、指定した型（派生型を含む）の例外で終わったことを確認して、その例外を返す。
        /// 呼び出しはその場で始まる（最初の await まで同期で進む）ため、待機中の状態を操作してから、返ってきた Task を待てる。
        /// 対象が UniTask を返す環境でも書けるよう、async ラムダ（() => await ...）で渡す。
        /// </summary>
        internal static async Task<TException> Catch<TException>(Func<Task> action) where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException exception)
            {
                return exception;
            }
            Assert.Fail($"{typeof(TException).Name} が発生しませんでした。");
            return null;
        }

        internal static async Task WaitUntil(Func<bool> condition, int timeout_ms = 1000)
        {
            var start = DateTime.UtcNow;
            while (!condition())
            {
                if ((DateTime.UtcNow - start).TotalMilliseconds > timeout_ms)
                    Assert.Fail("タイムアウトしました。条件が満たされませんでした。");
                await Task.Delay(10);
            }
        }
    }
}
