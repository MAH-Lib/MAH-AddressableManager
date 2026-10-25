using System;
using System.Collections.Generic;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// IProgress&lt;DownloadStatus&gt; の偽実装。届いた値を、届いた順に記録する。
    /// System.Progress&lt;T&gt; は通知を後のフレームで処理するため、テストでは使わず、その場で記録するこの実装を使う。
    /// ThrowOnReport を設定すると、Report が例外を投げる（利用者のコードが例外を投げた場合の再現）。
    /// </summary>
    internal sealed class RecordingDownloadProgress : IProgress<DownloadStatus>
    {
        private readonly List<DownloadStatus> _received = new();

        /// <summary> 届いた値（届いた順）。構造体のコピーなので、届いた時点の値のまま残る </summary>
        internal IReadOnlyList<DownloadStatus> Received => _received;

        /// <summary> 最後に届いた値。何も届いていなければ default（AccPhase == None） </summary>
        internal DownloadStatus Last => _received.Count > 0 ? _received[_received.Count - 1] : default;

        /// <summary> 設定すると、Report がこの例外を投げる（値は記録したうえで投げる） </summary>
        internal Exception ThrowOnReport { get; set; }

        /// <summary> Report が呼ばれるたびに、記録の後で実行する処理（最後の通知の中で次の処理を始める、などの再現用） </summary>
        internal Action<DownloadStatus> OnReport { get; set; }

        public void Report(DownloadStatus value)
        {
            _received.Add(value);
            OnReport?.Invoke(value);
            if (ThrowOnReport != null)
                throw ThrowOnReport;
        }
    }
}
