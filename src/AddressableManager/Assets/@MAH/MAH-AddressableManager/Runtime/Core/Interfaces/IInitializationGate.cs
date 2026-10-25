using System.Threading;
using System.Threading.Tasks;

namespace MAH.AddressableManager.Core
{
    /// <summary>
    /// Loading が「読み込んでよいか」を、Download が「初期化の成功まで待つ」を問い合わせる窓口。
    /// InitializeController が実装し、Manager が LoadingController・DownloadController に注入する。
    /// Initialize・Loading・Download から参照されるため、どの機能フォルダにも属さない Core 直下に置く。
    /// Task / UniTask の差の対象外（共有タスクは常に Task）のため、シンボルに関係なく Task を使う。
    /// </summary>
    internal interface IInitializationGate
    {
        /// <summary> 待たずに Load してよいか（PassThrough で初期化に失敗していない、または初期化に成功済み） </summary>
        bool AccIsOpen { get; }

        /// <summary> Load してよい状態になるまで待つ。未開始（WaitForInitialize）なら InvalidOperationException、失敗なら AddressableInitializeException </summary>
        Task WaitUntilOpenAsync(CancellationToken ct);

        /// <summary>
        /// 初期化の成功まで待つ（Download が使う）。LoadBeforeInitializePolicy に関係なく待つ。
        /// 成功済みならすぐ完了、実行中なら完了を待つ、未開始なら InvalidOperationException（初期化は始めない）、失敗なら AddressableInitializeException
        /// </summary>
        Task WaitUntilInitializedAsync(CancellationToken ct);
    }
}
