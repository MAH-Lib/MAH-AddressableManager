using System;
using System.Collections.Generic;
using MAH.AddressableManager.Attribute;
using UnityEngine;

namespace MAH.AddressableManager.Core.Download
{
    /// <summary>
    /// Download 機能のプロジェクト固定設定。カタログ更新の方針・対象 Label・リトライ回数・待機時間・古いバンドルのキャッシュ削除を持つ。
    /// SO ではなく、AddressableRuntimeSettingsSO が保持する [Serializable] クラス。
    /// DownloadController にはこのクラスだけを渡すため、EditMode テストでは new するだけで使える。
    /// </summary>
    [Serializable]
    internal sealed class AddressableDownloadSettings
    {
        private const int MIN_MAX_RETRY = 0;
        private const int MAX_MAX_RETRY = 10;
        private const int MIN_RETRY_DELAY_MS = 0;
        private const int MAX_RETRY_DELAY_MS = 60000;

        [SerializeField] private CatalogUpdatePolicy _catalogPolicy = CatalogUpdatePolicy.CheckAndUpdate;

        [AddressableLabel]
        [SerializeField] private string[] _labels = Array.Empty<string>();

        [SerializeField] private int _maxRetry = 3;

        [SerializeField] private int _retryDelayMs = 1000;

        [SerializeField] private bool _autoCleanBundleCache = false;

        /// <summary> カタログ更新の方針 </summary>
        internal CatalogUpdatePolicy AccCatalogPolicy => _catalogPolicy;
        /// <summary> 引数なしの API が対象にする Label。未設定のときは空（null ではない） </summary>
        internal IReadOnlyList<string> AccLabels => _labels;
        /// <summary> 失敗時のリトライ回数 </summary>
        internal int AccMaxRetry => _maxRetry;
        /// <summary> リトライ前の待機時間（ミリ秒） </summary>
        internal int AccRetryDelayMs => _retryDelayMs;
        /// <summary> カタログ更新時に、使われなくなった古いバンドルのキャッシュを消すか </summary>
        internal bool AccAutoCleanBundleCache => _autoCleanBundleCache;

        /// <summary> Unity のシリアライズ用。既定値で生成する </summary>
        public AddressableDownloadSettings()
        {
        }

        /// <summary> テスト等でコードから値を指定して生成する </summary>
        internal AddressableDownloadSettings(CatalogUpdatePolicy catalog_policy, string[] labels, int max_retry, int retry_delay_ms, bool auto_clean_bundle_cache)
        {
            _catalogPolicy = catalog_policy;
            _labels = labels;
            _maxRetry = max_retry;
            _retryDelayMs = retry_delay_ms;
            _autoCleanBundleCache = auto_clean_bundle_cache;
            ClampValues();
        }

        /// <summary> 設定値を有効な範囲に補正する（SO の OnValidate から呼ばれる） </summary>
        internal void ClampValues()
        {
            _labels ??= Array.Empty<string>(); // 既存の SO アセットに項目が無い場合や、テストの null を空にそろえる
            _maxRetry = Mathf.Clamp(_maxRetry, MIN_MAX_RETRY, MAX_MAX_RETRY);
            _retryDelayMs = Mathf.Clamp(_retryDelayMs, MIN_RETRY_DELAY_MS, MAX_RETRY_DELAY_MS);
        }
    }
}
