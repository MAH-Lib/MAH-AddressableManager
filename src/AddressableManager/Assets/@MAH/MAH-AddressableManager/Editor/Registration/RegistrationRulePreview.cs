using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Registration
{
    /// <summary>
    /// ルールの SO の Inspector に出すプレビュー（仕様 3.6）。対象のアセットの数・最初の数件の展開結果・警告をまとめる。
    /// 対象のアセットを探す処理は重いため、値を変えたときに1回だけ計算して持つ。Unity の型に依存しない。
    /// </summary>
    internal sealed class RegistrationRulePreview
    {
        private readonly List<RegistrationPreviewSample> _samples = new();
        private readonly List<string> _warnings = new();

        /// <summary> 対象のアセットの数（このルールに当たるもの） </summary>
        internal int AccTargetCount { get; set; }
        /// <summary> 最初の数件の展開結果 </summary>
        internal List<RegistrationPreviewSample> AccSamples => _samples;
        /// <summary> 警告（拡張子が空・All の注意・知らないマクロ・ほかのルールとの重なりなど） </summary>
        internal List<string> AccWarnings => _warnings;
    }
}
