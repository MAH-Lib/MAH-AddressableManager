namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// 既存のエントリ1つ分（アセットパス・アドレス・グループ名）。アドレスの重複を調べるため、すべてのエントリを渡すときに使う。
    /// Unity の型に依存しない。
    /// </summary>
    internal sealed class RegistrationEntryInput
    {
        private readonly string _assetPath;
        private readonly string _address;
        private readonly string _groupName;

        /// <summary> アセットパス </summary>
        internal string AccAssetPath => _assetPath;
        /// <summary> アドレス </summary>
        internal string AccAddress => _address;
        /// <summary> グループ名 </summary>
        internal string AccGroupName => _groupName;

        internal RegistrationEntryInput(string asset_path, string address, string group_name)
        {
            _assetPath = (asset_path ?? string.Empty).Replace('\\', '/');
            _address = address ?? string.Empty;
            _groupName = group_name ?? string.Empty;
        }
    }
}
