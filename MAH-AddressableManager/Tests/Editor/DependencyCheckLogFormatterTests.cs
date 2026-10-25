using System;
using System.Collections.Generic;
using NUnit.Framework;
using MAH.AddressableManager.Editor.Window;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class DependencyCheckLogFormatterTests
    {
        private static readonly DateTime NOW = new(2026, 10, 15, 14, 30, 0);

        private static DependencyCheckRow CreateRow(string path, string type, long size, params (string path, string group)[] referrers)
        {
            var list = new List<DependencyCheckReferrer>();
            var groups = new HashSet<string>();
            foreach (var (referrer_path, group) in referrers)
            {
                list.Add(new DependencyCheckReferrer(referrer_path, group));
                groups.Add(group);
            }
            return new DependencyCheckRow(path, path.Substring(path.LastIndexOf('/') + 1), type, size, list, groups.Count, false, false, false);
        }

        // 仕様書 2.5 の出力例と一致する（複数グループの印・親のパスの幅そろえ・件数行・経由の表記）
        [Test]
        public void FormatMatchesSpecExample()
        {
            var rows = new List<DependencyCheckRow>
            {
                new DependencyCheckRow("Assets/Art/Tex_Bg.png", "Tex_Bg.png", "Texture2D", 2L * 1024 * 1024,
                    new List<DependencyCheckReferrer>
                    {
                        new("Assets/Prefabs/Title.prefab", "Title", "Assets/Mats/Btn.mat", 1),
                        new("Assets/Prefabs/Menu.prefab", "Menu"),
                    }, 2, false, false, false),
                CreateRow("Assets/Art/Mat_Btn.mat", "Material", 4 * 1024,
                    ("Assets/Prefabs/Menu.prefab", "Menu")),
            };

            var text = DependencyCheckLogFormatter.Format(NOW, rows, new List<int> { 0, 1 }, "型 すべて / グループ すべて");

            var expected =
                "Addressable Dependency Check  2026-10-15 14:30:00\n" +
                "未登録の依存 2 件（複数グループ 1 件）  絞り込み：型 すべて / グループ すべて\n" +
                "\n" +
                "[複数グループ] Assets/Art/Tex_Bg.png  (Texture2D, 2.0 MB, 2 グループ)\n" +
                "  ← Assets/Prefabs/Title.prefab  [Title]（経由：Assets/Mats/Btn.mat）\n" +
                "  ← Assets/Prefabs/Menu.prefab   [Menu]\n" +
                "\n" +
                "Assets/Art/Mat_Btn.mat  (Material, 4 KB, 1 グループ)\n" +
                "  ← Assets/Prefabs/Menu.prefab   [Menu]\n";
            Assert.AreEqual(expected, text.Replace("\r\n", "\n"));
        }

        // 経由の表記：直接は空、経由先が1つならパスだけ、複数なら「ほか n 件」
        [Test]
        public void FormatViaVariants()
        {
            Assert.AreEqual(string.Empty, DependencyCheckLogFormatter.FormatVia(new DependencyCheckReferrer("Assets/P.prefab", "G")));
            Assert.AreEqual("（経由：Assets/M.mat）",
                DependencyCheckLogFormatter.FormatVia(new DependencyCheckReferrer("Assets/P.prefab", "G", "Assets/M.mat", 1)));
            Assert.AreEqual("（経由：Assets/M.mat ほか 2 件）",
                DependencyCheckLogFormatter.FormatVia(new DependencyCheckReferrer("Assets/P.prefab", "G", "Assets/M.mat", 3)));
        }

        // 表示中の行（visible）だけが出る。順序も visible のとおり
        [Test]
        public void FormatOutputsOnlyVisibleRowsInOrder()
        {
            var rows = new List<DependencyCheckRow>
            {
                CreateRow("Assets/A.png", "Texture2D", 10, ("Assets/P.prefab", "G")),
                CreateRow("Assets/B.png", "Texture2D", 20, ("Assets/P.prefab", "G")),
            };

            var text = DependencyCheckLogFormatter.Format(NOW, rows, new List<int> { 1 }, "x");

            StringAssert.Contains("未登録の依存 1 件", text);
            StringAssert.Contains("Assets/B.png", text);
            StringAssert.DoesNotContain("Assets/A.png", text);
        }

        // 0件のときは、その旨を出す
        [Test]
        public void FormatEmptyShowsNoRowMessage()
        {
            var text = DependencyCheckLogFormatter.Format(NOW, new List<DependencyCheckRow>(), new List<int>(), "x");

            StringAssert.Contains("未登録の依存 0 件（複数グループ 0 件）", text);
            StringAssert.Contains("該当する未登録の依存はありません。", text);
        }

        // サイズ表記の境界
        [TestCase(0L, "0 B")]
        [TestCase(1023L, "1023 B")]
        [TestCase(1024L, "1 KB")]
        [TestCase(4096L, "4 KB")]
        [TestCase(1048575L, "1024 KB")]
        [TestCase(1048576L, "1.0 MB")]
        [TestCase(2621440L, "2.5 MB")]
        public void FormatSizeBoundaries(long bytes, string expected)
        {
            Assert.AreEqual(expected, DependencyCheckLogFormatter.FormatSize(bytes));
        }
    }
}
