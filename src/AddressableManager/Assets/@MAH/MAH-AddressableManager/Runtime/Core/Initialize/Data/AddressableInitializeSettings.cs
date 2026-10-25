using System;
using UnityEngine;

namespace MAH.AddressableManager.Core.Initialize
{
    /// <summary>
    /// Initialize 機能のプロジェクト固定設定。初期化の開始方法・初期化前の Load の扱い・handle の解放・URL キャッシュ対策の ON/OFF を持つ。
    /// SO ではなく、AddressableRuntimeSettingsSO が保持する [Serializable] クラス。
    /// InitializeController にはこのクラスだけを渡すため、EditMode テストでは new するだけで使える。
    /// 範囲のある値を持たないため ClampValues は無い。
    /// </summary>
    [Serializable]
    internal sealed class AddressableInitializeSettings
    {
        [SerializeField] private InitializeMode _mode = InitializeMode.Auto;

        [SerializeField] private LoadBeforeInitializePolicy _loadBeforeInitialize = LoadBeforeInitializePolicy.PassThrough;

        [SerializeField] private bool _autoReleaseHandle = true;

        [SerializeField] private bool _enableCatalogCacheBusting = false;

        /// <summary> 初期化を始めるタイミング </summary>
        internal InitializeMode AccMode => _mode;
        /// <summary> 初期化前の Load の扱い </summary>
        internal LoadBeforeInitializePolicy AccLoadBeforeInitialize => _loadBeforeInitialize;
        /// <summary> Addressables.InitializeAsync に渡す autoReleaseHandle </summary>
        internal bool AccAutoReleaseHandle => _autoReleaseHandle;
        /// <summary> リモートカタログの URL キャッシュ対策を有効にするか </summary>
        internal bool AccEnableCatalogCacheBusting => _enableCatalogCacheBusting;

        /// <summary> Unity のシリアライズ用。既定値で生成する </summary>
        public AddressableInitializeSettings()
        {
        }

        /// <summary> テスト等でコードから値を指定して生成する </summary>
        internal AddressableInitializeSettings(InitializeMode mode, LoadBeforeInitializePolicy load_before_initialize, bool auto_release_handle, bool enable_catalog_cache_busting)
        {
            _mode = mode;
            _loadBeforeInitialize = load_before_initialize;
            _autoReleaseHandle = auto_release_handle;
            _enableCatalogCacheBusting = enable_catalog_cache_busting;
        }
    }
}
