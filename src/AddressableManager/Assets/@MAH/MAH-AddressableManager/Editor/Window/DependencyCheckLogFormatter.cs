using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MAH.AddressableManager.Editor.Localization;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// Dependency Check の表示中の行を、人が読むテキストにする。Unity 非依存の純粋なロジックで、テストから直接検証できる。
    /// 組み立ては出力のときの1回だけで、描画のたびには呼ばれない。
    /// </summary>
    internal static class DependencyCheckLogFormatter
    {
        private const string TITLE = "Addressable Dependency Check";
        private const string DATE_TIME_FORMAT = "yyyy-MM-dd HH:mm:ss";
        private const string REFERRER_INDENT = "  ← ";
        private const long KB = 1024;
        private const long MB = 1024 * 1024;

        /// <summary>
        /// テキストの全文を作る。visible は出力する行の rows 上の添字（出力する順）、
        /// filter_summary は絞り込みの表記（例：「型 すべて / グループ すべて」）
        /// </summary>
        internal static string Format(DateTime exported_at, List<DependencyCheckRow> rows, List<int> visible, string filter_summary)
        {
            var multi_group_count = 0;
            foreach (var index in visible)
                if (rows[index].AccIsMultiGroup) multi_group_count++;

            var builder = new StringBuilder();
            builder.AppendLine($"{TITLE}  {exported_at.ToString(DATE_TIME_FORMAT, CultureInfo.InvariantCulture)}");
            builder.AppendLine(EditorText.Format("dep.log.header", visible.Count, multi_group_count, filter_summary));

            if (visible.Count == 0)
            {
                builder.AppendLine();
                builder.AppendLine(EditorText.Get("dep.log.noRow"));
                return builder.ToString();
            }

            // 親のパスの幅は、出力全体の最長にそろえて、グループ名の位置を全行で縦にそろえる
            var width = 0;
            foreach (var index in visible)
                foreach (var referrer in rows[index].AccReferrers)
                    width = Math.Max(width, referrer.AccPath.Length);

            foreach (var index in visible)
            {
                var row = rows[index];
                builder.AppendLine();
                builder.AppendLine(
                    $"{(row.AccIsMultiGroup ? EditorText.Get("dep.log.multiGroupMark") : string.Empty)}{row.AccPath}  ({row.AccTypeName}, {FormatSize(row.AccSizeBytes)}, {EditorText.Format("dep.log.groups", row.AccGroupCount)})");

                foreach (var referrer in row.AccReferrers)
                    builder.AppendLine($"{REFERRER_INDENT}{referrer.AccPath.PadRight(width)}  [{referrer.AccGroupName}]{FormatVia(referrer)}");
            }
            return builder.ToString();
        }

        /// <summary>
        /// 経由の表記。直接の使用のときは空、経由のときは「（経由：パス）」、経由先が複数のときは「（経由：パス ほか n 件）」。
        /// 参照元の行の末尾に付ける（画面の展開した行と共有する）
        /// </summary>
        internal static string FormatVia(DependencyCheckReferrer referrer)
        {
            if (!referrer.AccIsVia) return string.Empty;

            return referrer.AccViaCount > 1
                ? EditorText.Format("dep.log.viaMore", referrer.AccViaPath, referrer.AccViaCount - 1)
                : EditorText.Format("dep.log.via", referrer.AccViaPath);
        }

        /// <summary> サイズ表記。1KB 未満は B、1MB 未満は KB（整数）、それ以上は MB（小数1桁） </summary>
        internal static string FormatSize(long bytes)
        {
            if (bytes < KB) return $"{bytes} B";
            if (bytes < MB) return $"{((double)bytes / KB).ToString("0", CultureInfo.InvariantCulture)} KB";
            return $"{((double)bytes / MB).ToString("0.0", CultureInfo.InvariantCulture)} MB";
        }
    }
}
