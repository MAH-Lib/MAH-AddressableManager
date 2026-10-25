using System;
using UnityEngine;

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// Loading 機能のプロジェクト固定設定。owner ポーリングの有効/間隔、リトライ回数・待機時間を持つ。
    /// SO ではなく、AddressableRuntimeSettingsSO が保持する [Serializable] クラス。
    /// LoadingController にはこのクラスだけを渡すため、EditMode テストでは new するだけで使える。
    /// </summary>
    [Serializable]
    internal sealed class AddressableLoadingSettings
    {
        private const float MIN_OWNER_POLLING_INTERVAL_SEC = 0.1f;
        private const int MIN_MAX_RETRY = 1;
        private const int MIN_RETRY_DELAY_MS = 0;

        [SerializeField] private bool _enableOwnerPolling = true;

        [SerializeField] private float _ownerPollingIntervalSec = 3f;

        [SerializeField] private int _maxRetry = 3;

        [SerializeField] private int _retryDelayMs = 1000;

        /// <summary> owner破棄のポーリングを行うか </summary>
        internal bool AccEnableOwnerPolling => _enableOwnerPolling;
        /// <summary> owner破棄のポーリング間隔（秒） </summary>
        internal float AccOwnerPollingIntervalSec => _ownerPollingIntervalSec;
        /// <summary> ロード失敗時の最大リトライ回数 </summary>
        internal int AccMaxRetry => _maxRetry;
        /// <summary> リトライ前の待機時間（ミリ秒） </summary>
        internal int AccRetryDelayMs => _retryDelayMs;

        /// <summary> Unity のシリアライズ用。既定値で生成する </summary>
        public AddressableLoadingSettings()
        {
        }

        /// <summary> テスト等でコードから値を指定して生成する </summary>
        internal AddressableLoadingSettings(bool enable_owner_polling, float owner_polling_interval_sec, int max_retry, int retry_delay_ms)
        {
            _enableOwnerPolling = enable_owner_polling;
            _ownerPollingIntervalSec = owner_polling_interval_sec;
            _maxRetry = max_retry;
            _retryDelayMs = retry_delay_ms;
            ClampValues();
        }

        /// <summary> 設定値を有効な範囲に補正する（SO の OnValidate から呼ばれる） </summary>
        internal void ClampValues()
        {
            _ownerPollingIntervalSec = Mathf.Max(MIN_OWNER_POLLING_INTERVAL_SEC, _ownerPollingIntervalSec);
            _maxRetry = Mathf.Max(MIN_MAX_RETRY, _maxRetry);
            _retryDelayMs = Mathf.Max(MIN_RETRY_DELAY_MS, _retryDelayMs);
        }
    }
}
