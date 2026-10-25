using System;
using System.Collections.Generic;
using System.Threading;
#if ADDRESSABLE_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#else
using System.Threading.Tasks;
#endif

namespace MAH.AddressableManager
{
    /// <summary>
    /// 初期化・ダウンロード・読み込み・解放の窓口。利用者が触れる唯一の入口。責務は呼び出し関数のみで、状態は持たない。
    /// Manager 経由で取得する（AddressableManager.AccInstance.AccHandler）。
    /// </summary>
    public interface IAddressableHandler
    {
        #region 初期化

        /// <summary> 初期化の状態 </summary>
        InitializeState AccInitializeState { get; }

        /// <summary> 初期化に成功しているか </summary>
        bool AccIsInitialized { get; }

        /// <summary>
        /// 初期化に成功したときに呼ばれる。
        /// 自動モードでは Manager の Awake で始まり、購読する前に完了している場合がある（Addressables が初期化済みなら Awake の中で完了する）。購読時に AccIsInitialized も確認すること
        /// </summary>
        event Action OnInitialized;

        /// <summary> 初期化に失敗したときに呼ばれる（1回だけ。失敗した後は初期化をやり直せない） </summary>
        event Action<AddressableInitializeException> OnInitializeFailed;

        /// <summary>
        /// 初期化を行う。未開始なら始めて完了まで待ち、実行中なら実行中の処理を待ち（新しく始めない）、成功済みならすぐ完了し、
        /// 失敗済みなら（やり直さずに）すぐ AddressableInitializeException を投げる。
        /// ct で打ち切られるのは、この呼び出し元の待機だけで、初期化そのものは止まらない。progress には待っている間の進捗（0〜1）が届く。
        /// </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        UniTask InitializeAsync(IProgress<float> progress = null, CancellationToken ct = default);
#else
        Task InitializeAsync(IProgress<float> progress = null, CancellationToken ct = default);
#endif

        #endregion

        #region ダウンロード

        // Label 単位。確認・判定・ダウンロードは、同時に1つしか実行できない（実行中に呼ぶと InvalidOperationException）。
        // Label を渡さない版は、Runtime SO の「ダウンロード」の Label を対象にする（空なら InvalidOperationException）。Label を渡す版は、そちらを優先する。
        // どれも、初期化の成功を待ってから始まる（手動モードで初期化が始まっていなければ InvalidOperationException、失敗していれば AddressableInitializeException）。
        // Label を渡す版に null を渡すと ArgumentNullException になる（呼び出し時ではなく、await の時点で届く）。
        // DownloadAsync(null) は、IProgress 版と IReadOnlyList 版のどちらにも当てはまり、コンパイルエラーになる。進捗だけを渡すときは progress: を、Label だけを渡すときは labels: を付ける。

        /// <summary>
        /// 取得した時点の、最新の処理（確認・判定・ダウンロード）の状態のコピー。値は後から変わらないため、必要なたびに取得し直す。
        /// 一度も処理していなければ AccPhase == None。処理中は AccIsDownloading 等で判定でき、取得し直すと毎フレーム更新された値が得られる。
        /// 進捗は、DownloadAsync の IProgress で受け取ることを推奨する。この取得は、IProgress を渡していない場所から状態を参照したいときの補助
        /// </summary>
        DownloadStatus AccDownloadStatus { get; }

        /// <summary>
        /// 更新データがあるかどうかだけを判定する。カタログは更新しない（SO の方針に関係なく UpdateCatalogs を呼ばない）。
        /// カタログに更新がある、または対象 Label にダウンロードが必要なバンドルがあれば true
        /// </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        UniTask<bool> HasUpdateAsync(CancellationToken ct = default);
        UniTask<bool> HasUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct = default);
#else
        Task<bool> HasUpdateAsync(CancellationToken ct = default);
        Task<bool> HasUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct = default);
#endif

        /// <summary>
        /// カタログを SO の方針に従って確認・更新し、更新されたカタログ数・ダウンロードが必要なバンドル数・合計バイト数を返す。
        /// 確認ダイアログに「N 件 / xx MB」を表示するために使う。
        /// カタログの更新中（UpdatingCatalog）は、Addressables の更新を途中で止められないため、ct のキャンセルは更新の完了後に反映される
        /// （OperationCanceledException・Canceled でも、カタログは更新済みになり得る）。Manager の破棄では、更新の完了を待たずに終わる
        /// </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        UniTask<DownloadCheckResult> CheckUpdateAsync(CancellationToken ct = default);
        UniTask<DownloadCheckResult> CheckUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct = default);
#else
        Task<DownloadCheckResult> CheckUpdateAsync(CancellationToken ct = default);
        Task<DownloadCheckResult> CheckUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct = default);
#endif

        /// <summary>
        /// 対象 Label のアセットをダウンロードする（バンドルをキャッシュに保存するだけで、アセットは読み込まない。カタログは更新しない）。
        /// progress には、段階・バイト数・割合・リトライ回数が届く。処理の終わりには、通知先を外す前に1回だけ、結果（Completed / Failed / Canceled）が届く。
        /// 失敗（リトライを使い切った）は AddressableDownloadException。ct のキャンセルは、呼び出し元の待機だけを OperationCanceledException で終わらせる
        /// （開始済みの通信は、止まらない場合がある）
        /// </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        UniTask DownloadAsync(IProgress<DownloadStatus> progress = null, CancellationToken ct = default);
        UniTask DownloadAsync(IReadOnlyList<string> labels, IProgress<DownloadStatus> progress = null, CancellationToken ct = default);
#else
        Task DownloadAsync(IProgress<DownloadStatus> progress = null, CancellationToken ct = default);
        Task DownloadAsync(IReadOnlyList<string> labels, IProgress<DownloadStatus> progress = null, CancellationToken ct = default);
#endif

        #endregion

        #region アセットの読み込み・解放

#if ADDRESSABLE_UNITASK_SUPPORT
        UniTask<AssetTicket<T>> LoadAsync<T>(AssetKey key, AssetLifetime lifetime, CancellationToken ct = default)
            where T : UnityEngine.Object;
#else
        Task<AssetTicket<T>> LoadAsync<T>(AssetKey key, AssetLifetime lifetime, CancellationToken ct = default)
            where T : UnityEngine.Object;
#endif

        void Release<T>(AssetTicket<T> ticket) where T : UnityEngine.Object;

        /// <summary>
        /// Label が付いたアセットをまとめて読み込み、まとめチケットを返す。Load のときに型は指定しない（Label にどの型が入っていてもよい）。
        /// 型は、まとめチケットから取り出すとき（TryGetAsset / GetAssets）に指定する。解放は <see cref="Release(AssetGroupTicket)"/> 1回でまとめて行う。
        /// 該当するアセットが無い Label は、警告を出して空のまとめチケットを返す。
        /// </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        UniTask<AssetGroupTicket> LoadByLabelAsync(string label, AssetLifetime lifetime, CancellationToken ct = default);
#else
        Task<AssetGroupTicket> LoadByLabelAsync(string label, AssetLifetime lifetime, CancellationToken ct = default);
#endif

        /// <summary> まとめチケットの中身をすべて解放する。解放済み・null のまとめチケットは、警告を出すだけで何もしない </summary>
        void Release(AssetGroupTicket ticket);

        #endregion
    }
}
