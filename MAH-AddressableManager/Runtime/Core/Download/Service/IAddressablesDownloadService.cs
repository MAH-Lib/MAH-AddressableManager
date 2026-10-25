using System.Collections.Generic;
#if ADDRESSABLE_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#else
using System.Threading.Tasks;
#endif

namespace MAH.AddressableManager.Core.Download
{
    /// <summary>
    /// Addressables のカタログ確認・更新・サイズ計算・ダウンロードの呼び出し窓口。Addressables の API を呼ぶのは、この Service だけ。
    /// EditMode テストで、失敗・進捗・件数を偽物で再現するためにインターフェースを挟む。
    /// 戻り値は、UniTask が導入されていれば UniTask、無ければ Task（共有タスクではないため、DownloadController と同じ出し分け）。
    /// 呼び出し元のキャンセルは、DownloadController が戻り値の待機に WithCancellation を付けて行うため、ここでは ct を受け取らない。
    /// </summary>
    internal interface IAddressablesDownloadService
    {
        /// <summary>
        /// リモートのカタログを確認し、更新のあるカタログの ID を返す（更新が無ければ空）。カタログは更新しない。
        /// 失敗は例外で返す（一部のカタログの確認だけが失敗した場合も失敗として扱う）
        /// </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        UniTask<IReadOnlyList<string>> CheckForCatalogUpdatesAsync();
#else
        Task<IReadOnlyList<string>> CheckForCatalogUpdatesAsync();
#endif

        /// <summary> 指定したカタログを更新する。catalog_ids は必ず明示する（null や空では呼ばない。引数なしの更新は、別の確認で立ったフラグの分まで更新するため） </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        UniTask UpdateCatalogsAsync(IReadOnlyList<string> catalog_ids, bool auto_clean_bundle_cache);
#else
        Task UpdateCatalogsAsync(IReadOnlyList<string> catalog_ids, bool auto_clean_bundle_cache);
#endif

        /// <summary>
        /// 対象 Label のうち、ダウンロードが必要なバンドルの数と合計バイト数を数える。
        /// Addressables.GetDownloadSizeAsync と同じ判定（サイズが 0 より大きいバンドルを重複なく数える）で、バンドルの数もあわせて返す
        /// </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        UniTask<DownloadSizeInfo> CalculateDownloadSizeAsync(IReadOnlyList<string> labels);
#else
        Task<DownloadSizeInfo> CalculateDownloadSizeAsync(IReadOnlyList<string> labels);
#endif

        /// <summary>
        /// 対象 Label の依存バンドルをダウンロードし、完了（成功・失敗）まで待つ。バンドルをキャッシュに保存するだけで、アセットは読み込まない。
        /// 完了したら handle を解放する。呼び出し元が待機をやめるとき（キャンセル・破棄）は <see cref="ReleaseDownloadHandle"/> で解放する。
        /// 同時に始められるのは1つだけ（実行中に呼ぶと InvalidOperationException）
        /// </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        UniTask StartDownloadAsync(IReadOnlyList<string> labels);
#else
        Task StartDownloadAsync(IReadOnlyList<string> labels);
#endif

        /// <summary>
        /// ダウンロード中のバイト数を読む。ダウンロード中でなければ、最後に読んだ値（完了後は合計と同じ値）を返す。
        /// handle の状態確認を伴うため、アクセサ（Acc）ではなくメソッドにしている。毎フレーム呼ばれる
        /// </summary>
        void GetDownloadBytes(out long downloaded_bytes, out long total_bytes);

        /// <summary> ダウンロードの handle を解放する（保持していなければ何もしない）。開始済みの通信は、止まらない場合がある </summary>
        void ReleaseDownloadHandle();
    }
}
