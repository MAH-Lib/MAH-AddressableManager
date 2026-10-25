namespace MAH.AddressableManager
{
    /// <summary> 寿命の種類 </summary>
    public enum LifetimeKind
    {
        /// <summary> 自動回収なし。永続アセット・フェーズ単位の管理などはこちら。Manager.Dispose時のみ強制解放される </summary>
        Manual,

        /// <summary> Load時のシーンのUnloadで回収される </summary>
        Scene,

        /// <summary> 指定したGameObjectの破棄で回収される </summary>
        Owner,
    }
}
