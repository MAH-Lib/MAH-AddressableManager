using MAH.AddressableManager.Core.Download;
using MAH.AddressableManager.Core.Initialize;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;

namespace MAH.AddressableManager.Core
{
    /// <summary>
    /// 実行時に使う設定をまとめた SO。AddressableManager に割り当てる唯一の設定アセット。
    /// 機能ごとの設定は各機能フォルダの [Serializable] クラス（AddressableLoadingSettings 等）として持ち、
    /// Controller には自分の機能の設定だけを渡す（機能同士が互いの設定を知らずに済むようにするため）。
    /// エディタでのみ使う設定はここに置かない（ビルドに含めないため、Editor 側の SO に置く）。
    /// </summary>
    [CreateAssetMenu(menuName = "MAH/Addressable/RuntimeSettings", fileName = "AddressableRuntimeSettings")]
    internal sealed class AddressableRuntimeSettingsSO : ScriptableObject
    {
        [SerializeField] private AddressableLogLevel _logLevel = AddressableLogLevel.Warning;

        [SerializeField] private AddressableInitializeSettings _initialize = new();

        [SerializeField] private AddressableDownloadSettings _download = new();

        [SerializeField] private AddressableLoadingSettings _loading = new();

        /// <summary> AddressableLogger に設定するログレベル </summary>
        internal AddressableLogLevel AccLogLevel => _logLevel;
        /// <summary> Initialize 機能の設定 </summary>
        internal AddressableInitializeSettings AccInitialize => _initialize;
        /// <summary> Download 機能の設定 </summary>
        internal AddressableDownloadSettings AccDownload => _download;
        /// <summary> Loading 機能の設定 </summary>
        internal AddressableLoadingSettings AccLoading => _loading;

        private void OnValidate()
        {
            _initialize ??= new AddressableInitializeSettings();
            _loading ??= new AddressableLoadingSettings();
            _loading.ClampValues();
            _download ??= new AddressableDownloadSettings();
            _download.ClampValues();
        }
    }
}
