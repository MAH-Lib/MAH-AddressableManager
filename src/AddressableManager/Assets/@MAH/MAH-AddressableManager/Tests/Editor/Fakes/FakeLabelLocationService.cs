using System;
using System.Collections.Generic;
using System.Threading;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Core.Loading;
#if ADDRESSABLE_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#else
using System.Threading.Tasks;
#endif
using SystemTask = System.Threading.Tasks.Task;
using SystemTaskCompletionSource = System.Threading.Tasks.TaskCompletionSource<bool>;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// ILabelLocationService の偽実装。実際の Addressables を呼ばず、テストから Label ごとのロケーションと失敗を設定できる。
    /// HoldResolve で、ロケーションの解決を完了させずに止めておける（解決中のキャンセルや破棄のテスト用）。
    /// </summary>
    internal sealed class FakeLabelLocationService : ILabelLocationService
    {
        private readonly Dictionary<string, List<LabelLocation>> _locationsByLabel = new();
        private readonly Dictionary<string, Exception> _failuresByLabel = new();
        private SystemTaskCompletionSource _gate;

        /// <summary> ResolveAsync が呼ばれた回数 </summary>
        internal int ResolveCallCount { get; private set; }

        /// <summary> 指定 Label のロケーションを設定する（Label を設定しなければ、0件として返す） </summary>
        internal void SetLocations(string label, params LabelLocation[] locations)
            => _locationsByLabel[label] = new List<LabelLocation>(locations);

        /// <summary> 指定 Label の解決を失敗させる </summary>
        internal void SetFailure(string label, Exception exception) => _failuresByLabel[label] = exception;

        /// <summary> 以降の ResolveAsync を、ReleaseResolve が呼ばれるまで完了させない </summary>
        internal void HoldResolve() => _gate = new SystemTaskCompletionSource();

        /// <summary> HoldResolve で止めていた ResolveAsync を完了させる </summary>
        internal void ReleaseResolve() => _gate?.TrySetResult(true);

#if ADDRESSABLE_UNITASK_SUPPORT
        public async UniTask ResolveAsync(string label, List<LabelLocation> result, CancellationToken ct)
#else
        public async Task ResolveAsync(string label, List<LabelLocation> result, CancellationToken ct)
#endif
        {
            ResolveCallCount++;
            if (_gate != null)
                await ((SystemTask)_gate.Task).WithCancellation(ct); // 止めている間も、キャンセルは受け付ける

            if (_failuresByLabel.TryGetValue(label, out var exception))
                throw exception;
            if (_locationsByLabel.TryGetValue(label, out var locations))
                result.AddRange(locations);
        }
    }
}
