namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary> 手動一括登録の一覧に出すアセットの状態（仕様 5.3） </summary>
    internal enum ManualRegistrationState
    {
        /// <summary> 未登録。既定でチェックがオン </summary>
        Unregistered = 0,
        /// <summary> 登録済み（手動・他のツールで登録したもの）。既定でチェックがオフ。オンにするとグループの移動とアドレスの書き換えを行う </summary>
        Registered = 1,
        /// <summary> 自動登録のルールに当たり、ルールの内容と一致している。変更しない </summary>
        AutoRegistered = 2,
        /// <summary> 自動登録のルールが競合している。変更しない（先に競合を直す） </summary>
        Conflicting = 3,
        /// <summary> 自動登録のルールに当たるが、まだ当て直していない。変更しない（ルール一覧のウィンドウで当て直す） </summary>
        PendingAuto = 4,
    }
}
