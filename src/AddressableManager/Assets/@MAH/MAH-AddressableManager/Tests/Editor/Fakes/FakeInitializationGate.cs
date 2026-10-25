using System;
using System.Threading;
using System.Threading.Tasks;
using MAH.AddressableManager.Core;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// IInitializationGate の偽実装。既定では常に開いていて、初期化済みでもあるため、これまでどおり Loading・Download だけを単体で動かせる。
    /// Loading 用：Close で閉じ、Open（待機が終わる）・Fail（失敗で終わる）・Cancel（キャンセルで終わる）で WaitUntilOpenAsync の待機を終わらせる。
    /// Download 用：BeginInitializing で「初期化中」にし、CompleteInitialization・FailInitialization・CancelInitialization で
    /// WaitUntilInitializedAsync の待機を終わらせる。InitializationException を設定すると、待たずにその例外で失敗する（未開始・失敗済みの再現）。
    /// </summary>
    internal sealed class FakeInitializationGate : IInitializationGate
    {
        private TaskCompletionSource<bool> _source = new();
        private TaskCompletionSource<bool> _initializedSource = new();

        /// <summary> WaitUntilOpenAsync が呼ばれた回数（ゲートが開いているときは呼ばれないはず） </summary>
        internal int WaitCallCount { get; private set; }

        internal bool IsOpen { get; private set; } = true;

        /// <summary> WaitUntilInitializedAsync が呼ばれた回数 </summary>
        internal int WaitInitializedCallCount { get; private set; }

        /// <summary> 初期化が成功済みか。false の間、WaitUntilInitializedAsync は待つ（または InitializationException で失敗する） </summary>
        internal bool IsInitialized { get; private set; } = true;

        /// <summary> 設定すると、初期化が成功していない間、WaitUntilInitializedAsync が待たずにこの例外で失敗する（未開始・失敗済みの再現） </summary>
        internal Exception InitializationException { get; set; }

        public bool AccIsOpen => IsOpen;

        public Task WaitUntilOpenAsync(CancellationToken ct)
        {
            WaitCallCount++;
            return _source.Task.WithCancellation(ct); // 待っている間も、呼び出し元のキャンセルは受け付ける
        }

        public Task WaitUntilInitializedAsync(CancellationToken ct)
        {
            WaitInitializedCallCount++;
            if (IsInitialized) return Task.CompletedTask;
            if (InitializationException != null) return Task.FromException(InitializationException);
            return _initializedSource.Task.WithCancellation(ct);
        }

        /// <summary> ゲートを閉じる。以降の Load は、Open / Fail / Cancel まで待つ </summary>
        internal void Close()
        {
            IsOpen = false;
            _source = new TaskCompletionSource<bool>();
        }

        /// <summary> ゲートを開き、待っている Load を再開させる </summary>
        internal void Open()
        {
            IsOpen = true;
            _source.TrySetResult(true);
        }

        /// <summary> 待っている Load を、指定の例外で終わらせる（ゲートは閉じたまま。初期化の失敗の再現） </summary>
        internal void Fail(Exception exception) => _source.TrySetException(exception);

        /// <summary> 待っている Load を、キャンセルで終わらせる（Manager の破棄で、共有タスクがキャンセルされた場合の再現） </summary>
        internal void Cancel() => _source.TrySetCanceled();

        /// <summary> 初期化中にする。以降の WaitUntilInitializedAsync は、CompleteInitialization / FailInitialization / CancelInitialization まで待つ </summary>
        internal void BeginInitializing()
        {
            IsInitialized = false;
            InitializationException = null;
            _initializedSource = new TaskCompletionSource<bool>();
        }

        /// <summary> 初期化を成功させ、待っている呼び出し元を再開させる </summary>
        internal void CompleteInitialization()
        {
            IsInitialized = true;
            _initializedSource.TrySetResult(true);
        }

        /// <summary> 待っている呼び出し元を、指定の例外で終わらせる（初期化の失敗の再現。初期化は成功しないまま） </summary>
        internal void FailInitialization(Exception exception) => _initializedSource.TrySetException(exception);

        /// <summary> 待っている呼び出し元を、キャンセルで終わらせる（Manager の破棄で、共有タスクがキャンセルされた場合の再現） </summary>
        internal void CancelInitialization() => _initializedSource.TrySetCanceled();
    }
}
