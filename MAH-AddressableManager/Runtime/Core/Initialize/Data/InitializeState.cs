namespace MAH.AddressableManager
{
    /// <summary> Addressables の初期化の進行状態。Succeeded / Failed は終端で、以降は変化しない </summary>
    public enum InitializeState
    {
        /// <summary> まだ初期化を始めていない（手動モードで InitializeAsync が呼ばれる前） </summary>
        NotStarted,

        /// <summary> 初期化中 </summary>
        Running,

        /// <summary> 初期化に成功した </summary>
        Succeeded,

        /// <summary> 初期化に失敗した。Addressables はアプリを再起動するまで初期化をやり直せないため、やり直しはしない </summary>
        Failed,
    }
}
