namespace MAH.AddressableManager.Core.Initialize
{
    /// <summary> 初期化が成功する前に Load が呼ばれたときの扱い。初期化に失敗した後は、どちらでも Load を止める </summary>
    internal enum LoadBeforeInitializePolicy
    {
        /// <summary> 待たずに読み込む（Addressables が最初のロード時に自動で初期化する） </summary>
        PassThrough,

        /// <summary> 初期化の完了を待ってから読み込む。未開始（手動モード）なら InvalidOperationException </summary>
        WaitForInitialize,
    }
}
