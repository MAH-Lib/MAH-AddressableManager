namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary> アセットの移動・名前の変更に対するアドレスの扱い </summary>
    internal enum RegistrationAddressMode
    {
        /// <summary> 移動・名前の変更のたびに、アドレスを展開し直す（既定） </summary>
        Follow = 0,
        /// <summary> 一度登録したら、アドレスを変えない（グループ・Label は当て直す） </summary>
        Fixed = 1,
    }
}
