using System.Collections.Generic;
using NUnit.Framework;
using MAH.AddressableManager.Editor.Window;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class DependencyCheckAggregatorTests
    {
        private DependencyCheckInput _input;
        private readonly List<DependencyCheckRow> _rows = new();

        [SetUp]
        public void SetUp()
        {
            _input = new DependencyCheckInput();
            _rows.Clear();
        }

        // 親を登録済みとして足し、直接の依存を持たせる
        private void AddParent(string path, string group, params string[] dependencies)
        {
            _input.AccParents.Add(new DependencyCheckInput.Parent(path, group));
            _input.AccRegisteredPaths.Add(path);
            AddNode(path, dependencies);
        }

        // 親ではないアセット（未登録の中間など）に、直接の依存を持たせる
        private void AddNode(string path, params string[] dependencies)
        {
            _input.AccDirectDependencies[path] = new List<string>(dependencies);
        }

        private DependencyCheckRow Find(string path) => _rows.Find(row => row.AccPath == path);

        // 登録済みのアセットと、親自身は、行にならない
        [Test]
        public void RegisteredAndSelfAreNotRows()
        {
            AddParent("Assets/A.prefab", "G1", "Assets/A.prefab", "Assets/B.prefab");
            AddParent("Assets/B.prefab", "G1");

            DependencyCheckAggregator.Aggregate(_input, _rows);

            Assert.AreEqual(0, _rows.Count);
        }

        // 未登録の子1つにつき1行。型・サイズ・ファイル名が載る。型が無いときは Unknown
        [Test]
        public void UnregisteredChildBecomesOneRowWithTypeAndSize()
        {
            AddParent("Assets/A.prefab", "G1", "Assets/Art/Tex.png", "Assets/Art/Other.bin");
            _input.AccTypeNames["Assets/Art/Tex.png"] = "Texture2D";
            _input.AccSizes["Assets/Art/Tex.png"] = 2048;

            DependencyCheckAggregator.Aggregate(_input, _rows);

            Assert.AreEqual(2, _rows.Count);
            var row = Find("Assets/Art/Tex.png");
            Assert.AreEqual("Tex.png", row.AccName);
            Assert.AreEqual("Texture2D", row.AccTypeName);
            Assert.AreEqual(2048, row.AccSizeBytes);
            Assert.AreEqual("Unknown", Find("Assets/Art/Other.bin").AccTypeName);
        }

        // 親と孫の間が未登録のアセットだけなら、孫の参照元は親で、直接の使用になる
        [Test]
        public void GrandchildThroughUnregisteredIsDirect()
        {
            AddParent("Assets/A.prefab", "G1", "Assets/Mat.mat"); // Mat は未登録。Tex は Mat の依存（孫）
            AddNode("Assets/Mat.mat", "Assets/Tex.png");

            DependencyCheckAggregator.Aggregate(_input, _rows);

            var referrers = Find("Assets/Tex.png").AccReferrers;
            Assert.AreEqual(1, referrers.Count);
            Assert.AreEqual("Assets/A.prefab", referrers[0].AccPath);
            Assert.IsFalse(referrers[0].AccIsVia);
        }

        // 登録済みのアセットを通して届く子は、参照元が経由になり、経由先が記録される。経由先自身は直接の使用
        [Test]
        public void ChildThroughRegisteredAssetIsVia()
        {
            AddParent("Assets/Title.prefab", "A", "Assets/Btn.mat");
            AddParent("Assets/Btn.mat", "B", "Assets/Tex.png");

            DependencyCheckAggregator.Aggregate(_input, _rows);

            Assert.AreEqual(1, _rows.Count);
            var row = Find("Assets/Tex.png");
            Assert.AreEqual(2, row.AccGroupCount); // 経由の使用も、グループ数・親の数に数える
            Assert.IsTrue(row.AccIsMultiGroup);
            Assert.AreEqual(2, row.AccReferrers.Count);

            var btn = row.AccReferrers.Find(referrer => referrer.AccPath == "Assets/Btn.mat");
            Assert.IsFalse(btn.AccIsVia);

            var title = row.AccReferrers.Find(referrer => referrer.AccPath == "Assets/Title.prefab");
            Assert.IsTrue(title.AccIsVia);
            Assert.AreEqual("Assets/Btn.mat", title.AccViaPath);
            Assert.AreEqual(1, title.AccViaCount);
        }

        // 直接の使用と経由の使用の両方があるときは、直接として扱う
        [Test]
        public void DirectUseWinsOverVia()
        {
            AddParent("Assets/Title.prefab", "A", "Assets/Btn.mat", "Assets/Tex.png");
            AddParent("Assets/Btn.mat", "B", "Assets/Tex.png");

            DependencyCheckAggregator.Aggregate(_input, _rows);

            var title = Find("Assets/Tex.png").AccReferrers.Find(referrer => referrer.AccPath == "Assets/Title.prefab");
            Assert.IsFalse(title.AccIsVia);
        }

        // 経由先が複数あるときは、パス順で最初の1つと、経由先の数が記録される
        [Test]
        public void MultipleViaTargetsKeepFirstByPathAndCount()
        {
            AddParent("Assets/A.prefab", "G1", "Assets/M2.mat", "Assets/M1.mat"); // 並びは M2 が先
            AddParent("Assets/M1.mat", "G2", "Assets/Tex.png");
            AddParent("Assets/M2.mat", "G2", "Assets/Tex.png");

            DependencyCheckAggregator.Aggregate(_input, _rows);

            var a = Find("Assets/Tex.png").AccReferrers.Find(referrer => referrer.AccPath == "Assets/A.prefab");
            Assert.IsTrue(a.AccIsVia);
            Assert.AreEqual("Assets/M1.mat", a.AccViaPath);
            Assert.AreEqual(2, a.AccViaCount);
        }

        // 登録済みのアセットが連なっていても、最初に出会う登録済みのアセットが経由先になる
        [Test]
        public void ViaTargetIsNearestRegisteredAsset()
        {
            AddParent("Assets/A.prefab", "G1", "Assets/R1.mat");
            AddParent("Assets/R1.mat", "G2", "Assets/R2.mat");
            AddParent("Assets/R2.mat", "G3", "Assets/Tex.png");

            DependencyCheckAggregator.Aggregate(_input, _rows);

            var referrers = Find("Assets/Tex.png").AccReferrers;
            var a = referrers.Find(referrer => referrer.AccPath == "Assets/A.prefab");
            Assert.AreEqual("Assets/R1.mat", a.AccViaPath);
            var r1 = referrers.Find(referrer => referrer.AccPath == "Assets/R1.mat");
            Assert.AreEqual("Assets/R2.mat", r1.AccViaPath);
            Assert.IsFalse(referrers.Find(referrer => referrer.AccPath == "Assets/R2.mat").AccIsVia);
        }

        // 依存が循環していても終わる
        [Test]
        public void CircularDependenciesTerminate()
        {
            AddParent("Assets/A.prefab", "G1", "Assets/X.png", "Assets/R.mat");
            AddNode("Assets/X.png", "Assets/Y.png"); // X と Y は未登録で、互いを参照する
            AddNode("Assets/Y.png", "Assets/X.png");
            AddParent("Assets/R.mat", "G2", "Assets/A.prefab", "Assets/Y.png"); // R と A も互いを参照する

            DependencyCheckAggregator.Aggregate(_input, _rows);

            Assert.AreEqual(2, _rows.Count);
            Assert.IsNotNull(Find("Assets/X.png"));
            Assert.IsNotNull(Find("Assets/Y.png"));
        }

        // 別グループの2つの親から使われると、グループ数 2 で複数グループ
        [Test]
        public void DifferentGroupsAreMultiGroup()
        {
            AddParent("Assets/A.prefab", "G1", "Assets/Tex.png");
            AddParent("Assets/B.prefab", "G2", "Assets/Tex.png");

            DependencyCheckAggregator.Aggregate(_input, _rows);

            var row = Find("Assets/Tex.png");
            Assert.AreEqual(2, row.AccGroupCount);
            Assert.IsTrue(row.AccIsMultiGroup);
            Assert.AreEqual(2, row.AccReferrers.Count);
        }

        // 同じグループの2つの親から使われても、複数グループにしない（親の数は 2、グループ数は 1）
        [Test]
        public void SameGroupIsNotMultiGroup()
        {
            AddParent("Assets/A.prefab", "G1", "Assets/Tex.png");
            AddParent("Assets/B.prefab", "G1", "Assets/Tex.png");

            DependencyCheckAggregator.Aggregate(_input, _rows);

            var row = Find("Assets/Tex.png");
            Assert.AreEqual(1, row.AccGroupCount);
            Assert.IsFalse(row.AccIsMultiGroup);
            Assert.AreEqual(2, row.AccReferrers.Count);
        }

        // 同じ親が同じ子を重複して持っていても、参照元は1件
        [Test]
        public void SameParentDependencyListedTwiceIsOneReferrer()
        {
            AddParent("Assets/A.prefab", "G1", "Assets/Tex.png", "Assets/Tex.png");

            DependencyCheckAggregator.Aggregate(_input, _rows);

            Assert.AreEqual(1, Find("Assets/Tex.png").AccReferrers.Count);
        }

        // パスに /Resources/ を含む子に印が付く
        [Test]
        public void ResourcesPathIsMarked()
        {
            AddParent("Assets/A.prefab", "G1", "Assets/Resources/Tex.png", "Assets/ResourcesX/Tex.png");

            DependencyCheckAggregator.Aggregate(_input, _rows);

            Assert.IsTrue(Find("Assets/Resources/Tex.png").AccIsResources);
            Assert.IsFalse(Find("Assets/ResourcesX/Tex.png").AccIsResources);
        }

        // .cs・.dll（大文字小文字を問わない）・Packages/ は、既定で隠す印が付き、行には残る
        [TestCase("Assets/Scripts/Foo.cs", true)]
        [TestCase("Assets/Plugins/Bar.DLL", true)]
        [TestCase("Packages/com.unity.x/Shader.shader", true)]
        [TestCase("Assets/Art/Tex.png", false)]
        public void HiddenByDefaultMark(string path, bool expected)
        {
            AddParent("Assets/A.prefab", "G1", path);

            DependencyCheckAggregator.Aggregate(_input, _rows);

            Assert.AreEqual(1, _rows.Count);
            Assert.AreEqual(expected, _rows[0].AccIsHiddenByDefault);
        }

        // 参照元に Pack Separately のグループの親を含む行にだけ、印が付く
        [Test]
        public void PackSeparatelyMarkOnlyWhenReferrerInPackSeparatelyGroup()
        {
            _input.AccPackSeparatelyGroups.Add("Sep");
            AddParent("Assets/A.prefab", "Sep", "Assets/X.png");
            AddParent("Assets/B.prefab", "Normal", "Assets/Y.png");

            DependencyCheckAggregator.Aggregate(_input, _rows);

            Assert.IsTrue(Find("Assets/X.png").AccIsPackSeparately);
            Assert.IsFalse(Find("Assets/Y.png").AccIsPackSeparately);
        }

        // 行はパス順、参照元もパス順。Aggregate は結果を作り直す（前回の行は残らない）
        [Test]
        public void RowsAndReferrersAreSortedByPathAndResultIsReplaced()
        {
            _rows.Add(null); // 前回の結果
            AddParent("Assets/B.prefab", "G1", "Assets/Z.png", "Assets/A.png");
            AddParent("Assets/A.prefab", "G2", "Assets/Z.png");

            DependencyCheckAggregator.Aggregate(_input, _rows);

            Assert.AreEqual(2, _rows.Count);
            Assert.AreEqual("Assets/A.png", _rows[0].AccPath);
            Assert.AreEqual("Assets/Z.png", _rows[1].AccPath);
            Assert.AreEqual("Assets/A.prefab", _rows[1].AccReferrers[0].AccPath);
            Assert.AreEqual("Assets/B.prefab", _rows[1].AccReferrers[1].AccPath);
        }
    }
}
