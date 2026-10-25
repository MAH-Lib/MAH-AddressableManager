namespace MAH.AddressableManager.Samples
{
    /// <summary>
    /// サンプルで試す寿命の種類。寿命ごとに別のアセットを読み込み、どの寿命のものが残っているか見分けられるようにする。
    /// Owner 寿命は GameObject の破棄に紐づくため、Prefab シーンだけで試す（このenumには含めない）。
    /// </summary>
    public enum SampleLifetimeKind
    {
        /// <summary> Release を呼ぶまで解放されない寿命 </summary>
        Manual,
        /// <summary> 読み込んだシーンがアンロードされたときに自動回収される寿命 </summary>
        Scene,
    }
}
