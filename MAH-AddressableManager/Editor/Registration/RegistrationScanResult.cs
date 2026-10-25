namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary> Scanner がアセットを集めた結果 </summary>
    internal enum RegistrationScanResult
    {
        /// <summary> 最後まで集めた </summary>
        Completed = 0,
        /// <summary> 進捗バーのキャンセルで打ち切った（結果は使わない） </summary>
        Canceled = 1,
        /// <summary> Addressables の設定が無い </summary>
        NoSettings = 2,
    }
}
