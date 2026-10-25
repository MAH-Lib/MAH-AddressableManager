namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary> 自動登録のログの細かさ </summary>
    internal enum RegistrationLogDetail
    {
        /// <summary> 1回の処理で N 件登録・M 件変更、のまとめだけを出す </summary>
        Summary = 0,
        /// <summary> アセットごとに1件ずつ出す </summary>
        PerAsset = 1,
    }
}
