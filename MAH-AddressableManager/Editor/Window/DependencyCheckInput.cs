using System.Collections.Generic;

namespace MAH.AddressableManager.Editor.Window
{
    /// <summary>
    /// Dependency Check の集約への入力。DependencyCheckScanner が Unity から読んだ値を、パスと型名の文字列だけで持つ（Unity 非依存。偽の入力でテストできる）。
    /// </summary>
    internal sealed class DependencyCheckInput
    {
        /// <summary> 登録済みの親1つ（パスと、属するグループ名）。依存の辿り方は AccDirectDependencies に持つ </summary>
        internal sealed class Parent
        {
            internal string AccPath { get; }
            internal string AccGroupName { get; }

            internal Parent(string path, string group_name)
            {
                AccPath = path;
                AccGroupName = group_name;
            }
        }

        /// <summary> 走査した親（Built In Data を除く、全グループの登録済みアセット） </summary>
        internal List<Parent> AccParents { get; } = new();
        /// <summary> 「登録済み」のパス（親のパス＋SpriteAtlas の packables）。ここに無い依存が、未登録の子 </summary>
        internal HashSet<string> AccRegisteredPaths { get; } = new();
        /// <summary>
        /// アセットのパス → 直接の依存のパス（自分自身は含まない）。親・登録済みのアセット・未登録の子のすべてを持つ。
        /// 登録済みのアセットを経由した使用か、直接の使用かを区別するため、孫以降をまとめず、1段ずつ持つ
        /// </summary>
        internal Dictionary<string, List<string>> AccDirectDependencies { get; } = new();
        /// <summary> 未登録の子のパス → メインアセットの型名 </summary>
        internal Dictionary<string, string> AccTypeNames { get; } = new();
        /// <summary> 未登録の子のパス → ディスク上のファイルサイズ（byte） </summary>
        internal Dictionary<string, long> AccSizes { get; } = new();
        /// <summary> Bundle Mode が Pack Separately のグループ名 </summary>
        internal HashSet<string> AccPackSeparatelyGroups { get; } = new();
    }
}
