namespace MAH.AddressableManager.Core.Initialize
{
    /// <summary> 初期化を始めるタイミング </summary>
    internal enum InitializeMode
    {
        /// <summary> Manager の Awake で初期化を始める </summary>
        Auto,

        /// <summary> InitializeAsync が呼ばれるまで始めない </summary>
        Manual,
    }
}
