using System;
using System.Collections.Generic;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// 一覧の1行（未登録の子1つ）。ウィンドウが保持し、再コンパイルで消えないよう [Serializable] のクラスにしている
    /// （readonly struct は Unity がシリアライズできない）。
    /// </summary>
    [Serializable]
    internal sealed class DependencyCheckRow
    {
        [SerializeField] private string _path;
        [SerializeField] private string _name;
        [SerializeField] private string _typeName;
        [SerializeField] private long _sizeBytes;
        [SerializeField] private List<DependencyCheckReferrer> _referrers;
        [SerializeField] private int _groupCount;
        [SerializeField] private bool _isResources;
        [SerializeField] private bool _isPackSeparately;
        [SerializeField] private bool _isHiddenByDefault;

        /// <summary> アセットのパス </summary>
        internal string AccPath => _path;
        /// <summary> ファイル名 </summary>
        internal string AccName => _name;
        /// <summary> メインアセットの型名（取得できなければ Unknown） </summary>
        internal string AccTypeName => _typeName;
        /// <summary> ディスク上のファイルサイズ（byte。取得できなければ 0） </summary>
        internal long AccSizeBytes => _sizeBytes;
        /// <summary> 参照元の親（パス順に入れる） </summary>
        internal List<DependencyCheckReferrer> AccReferrers => _referrers;
        /// <summary> 参照元のグループ数（同じグループは1つと数える） </summary>
        internal int AccGroupCount => _groupCount;
        /// <summary> 2つ以上のグループの親から使われている（Group の分け方を見直す手がかり。ビルドでの重複を表すわけではない） </summary>
        internal bool AccIsMultiGroup => _groupCount >= 2;
        /// <summary> パスに /Resources/ を含む </summary>
        internal bool AccIsResources => _isResources;
        /// <summary> 参照元に Pack Separately のグループの親を含む（ビルド時の参考。エントリごとに別バンドルになるため、同じグループの中でも重複して入り得る） </summary>
        internal bool AccIsPackSeparately => _isPackSeparately;
        /// <summary> 既定で隠す（.cs・.dll・Packages/ 配下） </summary>
        internal bool AccIsHiddenByDefault => _isHiddenByDefault;

        internal DependencyCheckRow(
            string path, string name, string type_name, long size_bytes, List<DependencyCheckReferrer> referrers,
            int group_count, bool is_resources, bool is_pack_separately, bool is_hidden_by_default)
        {
            _path = path;
            _name = name;
            _typeName = type_name;
            _sizeBytes = size_bytes;
            _referrers = referrers;
            _groupCount = group_count;
            _isResources = is_resources;
            _isPackSeparately = is_pack_separately;
            _isHiddenByDefault = is_hidden_by_default;
        }
    }
}
