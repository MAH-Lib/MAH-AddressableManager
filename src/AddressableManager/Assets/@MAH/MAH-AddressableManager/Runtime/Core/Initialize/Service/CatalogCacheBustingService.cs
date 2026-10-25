using System;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace MAH.AddressableManager.Core.Initialize
{
    /// <summary>
    /// リモートカタログの .hash の URL に、要求ごとに異なるクエリを付ける Service。
    /// iOS / macOS の OS の URL キャッシュで古い .hash が返り、カタログ更新が「更新なし」と判定されるのを避ける。
    /// Addressables.InternalIdTransformFunc に変換関数を設定し、Manager の破棄時に元へ戻す。
    /// 対象を .hash に絞るのは、カタログ本体（.bin / .json）の URL が、変換後の .hash の URL から作られて同じクエリが付くため。
    /// 変換関数はすべてのロケーションの解決で呼ばれるため、対象外の InternalId では新しい文字列を作らない。
    /// </summary>
    internal sealed class CatalogCacheBustingService
    {
        private const string TARGET_PREFIX = "http"; // https も含む
        private const string TARGET_EXTENSION = ".hash";
        private const char QUERY_START = '?';
        private const char QUERY_APPEND = '&';
        private const string QUERY_NAME = "t";

        private readonly long _sessionValue; // 起動時の値。Manager の起動時に1回だけ作る
        private readonly Func<IResourceLocation, string> _appliedTransform; // 設定する関数。毎回 delegate を作らず、同じ参照で設定・比較する
        private Func<IResourceLocation, string> _previousTransform; // 設定前の関数（利用者が設定していたもの）
        private long _serial; // 変換するたびに進める連番
        private bool _isApplied;

        /// <summary> 起動時の値（Manager が DateTime.UtcNow.Ticks を渡す。テストでは固定値を渡す） </summary>
        internal CatalogCacheBustingService(long session_value)
        {
            _sessionValue = session_value;
            _appliedTransform = TransformInternalId;
        }

        /// <summary> 変換関数を設定する。すでに設定されている関数は上書きせず、先にその関数を通してから変換する </summary>
        internal void Apply()
        {
            if (_isApplied) return;

            _previousTransform = Addressables.InternalIdTransformFunc;
            Addressables.InternalIdTransformFunc = _appliedTransform;
            _isApplied = true;
        }

        /// <summary>
        /// 設定前の関数へ戻す。ただし、自分の設定した関数のままのときだけ。
        /// 利用者が後から別の関数に上書きしていた場合は、その関数を消さないよう触らず、Warning を出す。
        /// 上書きした関数がこの関数を内部で呼んでいると、戻した後もこの関数は呼ばれ続ける。
        /// その場合に Awake より前に設定されていた元の関数が通らなくなるのを避けるため、上書きされていたときは設定前の関数を保持したままにする。
        /// </summary>
        internal void Restore()
        {
            if (!_isApplied) return;
            _isApplied = false;

            if (Addressables.InternalIdTransformFunc == _appliedTransform)
            {
                Addressables.InternalIdTransformFunc = _previousTransform;
                _previousTransform = null;
            }
            else
            {
                AddressableLogger.LogWarning("InternalIdTransformFunc が途中で別の関数に上書きされていたため、元に戻しません。上書きした関数がこの関数を呼んでいない場合、カタログの URL キャッシュ対策は、上書きされた時点から効いていませんでした。");
            }
        }

        /// <summary> InternalId を変換する。既存の関数を先に通し、その結果が対象（http で始まり、? より前のパスが .hash で終わる）ならクエリを付ける </summary>
        internal string TransformInternalId(IResourceLocation location)
        {
            var internal_id = _previousTransform != null ? _previousTransform(location) : location.InternalId;
            if (!IsTarget(internal_id))
                return internal_id; // 対象外は、新しい文字列を作らず、そのまま返す

            _serial++;
            var separator = internal_id.IndexOf(QUERY_START) >= 0 ? QUERY_APPEND : QUERY_START;
            var transformed = $"{internal_id}{separator}{QUERY_NAME}={_sessionValue}-{_serial}";
            AddressableLogger.Log($"カタログの URL にキャッシュ対策のクエリを付けました：{transformed}");
            return transformed;
        }

        // カルチャに依存しない比較（Ordinal）で判定する。
        // 拡張子は ? より前のパス部分で見る（CDN の署名トークン等のクエリが付いた .hash も対象にするため）。
        // 部分文字列を作らず、位置を指定した比較で済ませる。http の判定を先に行い、ローカルの InternalId では ? の探索まで進まない
        private static bool IsTarget(string internal_id)
        {
            if (string.IsNullOrEmpty(internal_id) || !internal_id.StartsWith(TARGET_PREFIX, StringComparison.Ordinal))
                return false;

            var path_end = internal_id.IndexOf(QUERY_START);
            if (path_end < 0) path_end = internal_id.Length;

            var extension_start = path_end - TARGET_EXTENSION.Length;
            return extension_start >= 0
                && string.CompareOrdinal(internal_id, extension_start, TARGET_EXTENSION, 0, TARGET_EXTENSION.Length) == 0;
        }
    }
}
