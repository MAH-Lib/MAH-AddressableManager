using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// アドレスの重複。別々のアセットに、同じアドレスが付くこと。登録はするが、エラーとして知らせる。
    /// Unity の型に依存しない。
    /// </summary>
    internal sealed class RegistrationDuplicate
    {
        private readonly string _address;
        private readonly List<RegistrationEntryInput> _entries;

        /// <summary> 重複しているアドレス </summary>
        internal string AccAddress => _address;
        /// <summary> そのアドレスを持つアセットとグループ（この計画を適用した後の状態） </summary>
        internal IReadOnlyList<RegistrationEntryInput> AccEntries => _entries;

        internal RegistrationDuplicate(string address, List<RegistrationEntryInput> entries)
        {
            _address = address;
            _entries = entries;
        }
    }
}
