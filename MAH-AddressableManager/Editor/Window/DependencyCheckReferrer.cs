using System;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// 未登録の子を使っている、登録済みの親（パスとグループ名）。
    /// 親が子を直接使っているか、登録済みの別のアセット（経由先）を通して使っているかも持つ。
    /// readonly struct は Unity がシリアライズできず、再コンパイルで結果が消えるため、[Serializable] のクラスにしている。
    /// </summary>
    [Serializable]
    internal sealed class DependencyCheckReferrer
    {
        [SerializeField] private string _path;
        [SerializeField] private string _groupName;
        [SerializeField] private string _viaPath;
        [SerializeField] private int _viaCount;

        /// <summary> 親のアセットのパス </summary>
        internal string AccPath => _path;
        /// <summary> 親が属する Addressables グループ名 </summary>
        internal string AccGroupName => _groupName;
        /// <summary> 経由先（登録済みのアセット）のうち、パス順で最初の1つ。直接の使用のときは空 </summary>
        internal string AccViaPath => _viaPath;
        /// <summary> 経由先の数。直接の使用のときは 0 </summary>
        internal int AccViaCount => _viaCount;
        /// <summary> 登録済みの別のアセットを経由した使用（直接ではない） </summary>
        internal bool AccIsVia => _viaCount > 0;

        /// <summary> via_count が 0 のときは、親が子を直接使っている </summary>
        internal DependencyCheckReferrer(string path, string group_name, string via_path = "", int via_count = 0)
        {
            _path = path;
            _groupName = group_name;
            _viaPath = via_path ?? string.Empty;
            _viaCount = via_count;
        }
    }
}
