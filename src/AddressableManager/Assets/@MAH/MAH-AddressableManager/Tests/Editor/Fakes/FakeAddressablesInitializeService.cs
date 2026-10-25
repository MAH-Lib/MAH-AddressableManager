using System;
using System.Threading.Tasks;
using MAH.AddressableManager.Core.Initialize;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// IAddressablesInitializeService の偽実装。実際の Addressables を呼ばず、テストから完了・失敗・同期の例外・進捗を操作できる。
    /// 既定では完了せずに止まり、Succeed / Fail で完了させる。
    /// </summary>
    internal sealed class FakeAddressablesInitializeService : IAddressablesInitializeService
    {
        private TaskCompletionSource<bool> _source;

        /// <summary> StartInitialize が呼ばれた回数 </summary>
        internal int StartCallCount { get; private set; }

        /// <summary> 最後の StartInitialize に渡された autoReleaseHandle </summary>
        internal bool LastAutoReleaseHandle { get; private set; }

        /// <summary> ReleaseHandle が呼ばれた回数 </summary>
        internal int ReleaseHandleCallCount { get; private set; }

        /// <summary> GetPercentComplete が返す値 </summary>
        internal float Percent { get; set; }

        /// <summary> true にすると、StartInitialize がその場で成功した Task を返す（Addressables が初期化済みの場合の再現） </summary>
        internal bool CompleteImmediately { get; set; }

        /// <summary> 設定すると、StartInitialize がその例外で失敗した Task を返す（同期の例外の再現。実際の Service も、投げずに失敗した Task にする） </summary>
        internal Exception FailOnStart { get; set; }

        public Task StartInitialize(bool auto_release_handle)
        {
            StartCallCount++;
            LastAutoReleaseHandle = auto_release_handle;

            if (FailOnStart != null) return Task.FromException(FailOnStart);
            if (CompleteImmediately) return Task.CompletedTask;

            _source = new TaskCompletionSource<bool>();
            return _source.Task;
        }

        public float GetPercentComplete() => Percent;

        public void ReleaseHandle() => ReleaseHandleCallCount++;

        /// <summary> 止めていた初期化を成功で完了させる </summary>
        internal void Succeed() => _source.TrySetResult(true);

        /// <summary> 止めていた初期化を失敗で完了させる </summary>
        internal void Fail(Exception exception) => _source.TrySetException(exception);
    }
}
