namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary> Writer が計画を書き込んだ結果 </summary>
    internal readonly struct RegistrationApplyResult
    {
        private readonly int _appliedCount;
        private readonly int _failedCount;
        private readonly int _skippedCount;
        private readonly bool _canceled;

        /// <summary> 書き込めた件数 </summary>
        internal int AccAppliedCount => _appliedCount;
        /// <summary> 書き込めなかった件数（グループやエントリが見つからなかったもの） </summary>
        internal int AccFailedCount => _failedCount;
        /// <summary> 書き込まずにスキップした件数（計画を作った時点から、エントリの状態が変わっていたもの） </summary>
        internal int AccSkippedCount => _skippedCount;
        /// <summary> 進捗バーのキャンセルで、途中で止めたか（そこまでの分は書き込まれている） </summary>
        internal bool AccCanceled => _canceled;

        internal RegistrationApplyResult(int applied_count, int failed_count, int skipped_count, bool canceled)
        {
            _appliedCount = applied_count;
            _failedCount = failed_count;
            _skippedCount = skipped_count;
            _canceled = canceled;
        }
    }
}
