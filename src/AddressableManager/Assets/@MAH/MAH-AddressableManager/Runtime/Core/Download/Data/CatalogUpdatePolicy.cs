namespace MAH.AddressableManager.Core.Download
{
    /// <summary> CheckUpdateAsync がカタログを確認・更新するかの方針。HasUpdateAsync は、どの方針でもカタログを更新しない </summary>
    internal enum CatalogUpdatePolicy
    {
        /// <summary> リモートサーバーへのカタログの更新確認も、更新もしない（通信しない）。今のカタログで件数と容量だけを数える </summary>
        None,

        /// <summary> リモートサーバーに更新の有無を問い合わせるだけで、更新はしない（UI 表示用）。件数と容量は今のカタログに対する値 </summary>
        CheckOnly,

        /// <summary> 調べて、更新のあるカタログを更新する </summary>
        CheckAndUpdate,
    }
}
