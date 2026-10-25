namespace MAH.AddressableManager
{
    /// <summary>
    /// ダウンロード機能の処理の段階。Completed / Failed / Canceled は、次の処理が始まるまで残る。
    /// 先頭の None は default 値でもあり、default の DownloadStatus は「まだ何も処理していない」状態を表す
    /// </summary>
    public enum DownloadPhase
    {
        /// <summary> まだ何も処理していない（ダウンロードなし） </summary>
        None,

        /// <summary> カタログの更新確認中 </summary>
        CheckingCatalog,

        /// <summary> カタログの更新中 </summary>
        UpdatingCatalog,

        /// <summary> ダウンロードが必要なバンドルと容量を計算中 </summary>
        CalculatingSize,

        /// <summary> ダウンロード中。バイト数と割合が毎フレーム更新される </summary>
        Downloading,

        /// <summary> 直前の処理が成功した </summary>
        Completed,

        /// <summary> 直前の処理が失敗した（リトライを使い切った、初期化の待機で失敗した） </summary>
        Failed,

        /// <summary> 直前の処理がキャンセルされた（ct のキャンセル、または Manager の破棄） </summary>
        Canceled,
    }
}
