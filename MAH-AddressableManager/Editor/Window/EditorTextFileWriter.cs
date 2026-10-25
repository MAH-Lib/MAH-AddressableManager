using System;
using System.Globalization;
using System.IO;
using System.Text;
using MAH.AddressableManager.Core;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// Editor が出力するテキストファイルの、出力先の解決と書き込み。リークのログ（LeakLogWriter）と Dependency Check が共有する。
    /// 日時付きのファイル名で書く（溜める。同じ秒なら _2・_3… の連番）。UTF-8（BOM なし）。
    /// IO の失敗は、例外を握って警告を出し、null を返す（Editor の操作を止めない）。
    /// </summary>
    internal static class EditorTextFileWriter
    {
        /// <summary> UTF-8（BOM なし）。固定名で書く LeakLogWriter.WriteLastPlay も同じものを使う </summary>
        internal static readonly UTF8Encoding ENCODING = new(false);

        private const string EXTENSION = ".txt";
        private const string FILE_TIME_FORMAT = "yyyyMMdd_HHmmss";

        /// <summary>
        /// 出力先を、フォルダのパスにする。相対パスはプロジェクトのルートから、絶対パス（Windows のドライブレターを含む）はそのまま。区切りは / にそろえる。
        /// output_folder が null・空白のときは default_folder にする（設定の補正を通らない経路への備え）
        /// </summary>
        internal static string ResolveFolder(string project_root, string output_folder, string default_folder)
        {
            if (string.IsNullOrWhiteSpace(output_folder)) output_folder = default_folder;

            var folder = output_folder.Trim().Replace('\\', '/');
            if (IsAbsolute(folder)) return folder.TrimEnd('/');

            if (folder.StartsWith("./", StringComparison.Ordinal)) folder = folder.Substring(2);
            return $"{project_root.Replace('\\', '/').TrimEnd('/')}/{folder.TrimEnd('/')}";
        }

        /// <summary> 日時付きのファイル名。&lt;prefix&gt;yyyyMMdd_HHmmss.txt </summary>
        internal static string CreateFileName(string prefix, DateTime now)
            => prefix + now.ToString(FILE_TIME_FORMAT, CultureInfo.InvariantCulture) + EXTENSION;

        /// <summary>
        /// 日時付きのファイルに書く（溜める）。同じ秒に続けて出力したときは、上書きせず、_2・_3… の連番を付ける。
        /// 書き出したパスを返す。失敗したときは、「failure_label：メッセージ」の警告を出して null を返す
        /// </summary>
        internal static string WriteTimestamped(string text, string folder, string prefix, DateTime now, string failure_label)
        {
            try
            {
                Directory.CreateDirectory(folder);
                var path = $"{folder}/{CreateFileName(prefix, now)}";
                var base_path = path.Substring(0, path.Length - EXTENSION.Length);
                for (var number = 2; File.Exists(path); number++)
                    path = $"{base_path}_{number}{EXTENSION}";

                File.WriteAllText(path, text, ENCODING);
                return path;
            }
            catch (Exception exception) // Editor の操作を止めないため、IO の失敗は握る
            {
                AddressableLogger.LogWarning($"{failure_label}: {exception.Message}");
                return null;
            }
        }

        // "/" で始まる、または "C:" のようにドライブレターで始まるパスを絶対パスとして扱う（OS に依存せず、同じ結果にするため）
        private static bool IsAbsolute(string folder)
            => folder.StartsWith("/", StringComparison.Ordinal)
               || (folder.Length >= 2 && char.IsLetter(folder[0]) && folder[1] == ':');
    }
}
