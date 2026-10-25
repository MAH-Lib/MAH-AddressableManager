#if UNITY_EDITOR
using System;
using MAH.AddressableManager;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// Registry の Entry 1件分の、取得した時点の値のコピー。取得後に Model が変わっても値は変わらない。
    /// 寿命スコープごとの確保内訳は、Entry ごとに配列を確保しないよう、別の List（ScopeCountSnapshot）にまとめて連結して持ち、
    /// この Snapshot は、その開始位置と件数だけを持つ。
    /// Editor のデバッグウィンドウへ渡すためのもので、#if UNITY_EDITOR のため本番ビルドには含まれない。
    /// </summary>
    internal readonly struct AssetEntrySnapshot
    {
        private readonly AssetKey _key;
        private readonly Type _assetType;
        private readonly string _assetName;
        private readonly int _refCount;
        private readonly AsyncOperationStatus _status;
        private readonly int _scopeCountStart;
        private readonly int _scopeCountLength;

        internal AssetKey AccKey => _key;
        internal Type AccAssetType => _assetType;
        /// <summary> 読み込んだアセットの名前（UnityEngine.Object.name のコピー）。ロード完了前・アセットが破棄済みのときは空文字 </summary>
        internal string AccAssetName => _assetName;
        internal int AccRefCount => _refCount;
        /// <summary> ロードの進行状態 </summary>
        internal AsyncOperationStatus AccStatus => _status;
        /// <summary> 寿命スコープごとの確保内訳（ScopeCountSnapshot の List）の中での、この Entry の開始位置 </summary>
        internal int AccScopeCountStart => _scopeCountStart;
        /// <summary> 寿命スコープごとの確保内訳の、この Entry の件数 </summary>
        internal int AccScopeCountLength => _scopeCountLength;

        internal AssetEntrySnapshot(
            AssetKey key,
            Type asset_type,
            string asset_name,
            int ref_count,
            AsyncOperationStatus status,
            int scope_count_start,
            int scope_count_length)
        {
            _key = key;
            _assetType = asset_type;
            _assetName = asset_name ?? string.Empty;
            _refCount = ref_count;
            _status = status;
            _scopeCountStart = scope_count_start;
            _scopeCountLength = scope_count_length;
        }
    }
}
#endif
