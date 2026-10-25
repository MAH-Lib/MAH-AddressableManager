namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary> パスだけで判定できる「いつも登録しないもの」の種別 </summary>
    internal enum RegistrationExclusionKind
    {
        /// <summary> 登録してよい </summary>
        None = 0,
        /// <summary> 黙って登録しない（スクリプト・アセンブリの定義・.meta・Assets の外） </summary>
        Ignored = 1,
        /// <summary> Resources フォルダの配下。登録せず、警告を出す </summary>
        Resources = 2,
    }
}
