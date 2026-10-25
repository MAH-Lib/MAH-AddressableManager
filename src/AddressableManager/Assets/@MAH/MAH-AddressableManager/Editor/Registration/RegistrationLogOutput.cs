using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// RegistrationLogFormatter が組み立てたログの文字列を、出すレベルごとに分けて持つ。
    /// 実際に Console へ出すのは呼び出し側（AddressableLogger）。Unity の型に依存しない。
    /// </summary>
    internal sealed class RegistrationLogOutput
    {
        private readonly List<string> _infos = new();
        private readonly List<string> _warnings = new();
        private readonly List<string> _errors = new();

        /// <summary> 通常のログ（まとめ・1件ずつ） </summary>
        internal List<string> AccInfos => _infos;
        /// <summary> 警告 </summary>
        internal List<string> AccWarnings => _warnings;
        /// <summary> エラー（登録できなかったもの・競合・アドレスの重複） </summary>
        internal List<string> AccErrors => _errors;

        /// <summary> 何も出さないか </summary>
        internal bool AccIsEmpty => _infos.Count == 0 && _warnings.Count == 0 && _errors.Count == 0;

        /// <summary> 中身を空にする（使い回すため） </summary>
        internal void Clear()
        {
            _infos.Clear();
            _warnings.Clear();
            _errors.Clear();
        }
    }
}
