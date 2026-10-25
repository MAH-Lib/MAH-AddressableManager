using System;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// 指定の語を含む警告の件数を数える。リーク警告が「1件だけ」出る・「出ない」ことの確認に使う。
    /// using で囲んだ範囲のログだけを数える。
    /// </summary>
    internal sealed class WarningCounter : IDisposable
    {
        private readonly string _keyword;
        private int _count;

        internal int AccCount => _count;

        internal WarningCounter(string keyword)
        {
            _keyword = keyword;
            Application.logMessageReceived += OnLogMessageReceived;
        }

        private void OnLogMessageReceived(string condition, string stack_trace, LogType type)
        {
            if (type == LogType.Warning && condition.Contains(_keyword))
                _count++;
        }

        public void Dispose() => Application.logMessageReceived -= OnLogMessageReceived;
    }
}
