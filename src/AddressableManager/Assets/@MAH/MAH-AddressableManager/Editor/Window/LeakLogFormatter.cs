using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MAH.AddressableManager.Editor.Localization;
using MAH.AddressableManager.Core.Loading;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// リークのログファイル（テキスト）の中身を組み立てる。Unity 非依存の純粋なロジックで、テストから直接検証できる。
    /// 出力する行（rows）とその回数（counts）は、呼び出し側が決める
    /// （手動の出力は、タブに表示中の行とクリア以降の回数。Play 終了時の自動出力は、全行とその全回数）。
    /// 組み立ては出力のときの1回だけで、描画のたびには呼ばれない。
    /// </summary>
    internal static class LeakLogFormatter
    {
        private const string DATE_TIME_FORMAT = "yyyy-MM-dd HH:mm:ss";
        private const string TIME_FORMAT = "HH:mm:ss";
        private const string TRACE_INDENT = "      ";
        private const string DETAIL_INDENT = "    ";

        /// <summary>
        /// ログの全文を作る。rows は出力する行の leaks 上の添字（出力する順）、counts はその行ごとの回数（rows と同じ長さ）。
        /// project_root は、スタックトレースの相対パスを絶対パスにするための、Unity プロジェクトのルート
        /// </summary>
        internal static string Format(DateTime exported_at, List<LeakSnapshot> leaks, List<int> rows, List<int> counts, string project_root)
        {
            if (rows.Count != counts.Count)
                throw new ArgumentException($"rows（{rows.Count}件）と counts（{counts.Count}件）の長さが違います。", nameof(counts));

            var total_count = 0;
            foreach (var count in counts) total_count += count;

            var builder = new StringBuilder();
            builder.AppendLine(EditorText.Get("leaklog.title"));
            builder.AppendLine(EditorText.Format("leaklog.exportedAt", exported_at.ToString(DATE_TIME_FORMAT, CultureInfo.InvariantCulture)));
            builder.AppendLine(EditorText.Format("leak.summary", total_count, rows.Count));

            if (rows.Count == 0)
            {
                builder.AppendLine();
                builder.AppendLine(EditorText.Get("leaklog.noLeak"));
                return builder.ToString();
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var leak = leaks[rows[i]];
                builder.AppendLine();
                builder.AppendLine($"[{i + 1}] ×{counts[i]}  {BuildTitle(leak)}");
                builder.AppendLine(DETAIL_INDENT + EditorText.Format("leaklog.lifetime", BuildScopeName(leak)));
                builder.AppendLine(DETAIL_INDENT + EditorText.Format("leaklog.lastTime", leak.AccLastTime.ToString(TIME_FORMAT, CultureInfo.InvariantCulture)));
                AppendStackTrace(builder, leak.AccStackTrace, project_root);
            }
            return builder.ToString();
        }

        /// <summary> 行の見出し。単体はキーと型、Label 一括は Label と件数 </summary>
        internal static string BuildTitle(LeakSnapshot leak)
            => leak.AccIsGroup
                ? EditorText.Format("leak.labelTitle", leak.AccLabel, leak.AccGroupCount)
                : $"{leak.AccKey}  <{leak.AccAssetType?.Name}>";

        /// <summary>
        /// 寿命のスコープの表記。発生時に Runtime が記録した名前を使う（寿命の終了時に起きるため、後から引くと破棄済みになる）。
        /// 名前が取れなかったものは、ID（Owner）・handle（Scene）で示す
        /// </summary>
        internal static string BuildScopeName(LeakSnapshot leak)
        {
            switch (leak.AccScopeKind)
            {
                case LifetimeKind.Owner:
                    return leak.AccScopeName.Length > 0
                        ? EditorText.Format("common.scope.owner", leak.AccScopeName, leak.AccScopeValue)
                        : EditorText.Format("common.scope.owner", EditorText.Get("common.noName"), leak.AccScopeValue);
                case LifetimeKind.Scene:
                    return leak.AccScopeName.Length > 0
                        ? EditorText.Format("common.scope.scene", leak.AccScopeName)
                        : EditorText.Format("common.scope.sceneDestroyed", EditorText.Get("common.noName"), leak.AccScopeValue);
                default:
                    return leak.AccScopeKind.ToString();
            }
        }

        // 呼び出し元（最初の利用者のフレーム）と、スタックトレースの全文を書く
        private static void AppendStackTrace(StringBuilder builder, string stack_trace, string project_root)
        {
            var frames = StackTraceParser.Parse(stack_trace, project_root);
            var caller_index = StackTraceParser.FindFirstUserFrame(frames);
            builder.AppendLine(caller_index >= 0
                ? DETAIL_INDENT + EditorText.Format("leaklog.caller", frames[caller_index].AccPath + ":" + frames[caller_index].AccLine)
                : DETAIL_INDENT + EditorText.Format("leaklog.caller", EditorText.Get("leaklog.unknownCaller")));

            builder.AppendLine(DETAIL_INDENT + EditorText.Get("leaklog.stackTrace"));
            if (string.IsNullOrEmpty(stack_trace)) return;

            var start = 0;
            while (start < stack_trace.Length)
            {
                var end = stack_trace.IndexOf('\n', start);
                if (end < 0) end = stack_trace.Length;
                var line = stack_trace.Substring(start, end - start).Trim();
                start = end + 1;
                if (line.Length > 0) builder.AppendLine(TRACE_INDENT + line);
            }
        }
    }
}
