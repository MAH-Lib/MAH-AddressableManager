namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary> ルールの対象の指定方法 </summary>
    internal enum RegistrationTargetMode
    {
        /// <summary> 指定した拡張子だけを対象にする </summary>
        Extensions = 0,
        /// <summary> 登録できるものすべてを対象にする（除外する拡張子を指定できる） </summary>
        All = 1,
    }
}
