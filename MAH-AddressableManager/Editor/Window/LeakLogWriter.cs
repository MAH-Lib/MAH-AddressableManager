using System;
using System.IO;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Editor.Localization;
using MAH.AddressableManager.Editor.SettingsSync;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// リークのログファイルの書き込み。出力先の解決・フォルダの作成・書き込み・固定名の2ファイルの入れ替えを行う。
    /// 手動の出力（日時付き。溜める）と、Play 終了時の自動出力（固定名の LastPlay を上書きし、前回分を -prev に1世代だけ残す）を持つ。
    /// 削除・移動の対象は、自動出力の固定名の2ファイルだけ（パターンでの一括削除をしないため、手動のログは自動では消えない）。
    /// IO の失敗は、例外を握って警告を出し、null を返す（Editor・Play の終了を止めない）。
    /// </summary>
    internal static class LeakLogWriter
    {
        internal const string LAST_PLAY_FILE_NAME = "AddressableLeaks_LastPlay.txt";
        internal const string LAST_PLAY_PREV_FILE_NAME = "AddressableLeaks_LastPlay-prev.txt";

        private const string FILE_PREFIX = "AddressableLeaks_";

        /// <summary> 出力先をフォルダのパスにする（解決の規則は EditorTextFileWriter.ResolveFolder）。output_folder が null・空白のときは、既定の出力先にする </summary>
        internal static string ResolveFolder(string project_root, string output_folder)
            => EditorTextFileWriter.ResolveFolder(project_root, output_folder, AddressableLeakLogSettings.DEFAULT_OUTPUT_FOLDER);

        /// <summary> 手動の出力のファイル名。AddressableLeaks_yyyyMMdd_HHmmss.txt </summary>
        internal static string CreateFileName(DateTime now)
            => EditorTextFileWriter.CreateFileName(FILE_PREFIX, now);

        /// <summary>
        /// 手動の出力。日時付きのファイルに書く（溜める）。同じ秒に続けて出力したときは、上書きせず、_2・_3… の連番を付ける。
        /// 書き出したパスを返す。失敗したときは、警告を出して null を返す
        /// </summary>
        internal static string WriteManual(string text, string folder, DateTime now)
            => EditorTextFileWriter.WriteTimestamped(text, folder, FILE_PREFIX, now, EditorText.Get("leaklog.writeFailure"));

        /// <summary>
        /// Play 終了時の自動出力。AddressableLeaks_LastPlay.txt を上書きし、前回分を AddressableLeaks_LastPlay-prev.txt に移す（1世代だけ残す）。
        /// 触るのはこの固定名の2ファイルだけ。書き出したパスを返す。失敗したときは、警告を出して null を返す
        /// </summary>
        internal static string WriteLastPlay(string text, string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                var last_path = $"{folder}/{LAST_PLAY_FILE_NAME}";
                var prev_path = $"{folder}/{LAST_PLAY_PREV_FILE_NAME}";
                if (File.Exists(last_path))
                {
                    if (File.Exists(prev_path)) File.Delete(prev_path); // File.Move は上書きできないため、先に消す
                    File.Move(last_path, prev_path);
                }

                File.WriteAllText(last_path, text, EditorTextFileWriter.ENCODING);
                return last_path;
            }
            catch (Exception exception)
            {
                LogFailure(exception);
                return null;
            }
        }

        private static void LogFailure(Exception exception)
            => AddressableLogger.LogWarning($"{EditorText.Get("leaklog.writeFailure")}: {exception.Message}");
    }
}
