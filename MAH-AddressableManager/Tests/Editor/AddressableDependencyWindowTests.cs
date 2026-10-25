using System.Collections.Generic;
using NUnit.Framework;
using MAH.AddressableManager.Editor.Window;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class AddressableDependencyWindowTests
    {
        private readonly List<int> _result = new();
        private List<DependencyCheckRow> _rows;

        private static DependencyCheckRow CreateRow(string path, string type, long size, string group, bool hidden = false)
        {
            var referrers = new List<DependencyCheckReferrer> { new("Assets/P.prefab", group) };
            return new DependencyCheckRow(path, path.Substring(path.LastIndexOf('/') + 1), type, size, referrers, 1, false, false, hidden);
        }

        [SetUp]
        public void SetUp()
        {
            // パス順に並んでいる前提（Aggregate の出力と同じ）
            _rows = new List<DependencyCheckRow>
            {
                CreateRow("Assets/A_Tex.png", "Texture2D", 100, "G1"),
                CreateRow("Assets/B_Mat.mat", "Material", 300, "G2"),
                CreateRow("Assets/C_Tex.png", "Texture2D", 300, "G2"),
                CreateRow("Assets/D_Script.cs", "MonoScript", 5, "G1", hidden: true),
            };
        }

        private void Build(List<string> types = null, string name = "", string group = "", bool show_hidden = false, bool sort_by_size = false)
            => AddressableDependencyWindow.BuildVisibleRows(_rows, types ?? new List<string>(), name, group, show_hidden, sort_by_size, _result);

        // 絞り込み無しでは、既定で隠す行を除いた全行が、パス順で出る
        [Test]
        public void NoFilterHidesHiddenByDefaultRows()
        {
            Build();

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, _result);
        }

        // 既定で隠す行は、表示に切り替えると出る
        [Test]
        public void ShowHiddenIncludesHiddenRows()
        {
            Build(show_hidden: true);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, _result);
        }

        // 型は複数選択（OR）
        [Test]
        public void TypeFilterIsMultiSelect()
        {
            Build(types: new List<string> { "Material" });
            CollectionAssert.AreEqual(new[] { 1 }, _result);

            Build(types: new List<string> { "Material", "Texture2D" });
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, _result);
        }

        // 名前は部分一致で、大文字小文字を区別しない
        [Test]
        public void NameFilterIsCaseInsensitiveContains()
        {
            Build(name: "tEX");

            CollectionAssert.AreEqual(new[] { 0, 2 }, _result);
        }

        // グループは、参照元のグループで絞る
        [Test]
        public void GroupFilterMatchesReferrerGroup()
        {
            Build(group: "G2");

            CollectionAssert.AreEqual(new[] { 1, 2 }, _result);
        }

        // 条件は AND で組み合わさる
        [Test]
        public void FiltersCombineWithAnd()
        {
            Build(types: new List<string> { "Texture2D" }, group: "G2");

            CollectionAssert.AreEqual(new[] { 2 }, _result);
        }

        // サイズの大きい順。同じサイズはパス順（添字順）を保つ
        [Test]
        public void SortBySizeIsDescendingWithStablePathOrder()
        {
            Build(sort_by_size: true);

            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, _result);
        }

        // 呼び直すと、前回の結果は残らない
        [Test]
        public void BuildReplacesPreviousResult()
        {
            Build();
            Build(name: "zzz");

            Assert.AreEqual(0, _result.Count);
        }
    }
}
