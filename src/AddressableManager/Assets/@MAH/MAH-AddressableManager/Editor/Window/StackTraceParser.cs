using System;
using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// スタックトレースの1フレーム分。ジャンプできるのは、ファイルと行の情報があるフレームだけ（CanJump）。
    /// </summary>
    internal readonly struct TraceFrame
    {
        private readonly string _label;
        private readonly string _path;
        private readonly string _fileName;
        private readonly int _line;
        private readonly bool _canJump;
        private readonly bool _isModule;

        /// <summary> 表示用の1行。「メソッド名  (ファイル名:行)」。ジャンプできないフレームは、メソッド名だけ </summary>
        internal string AccLabel => _label;
        /// <summary> ジャンプ先のファイルの絶対パス（区切りは / にそろえる）。ジャンプできないときは空文字 </summary>
        internal string AccPath => _path;
        /// <summary> ジャンプ先のファイル名（フォルダを除く）。ジャンプできないときは空文字 </summary>
        internal string AccFileName => _fileName;
        internal int AccLine => _line;
        /// <summary> ファイルと行の情報があり、ジャンプできるか </summary>
        internal bool AccCanJump => _canJump;
        /// <summary> モジュール内（メソッドが AddressableManager の名前空間に属する、または Library/PackageCache の配下）のフレームか </summary>
        internal bool AccIsModule => _isModule;

        internal TraceFrame(string label, string path, string file_name, int line, bool can_jump, bool is_module)
        {
            _label = label;
            _path = path;
            _fileName = file_name;
            _line = line;
            _canJump = can_jump;
            _isModule = is_module;
        }
    }

    /// <summary>
    /// Environment.StackTrace（Mono 形式）の文字列を、フレームごとに解析する。Unity 非依存の純粋なロジックで、テストから直接検証できる。
    /// 解析できない行は、例外にせず、ジャンプ不可のフレームとして残す。
    /// 例）"  at A.B.Method (System.Int32 x) [0x00012] in /path/File.cs:42 "
    /// </summary>
    internal static class StackTraceParser
    {
        private const string AT_PREFIX = "at ";
        private const string METHOD_END = " (";
        private const string LOCATION_MARKER = "] in "; // "[0x00012] in <場所>"
        private const string LOCATION_MARKER_FALLBACK = " in ";
        // モジュール内のフレーム。「最初の利用者のフレーム」を探すときに飛ばす。
        // 導入場所（Assets・Packages の embedded・file: 参照・PackageCache）に左右されないよう、メソッドの名前空間で判定する。
        // PackageCache の判定は、UniTask など、モジュール以外のパッケージのフレームを飛ばすために残す
        private const string MODULE_NAMESPACE = "MAH.AddressableManager.";
        private const string PACKAGE_CACHE_PATH = "/Library/PackageCache/";
        // モジュールの名前空間の下でも、利用者側のコードとして扱う（サンプル・テストからのリークは、そこへジャンプさせたいため）
        private static readonly string[] USER_NAMESPACES = { "MAH.AddressableManager.Samples.", "MAH.AddressableManager.Tests." };
        // プロジェクトからの相対パスで出てくる（Unity 自身のフレームなど）先頭の語
        private static readonly string[] RELATIVE_ROOTS = { "Assets/", "Packages/", "Library/" };
        private static readonly TraceFrame[] EMPTY = Array.Empty<TraceFrame>();

        /// <summary> スタックトレースをフレームに分ける。project_root は、相対パスを絶対パスにするための、Unity プロジェクトのルート（無ければ空文字） </summary>
        internal static TraceFrame[] Parse(string stack_trace, string project_root)
        {
            if (string.IsNullOrEmpty(stack_trace)) return EMPTY;

            var frames = new List<TraceFrame>();
            var start = 0;
            while (start < stack_trace.Length)
            {
                var end = stack_trace.IndexOf('\n', start);
                if (end < 0) end = stack_trace.Length;
                var line = stack_trace.Substring(start, end - start).Trim();
                start = end + 1;
                if (line.Length == 0) continue;

                frames.Add(ParseLine(line, project_root));
            }
            return frames.ToArray();
        }

        /// <summary> 最初の「利用者のフレーム」の添字。ジャンプできて、モジュール内でない最初のフレーム。無ければ -1 </summary>
        internal static int FindFirstUserFrame(TraceFrame[] frames)
        {
            for (var i = 0; i < frames.Length; i++)
            {
                if (frames[i].AccCanJump && !frames[i].AccIsModule)
                    return i;
            }
            return -1;
        }

        private static TraceFrame ParseLine(string line, string project_root)
        {
            var body = line.StartsWith(AT_PREFIX, StringComparison.Ordinal) ? line.Substring(AT_PREFIX.Length) : line;
            var method_end = body.IndexOf(METHOD_END, StringComparison.Ordinal);
            var method = method_end > 0 ? body.Substring(0, method_end) : body;

            if (!TryGetLocation(line, out var location)
                || !TryParseLocation(location, project_root, out var path, out var line_number))
                return new TraceFrame(method, string.Empty, string.Empty, 0, false, false);

            var file_name = path.Substring(path.LastIndexOf('/') + 1);
            var is_module = IsModuleMethod(method) || path.Contains(PACKAGE_CACHE_PATH);
            return new TraceFrame($"{method}  ({file_name}:{line_number})", path, file_name, line_number, true, is_module);
        }

        // メソッド名（"名前空間.型.メソッド"）が、モジュールの名前空間に属するか。Samples・Tests の名前空間は、利用者側として扱う
        private static bool IsModuleMethod(string method)
        {
            if (!method.StartsWith(MODULE_NAMESPACE, StringComparison.Ordinal)) return false;

            foreach (var user_namespace in USER_NAMESPACES)
            {
                if (method.StartsWith(user_namespace, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        // "[0x00012] in <場所>" の <場所> を取り出す。無ければ、最後の " in " 以降を使う
        private static bool TryGetLocation(string line, out string location)
        {
            var marker = line.IndexOf(LOCATION_MARKER, StringComparison.Ordinal);
            if (marker >= 0)
            {
                location = line.Substring(marker + LOCATION_MARKER.Length).Trim();
                return true;
            }

            var fallback = line.LastIndexOf(LOCATION_MARKER_FALLBACK, StringComparison.Ordinal);
            if (fallback >= 0)
            {
                location = line.Substring(fallback + LOCATION_MARKER_FALLBACK.Length).Trim();
                return true;
            }

            location = null;
            return false;
        }

        // "<パス>:<行>" を分ける。最後の ':' で分けるため、Windows のドライブレター（C:\...）を壊さない。
        // パスが "<...>"（ハッシュ・"filename unknown"）のとき、行が 0 以下のときは、ジャンプできない
        private static bool TryParseLocation(string location, string project_root, out string path, out int line_number)
        {
            path = null;
            line_number = 0;

            var colon = location.LastIndexOf(':');
            if (colon <= 0) return false;
            if (!int.TryParse(location.Substring(colon + 1).Trim(), out line_number) || line_number <= 0) return false;

            path = location.Substring(0, colon).Trim().Replace('\\', '/');
            if (path.Length == 0 || path[0] == '<') return false;

            if (path.StartsWith("./", StringComparison.Ordinal)) path = path.Substring(2);
            if (!string.IsNullOrEmpty(project_root) && IsRelativeProjectPath(path))
                path = project_root.Replace('\\', '/').TrimEnd('/') + "/" + path;
            return true;
        }

        private static bool IsRelativeProjectPath(string path)
        {
            foreach (var root in RELATIVE_ROOTS)
            {
                if (path.StartsWith(root, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
