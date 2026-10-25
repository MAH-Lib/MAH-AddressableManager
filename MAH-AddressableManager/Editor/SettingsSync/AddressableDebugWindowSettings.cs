using System;
using UnityEngine;

namespace MAH.AddressableManager.Editor.SettingsSync
{
    /// <summary>
    /// デバッグウィンドウ（AddressableDebugWindow）の設定。更新間隔を持つ。
    /// SO ではなく、AddressableEditorSettingsSO が保持する [Serializable] クラス。
    /// 機能（タブ）ごとの設定は、この共通の設定とは別に、Addressable&lt;機能&gt;Settings として SO に足す。
    /// </summary>
    [Serializable]
    internal sealed class AddressableDebugWindowSettings
    {
        private const float DEFAULT_REFRESH_INTERVAL_SEC = 0.5f;
        // OnInspectorUpdate の呼ばれる頻度（約 10Hz）に合わせた下限。これより短くしても、更新は速くならない
        internal const float MIN_REFRESH_INTERVAL_SEC = 0.1f;

        [SerializeField] private float _refreshIntervalSec = DEFAULT_REFRESH_INTERVAL_SEC;

        /// <summary> Window が窓口から状態を取り直す間隔（秒） </summary>
        internal float AccRefreshIntervalSec => _refreshIntervalSec;

        /// <summary> Unity のシリアライズ用。既定値で生成する </summary>
        public AddressableDebugWindowSettings()
        {
        }

        /// <summary> テスト等でコードから値を指定して生成する </summary>
        internal AddressableDebugWindowSettings(float refresh_interval_sec)
        {
            _refreshIntervalSec = refresh_interval_sec;
            ClampValues();
        }

        /// <summary> 設定値を有効な範囲に補正する（SO の OnValidate から呼ばれる） </summary>
        internal void ClampValues()
        {
            _refreshIntervalSec = Mathf.Max(MIN_REFRESH_INTERVAL_SEC, _refreshIntervalSec);
        }
    }
}
