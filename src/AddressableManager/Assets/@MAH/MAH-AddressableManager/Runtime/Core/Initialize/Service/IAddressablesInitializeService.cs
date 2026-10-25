using System.Threading.Tasks;

namespace MAH.AddressableManager.Core.Initialize
{
    /// <summary>
    /// Addressables.InitializeAsync の1回分の呼び出しと、その結果の受け渡し。
    /// EditMode テストで、失敗・同期の例外・進捗を偽物で再現するためにインターフェースを挟む。
    /// 戻り値は、Task / UniTask のシンボルに関係なく常に Task（共有タスクの元になるため）。
    /// </summary>
    internal interface IAddressablesInitializeService
    {
        /// <summary>
        /// 初期化を始める。成功なら完了した Task、失敗なら例外で終わる Task を返す。
        /// Addressables が同期で投げた例外も、投げずに失敗した Task として返す。1つの Service につき1回だけ呼ぶ。
        /// </summary>
        Task StartInitialize(bool auto_release_handle);

        /// <summary> 初期化の進捗（0〜1）。完了後は 1 を返す </summary>
        float GetPercentComplete();

        /// <summary> 保持している handle を解放する（autoReleaseHandle が false のときだけ保持している）。無ければ何もしない </summary>
        void ReleaseHandle();
    }
}
