using System;
using MAH.AddressableManager;
using UnityEngine;
using Conditional = System.Diagnostics.ConditionalAttribute; // System.Diagnostics を丸ごとusingするとUnityEngine.Debugと衝突するため

namespace MAH.AddressableManager.Core
{
    /// <summary>
    /// Debug.Log を包むだけの内部用ロガー。AppLogger 等の外部依存を持ち込まないための薄いラッパー。
    /// 出力の閾値は AccLevel（AddressableRuntimeSettingsSO 由来）で切り替える。
    /// Log/LogInfo/LogWarning/LogException は UNITY_ASSERTIONS（Editor・開発ビルドで定義され、リリースビルドでは外れる）
    /// を付け、呼び出し自体をリリースビルドから完全に取り除く（文字列生成のGCアロケーションごと消す）。
    /// LogError だけは、リリースビルドでも致命的な失敗を追えるよう常に残す。
    /// </summary>
    internal static class AddressableLogger
    {
        /// <summary> title を省略したときのタイトル（Runtime のスクリプトはこれを使う） </summary>
        internal const string DEFAULT_TITLE = "AddressableManager";

        // Console上で他のログと見分けやすいよう、タグ部分だけ水色のリッチテキストにする
        private const string LOG_PREFIX_FORMAT = "<color=lightblue>[{0}]</color> ";

        private static AddressableLogLevel _level = AddressableLogLevel.Warning;

        /// <summary> 出力する最低レベル。これより低い重要度のログは出力しない </summary>
        internal static AddressableLogLevel AccLevel
        {
            get => _level;
            set => _level = value;
        }

        // ドメインリロードを無効にした環境でも、Play開始のたびに既定値へ戻す
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLevelOnLoad()
        {
            _level = AddressableLogLevel.Warning;
        }

        // title は Console 上のタグ部分に表示される（例：[AddressableManager] / [AddressableSample]）。
        // Runtime のスクリプトは省略（DEFAULT_TITLE）、サンプルなど別の出どころのログだけが指定する。
        [Conditional("UNITY_ASSERTIONS")]
        internal static void Log(string message, string title = DEFAULT_TITLE)
        {
            if (_level > AddressableLogLevel.Verbose) return;
            Debug.Log(CreatePrefix(title) + message);
        }

        [Conditional("UNITY_ASSERTIONS")]
        internal static void LogWarning(string message, string title = DEFAULT_TITLE)
        {
            if (_level > AddressableLogLevel.Warning) return;
            Debug.LogWarning(CreatePrefix(title) + message);
        }

        // 利用者の操作の結果（ログファイルの出力など）を知らせる通知。既定のレベル（Warning）でも表示される。Log（Verbose 限定の内部の経過）とは区別する
        internal static void LogInfo(string message, string title = DEFAULT_TITLE)
        {
            if (_level > AddressableLogLevel.Info) return;
            Debug.Log(CreatePrefix(title) + message);
        }

        // リリースビルドでも致命的な失敗を追えるよう、LogErrorだけはConditionalを付けない
        internal static void LogError(string message, string title = DEFAULT_TITLE)
        {
            if (_level > AddressableLogLevel.Error) return;
            Debug.LogError(CreatePrefix(title) + message);
        }

        [Conditional("UNITY_ASSERTIONS")]
        internal static void LogException(Exception exception)
        {
            if (_level > AddressableLogLevel.Error) return;
            Debug.LogException(exception);
        }

        private static string CreatePrefix(string title) => string.Format(LOG_PREFIX_FORMAT, title);
    }
}
