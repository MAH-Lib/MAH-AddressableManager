using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MAH.AddressableManager
{
    /// <summary>
    /// Label 一括ロード1回ごとに発行される、まとめチケット。読み込んだアセットを持ち、Release 1回でまとめて解放される。
    /// Load のときに型は指定せず、型は取り出すとき（TryGetAsset / GetAssets）に指定する。取り出せるのは、読み込んだ型が T と完全一致するものだけ。
    /// 取り出し結果のキャッシュと「解放済み」の印という変化する状態を持つため、struct ではなく class にしている。
    /// </summary>
    public sealed class AssetGroupTicket
    {
        private readonly string _label;
        private readonly int _count;
        private string[] _addresses;
        private Type[] _assetTypes;
        private UnityEngine.Object[] _assets;
        private long[] _serials;
        private Dictionary<Type, object> _assetListByType; // 型ごとの取り出し結果のキャッシュ（初回の取り出しで作る）
        private bool _isReleased;
#if UNITY_EDITOR
        private readonly string _stackTrace;
#endif

        /// <summary> 読み込んだ Label </summary>
        public string AccLabel => _label;
        /// <summary> 中身の件数 </summary>
        public int AccCount => _count;
        /// <summary> 解放済み（明示解放または寿命による回収）で、中身が使えない状態か </summary>
        public bool AccIsReleased => _isReleased;
#if UNITY_EDITOR
        /// <summary> 確保時のスタックトレース。リーク警告用。まとめチケット1枚につき1回だけ取得する（#if UNITY_EDITOR のため本番ビルドには含まれない） </summary>
        internal string AccStackTrace => _stackTrace;
#endif

        /// <summary> count は中身の件数。中身（アセット等）は、全件の確保が終わった後に Fill で詰める </summary>
        internal AssetGroupTicket(string label, int count)
        {
            _label = label;
            _count = count;
#if UNITY_EDITOR
            _stackTrace = Environment.StackTrace;
#endif
        }

        /// <summary>
        /// そのアドレス（PrimaryKey）で、読み込んだ型が T と完全一致するアセットがあれば true を返す。無ければ false と null。
        /// 解放済みのときも false を返す。
        /// </summary>
        public bool TryGetAsset<T>(string address, out T asset) where T : UnityEngine.Object
        {
            asset = null;
            if (_isReleased || _assets == null || address == null)
                return false;

            var asset_type = typeof(T);
            for (int i = 0; i < _assets.Length; i++)
            {
                if (_assetTypes[i] != asset_type || _addresses[i] != address)
                    continue;
                asset = (T)_assets[i];
                return true;
            }
            return false;
        }

        /// <summary>
        /// 読み込んだ型が T と完全一致するアセットをすべて返す。基底型では探さない（Object を指定すると何も返らない）。
        /// 型ごとの初回の呼び出しで結果を作ってキャッシュし、2回目以降は同じインスタンスを返す（確保しない）。
        /// 該当が0件、または解放済みのときは空を返す。
        /// IReadOnlyList の foreach は列挙子がボックス化されるため、for と Count / インデクサで回すこと。
        /// </summary>
        public IReadOnlyList<T> GetAssets<T>() where T : UnityEngine.Object
        {
            if (_isReleased || _assets == null)
                return Array.Empty<T>();

            var asset_type = typeof(T);
            if (_assetListByType != null && _assetListByType.TryGetValue(asset_type, out var cached))
                return (IReadOnlyList<T>)cached;

            List<T> list = null;
            for (int i = 0; i < _assets.Length; i++)
            {
                if (_assetTypes[i] != asset_type)
                    continue;
                list ??= new List<T>();
                list.Add((T)_assets[i]);
            }
            if (list == null)
                return Array.Empty<T>(); // 該当なしは確保もキャッシュもしない（Array.Empty は共有の空配列）

            // List<T> にキャストして書き換えられると次回以降の結果も壊れるため、ReadOnlyCollection で包んで返す
            var read_only = new ReadOnlyCollection<T>(list);
            _assetListByType ??= new Dictionary<Type, object>();
            _assetListByType[asset_type] = read_only;
            return read_only;
        }

        /// <summary>
        /// 全件の確保が終わった後に、中身（アドレス・型・アセット・通し番号）を詰める。LoadingController から呼ぶ。
        /// 渡した配列は、コピーせずにそのまま保持する（呼び出し側は、以後書き換えない）。
        /// Loading 機能の型（AssetEntryKey 等）を知らずに済むよう、配列で受け取る。
        /// </summary>
        internal void Fill(string[] addresses, Type[] asset_types, UnityEngine.Object[] assets, long[] serials)
        {
            var count = serials.Length;
            if (count != _count)
                throw new ArgumentException($"件数が一致しません（まとめチケット：{_count}、渡された通し番号：{count}）。", nameof(serials));
            if (addresses.Length != count || asset_types.Length != count || assets.Length != count)
                throw new ArgumentException($"配列の長さが一致しません（通し番号：{count}、アドレス：{addresses.Length}、型：{asset_types.Length}、アセット：{assets.Length}）。");
            _addresses = addresses;
            _assetTypes = asset_types;
            _assets = assets;
            _serials = serials;
        }

        internal long GetSerial(int index) => _serials[index];

        /// <summary> 解放済みにして、取り出し結果のキャッシュと中身への参照を破棄する。明示解放と寿命による回収の両方から呼ぶ </summary>
        internal void Invalidate()
        {
            _isReleased = true;
            _assetListByType = null;
            _assets = null;
        }
    }
}
