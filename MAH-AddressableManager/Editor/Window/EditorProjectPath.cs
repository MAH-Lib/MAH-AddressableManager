using System.IO;
using UnityEngine;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// Editor 内で使う、Unity プロジェクトのパスの解決。スタックトレースの相対パスの解決と、ログファイルの出力先の解決が共通で使う。
    /// </summary>
    internal static class EditorProjectPath
    {
        /// <summary> Unity プロジェクトのルート（Assets の親）。Application.dataPath は主スレッドでしか読めないため、主スレッドから呼ぶこと </summary>
        internal static string GetProjectRoot() => Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
    }
}
