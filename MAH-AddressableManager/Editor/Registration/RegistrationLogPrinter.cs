using MAH.AddressableManager.Core;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// RegistrationLogFormatter が組み立てたログを、AddressableLogger で Console に出す。
    /// 自動登録・当て直し・手動一括登録で共通に使う。出すのはここだけで、文字列の組み立てはしない（Formatter の役目）。
    /// </summary>
    internal static class RegistrationLogPrinter
    {
        /// <summary> Info → Warning → Error の順に出す </summary>
        internal static void Print(RegistrationLogOutput output)
        {
            for (var i = 0; i < output.AccInfos.Count; i++) AddressableLogger.LogInfo(output.AccInfos[i]);
            for (var i = 0; i < output.AccWarnings.Count; i++) AddressableLogger.LogWarning(output.AccWarnings[i]);
            for (var i = 0; i < output.AccErrors.Count; i++) AddressableLogger.LogError(output.AccErrors[i]);
        }
    }
}
