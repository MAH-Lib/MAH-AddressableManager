using System;
using System.Threading;
using System.Threading.Tasks;

namespace MAH.AddressableManager.Core.Initialize
{
    /// <summary>
    /// Addressables の初期化の実行と状態遷移を担う Controller。IInitializationGate を実装し、Loading へ「読み込んでよいか」を伝える。
    /// 状態データは InitializeStateModel（Manager から注入）、Addressables の呼び出しは Service に任せる。
    /// 初期化の失敗は致命的（Addressables は再起動までやり直せない）なため、リトライはせず、Failed の後に Addressables を呼ばない。
    /// LoadingController など他の Controller は参照しない。
    /// </summary>
    internal sealed class InitializeController : IInitializationGate, IDisposable
    {
        // 破棄後に失敗した Service のタスクを、未観測の例外にしないための delegate（キャッシュ）
        private static readonly Action<Task> OBSERVE_FAULT = task => { _ = task.Exception; };

        private readonly AddressableInitializeSettings _settings;
        private readonly InitializeStateModel _stateModel;
        private readonly IAddressablesInitializeService _service;
        private readonly CancellationTokenSource _disposeCts = new(); // 初期化そのものを止められるのは、Dispose だけ
        private TaskCompletionSource<bool> _sharedSource; // 初期化1回分の共有タスク。1回だけ作る
        private bool _isDisposed;

        /// <summary> 初期化に成功したときに呼ばれる（Manager が公開イベントへ中継する） </summary>
        internal event Action OnSucceeded;
        /// <summary> 初期化に失敗したときに呼ばれる（1回だけ。Manager が公開イベントへ中継する） </summary>
        internal event Action<AddressableInitializeException> OnFailed;

        /// <summary> 初期化の状態 </summary>
        internal InitializeState AccState => _stateModel.AccState;
        /// <summary> 初期化に成功しているか </summary>
        internal bool AccIsInitialized => _stateModel.AccState == InitializeState.Succeeded;

        /// <summary> 待たずに Load してよいか。初期化に成功済み、または PassThrough で失敗していない間は true </summary>
        public bool AccIsOpen => _stateModel.AccState == InitializeState.Succeeded
            || (_stateModel.AccState != InitializeState.Failed && _settings.AccLoadBeforeInitialize == LoadBeforeInitializePolicy.PassThrough);

        internal InitializeController(
            AddressableInitializeSettings settings,
            InitializeStateModel state_model,
            IAddressablesInitializeService service)
        {
            _settings = settings;
            _stateModel = state_model;
            _service = service;
        }

        /// <summary>
        /// 初期化を行う。NotStarted なら始め、Running なら実行中の処理を待ち、Succeeded ならすぐ完了し、Failed ならすぐ同じ例外を投げる。
        /// ct で打ち切られるのは、この呼び出し元の待機だけ。
        /// </summary>
        internal async Task InitializeAsync(IProgress<float> progress, CancellationToken ct)
        {
            ThrowIfDisposed();
            switch (_stateModel.AccState)
            {
                case InitializeState.Succeeded:
                    progress?.Report(1f); // 完了済みでも、完了時には必ず 1 を届ける
                    return;
                case InitializeState.Failed:
                    // やり直さず、失敗済みの共有タスクを待って、毎回同じ例外を返す。
                    // 例外を投げ直すと、そのたびに同じインスタンスのスタックトレースが上書きされるため、await で伝える
                    await _stateModel.AccSharedTask;
                    return;
            }

            // 通知先は、初期化を始める前に登録する。Addressables が初期化済みだと、開始と同時に完了し、完了時の 1 を通知してしまうため
            _stateModel.AddProgressTarget(progress);
            try
            {
                if (_stateModel.AccState == InitializeState.NotStarted)
                    StartInitialize(); // Addressables が初期化済みなら、この中で同期的に完了する

                // Running、または開始と同時に完了した場合。どちらも共有タスクを待てば、成功・失敗の結果が届く
                Task shared_task = _stateModel.AccSharedTask;
                await shared_task.WithCancellation(ct);
            }
            finally
            {
                _stateModel.RemoveProgressTarget(progress);
            }
        }

        /// <summary>
        /// 自動モードの開始。結果を待つ利用者はいないため、待たずに初期化を始め、失敗の例外だけを観測済みにする
        /// （失敗のログと状態遷移は RunAsync が行う。未観測の例外として TaskScheduler.UnobservedTaskException に流さないため）。
        /// Manager が Awake で、Tick の購読より後に呼ぶ
        /// </summary>
        internal void StartAutoInitialize()
        {
            ObserveFault(InitializeAsync(null, CancellationToken.None));
        }

        /// <summary> Load してよい状態になるまで待つ。未開始は InvalidOperationException、失敗は AddressableInitializeException </summary>
        public Task WaitUntilOpenAsync(CancellationToken ct) => WaitUntilSucceededAsync(ct);

        /// <summary>
        /// 初期化の成功まで待つ（Download 用）。LoadBeforeInitializePolicy には関係しない。
        /// 未開始は InvalidOperationException（初期化は始めない）、失敗は AddressableInitializeException
        /// </summary>
        public Task WaitUntilInitializedAsync(CancellationToken ct) => WaitUntilSucceededAsync(ct);

        // 初期化の成功まで待つ共通の処理。WaitUntilOpenAsync は、待たなくてよい場合（AccIsOpen が true）には呼ばれないため、
        // 呼ばれたときは「成功まで待つ」動作でそろう
        private Task WaitUntilSucceededAsync(CancellationToken ct)
        {
            switch (_stateModel.AccState)
            {
                case InitializeState.Succeeded:
                    return Task.CompletedTask;
                case InitializeState.Failed:
                    return Task.FromException(_stateModel.AccFailure);
                case InitializeState.NotStarted:
                    return Task.FromException(new InvalidOperationException(
                        "Addressables の初期化がまだ始まっていません。InitializeAsync を呼ぶか、初期化モードを Auto にしてください。"));
                default:
                    return _stateModel.AccSharedTask.WithCancellation(ct);
            }
        }

        /// <summary> Manager の LateUpdate から呼ばれる。初期化中だけ、待機中の呼び出し元へ進捗を通知する </summary>
        internal void Tick(float delta_time)
        {
            if (_stateModel.AccState != InitializeState.Running) return;
            _stateModel.ReportProgress(_service.GetPercentComplete());
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _disposeCts.Cancel();
            _sharedSource?.TrySetCanceled(); // 待機中の呼び出し元を、続行の順序に頼らず確実に OperationCanceledException で終わらせる。状態は変えない
            if (!_settings.AccAutoReleaseHandle)
                _service.ReleaseHandle();
            _disposeCts.Dispose();
        }

        // 共有タスクを作って Running にしてから、Service を呼ぶ。同期で完了しても、Running を経由してから遷移する順序を守る
        private void StartInitialize()
        {
            _sharedSource = new TaskCompletionSource<bool>();
            _stateModel.MarkRunning(_sharedSource.Task);
            _ = RunAsync(); // 例外は RunAsync の中で状態・共有タスクへ流すため、ここでは待たない
        }

        private async Task RunAsync()
        {
            Task service_task = null;
            try
            {
                service_task = _service.StartInitialize(_settings.AccAutoReleaseHandle);
                await service_task.WithCancellation(_disposeCts.Token);
            }
            catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
            {
                // Dispose による中断。状態は変えず（破棄後の状態は変化しない）、待機中の呼び出し元だけを終わらせる
                _sharedSource.TrySetCanceled();
                ObserveFault(service_task);
                return;
            }
            catch (Exception exception)
            {
                var failure = new AddressableInitializeException("Addressables の初期化に失敗しました。", exception);
                _stateModel.MarkFailed(failure);
                AddressableLogger.LogError($"Addressables の初期化に失敗しました。アプリを再起動するまで、Addressables は使えません。{exception.Message}");
                RaiseFailed(failure);
                _sharedSource.TrySetException(failure); // 待っている全員に同じ例外を届ける
                return;
            }

            _stateModel.MarkSucceeded();
            _stateModel.ReportProgress(1f); // 完了時には、待っている全員に必ず 1 を届ける
            RaiseSucceeded();
            _sharedSource.TrySetResult(true);
        }

        // イベントの購読側の例外で、状態遷移や共有タスクの完了を止めないよう、ここで受け止めてログにする
        private void RaiseSucceeded()
        {
            try
            {
                OnSucceeded?.Invoke();
            }
            catch (Exception exception)
            {
                AddressableLogger.LogException(exception);
            }
        }

        private void RaiseFailed(AddressableInitializeException failure)
        {
            try
            {
                OnFailed?.Invoke(failure);
            }
            catch (Exception exception)
            {
                AddressableLogger.LogException(exception);
            }
        }

        // Dispose で待つのをやめた Service のタスクが、後から失敗しても、未観測の例外にならないようにする
        private static void ObserveFault(Task task)
        {
            task?.ContinueWith(OBSERVE_FAULT, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(InitializeController));
        }
    }
}
