using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using MAH.AddressableManager.Editor.Registration;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class RegistrationRulePreviewBuilderTests
    {
        private const string DEFAULT_GROUP = "Default Local Group";
        private static readonly string[] NONE = new string[0];

        private static RegistrationRuleInput Rule(
            string name,
            string folder,
            string[] extensions = null,
            RegistrationTargetMode mode = RegistrationTargetMode.Extensions,
            string address = "",
            string group = "",
            string[] labels = null,
            bool enabled = true)
        {
            return new RegistrationRuleInput(
                name, "guid-" + name, folder, enabled, mode,
                extensions ?? new[] { ".prefab" }, NONE, NONE,
                address, group, string.Empty, labels ?? NONE, RegistrationAddressMode.Follow);
        }

        private static RegistrationAssetInput Asset(string path)
        {
            return new RegistrationAssetInput(path, "guid:" + path, "GameObject", false, string.Empty, string.Empty, null, string.Empty);
        }

        private static RegistrationEnvironment Env()
        {
            return new RegistrationEnvironment(new[] { DEFAULT_GROUP }, DEFAULT_GROUP, NONE, new Dictionary<string, string>(), null);
        }

        private static RegistrationRulePreview Build(RegistrationRuleInput rule, RegistrationAssetInput[] assets, params RegistrationRuleInput[] all_rules)
        {
            return RegistrationRulePreviewBuilder.Build(rule, assets, all_rules, Env());
        }

        // 対象のアセットの数を数え、最初の 3 件（パス順）だけ展開結果を出す
        [Test]
        public void CountsTargetsAndSamplesFirstThreeInPathOrder()
        {
            var rule = Rule("R", "Assets/App");
            var assets = new[]
            {
                Asset("Assets/App/E.prefab"),
                Asset("Assets/App/B.prefab"),
                Asset("Assets/App/D.prefab"),
                Asset("Assets/App/A.prefab"),
                Asset("Assets/App/C.prefab"),
            };
            var preview = Build(rule, assets);

            Assert.AreEqual(5, preview.AccTargetCount);
            Assert.AreEqual(RegistrationRulePreviewBuilder.SAMPLE_COUNT, preview.AccSamples.Count);
            CollectionAssert.AreEqual(
                new[] { "Assets/App/A.prefab", "Assets/App/B.prefab", "Assets/App/C.prefab" },
                preview.AccSamples.Select(s => s.AccAssetPath));
        }

        // ルールに当たらないもの・いつも登録しないもの（スクリプト・Resources）は、数に入れない
        [Test]
        public void ExcludesNonMatchingAndAlwaysExcludedAssets()
        {
            var rule = new RegistrationRuleInput("R", "g", "Assets/App", true, RegistrationTargetMode.All, NONE, NONE, NONE,
                string.Empty, string.Empty, string.Empty, NONE, RegistrationAddressMode.Follow);
            var assets = new[]
            {
                Asset("Assets/App/Title.prefab"),
                Asset("Assets/App/Script.cs"),
                Asset("Assets/App/Resources/R.prefab"),
                Asset("Assets/Other/Out.prefab"),
            };

            Assert.AreEqual(1, Build(rule, assets).AccTargetCount);
        }

        // 展開結果：アドレス・グループ・Label。空のテンプレートはアセットパス・DefaultGroup・Label なし
        [Test]
        public void ExpandsSampleAndFallsBackToDefaults()
        {
            var expanded = Build(
                Rule("R", "Assets/App", address: "{Folder.Top}/{Asset.Name}", group: "G_{Folder.Top}", labels: new[] { "L1", "{Folder.Top}", "L1" }),
                new[] { Asset("Assets/App/Menu/Title.prefab") });
            var sample = expanded.AccSamples.Single();
            Assert.AreEqual("Menu/Title", sample.AccAddress);
            Assert.AreEqual("G_Menu", sample.AccGroup);
            CollectionAssert.AreEqual(new[] { "L1", "Menu" }, sample.AccLabels);

            var defaults = Build(Rule("R", "Assets/App"), new[] { Asset("Assets/App/Title.prefab") });
            var default_sample = defaults.AccSamples.Single();
            Assert.AreEqual("Assets/App/Title.prefab", default_sample.AccAddress);
            Assert.AreEqual(DEFAULT_GROUP, default_sample.AccGroup);
            Assert.AreEqual(0, default_sample.AccLabels.Count);
        }

        // 拡張子が空のルールは、警告が出て、対象は 0 件
        [Test]
        public void EmptyExtensionsWarnsAndHasNoTargets()
        {
            var preview = Build(Rule("R", "Assets/App", extensions: NONE), new[] { Asset("Assets/App/Title.prefab") });

            Assert.AreEqual(0, preview.AccTargetCount);
            Assert.IsTrue(preview.AccWarnings.Any(w => w.Contains("拡張子が空")));
        }

        // All には、バンドルの構成が変わる注意が出る
        [Test]
        public void AllModeShowsBundleNotice()
        {
            var preview = Build(Rule("R", "Assets/App", mode: RegistrationTargetMode.All), new RegistrationAssetInput[0]);

            Assert.IsTrue(preview.AccWarnings.Any(w => w.Contains("All は")));
        }

        // 対象フォルダが未指定なら、警告が出て、アセットは見ない
        [Test]
        public void MissingFolderWarns()
        {
            var preview = Build(Rule("R", string.Empty), new[] { Asset("Assets/App/Title.prefab") });

            Assert.AreEqual(0, preview.AccTargetCount);
            Assert.IsTrue(preview.AccWarnings.Any(w => w.Contains("対象フォルダが未指定")));
        }

        // Enabled がオフなら、警告が出て、対象は 0 件
        [Test]
        public void DisabledRuleWarns()
        {
            var preview = Build(Rule("R", "Assets/App", enabled: false), new[] { Asset("Assets/App/Title.prefab") });

            Assert.AreEqual(0, preview.AccTargetCount);
            Assert.IsTrue(preview.AccWarnings.Any(w => w.Contains("無効")));
        }

        // ほかのルールと重なっていると警告が出る。自分自身（同じ GUID）や、重ならないルールでは出ない
        [Test]
        public void OverlappingRuleWarnsButSelfAndDisjointDoNot()
        {
            var rule = Rule("R", "Assets/App");
            var overlapping = Rule("Other", "Assets");
            var disjoint = Rule("Far", "Assets/Far");

            var preview = Build(rule, new RegistrationAssetInput[0], rule, overlapping, disjoint);

            var overlap_warnings = preview.AccWarnings.Where(w => w.Contains("重なっています")).ToList();
            Assert.AreEqual(1, overlap_warnings.Count);
            StringAssert.Contains("Other", overlap_warnings[0]);
        }

        // 知らないマクロは警告になり、同じ警告は 1 回だけ（複数のアセットで同じ内容でも）
        [Test]
        public void UnknownMacroWarnsOncePerAsset()
        {
            var preview = Build(
                Rule("R", "Assets/App", address: "{Asset.Nmae}"),
                new[] { Asset("Assets/App/A.prefab"), Asset("Assets/App/B.prefab") });

            var macro_warnings = preview.AccWarnings.Where(w => w.Contains("{Asset.Nmae}")).ToList();
            Assert.AreEqual(2, macro_warnings.Count);
            StringAssert.Contains("Assets/App/A.prefab", macro_warnings[0]);
            StringAssert.Contains("Assets/App/B.prefab", macro_warnings[1]);
        }

        // グループ名に使えない文字が入ると、警告が出る
        [Test]
        public void InvalidGroupNameWarns()
        {
            var preview = Build(
                Rule("R", "Assets/App", group: "{Folder.Relative}"),
                new[] { Asset("Assets/App/Sub/Deep/Title.prefab") });

            Assert.IsTrue(preview.AccWarnings.Any(w => w.Contains("グループ名に使えない文字")));
        }
    }
}
