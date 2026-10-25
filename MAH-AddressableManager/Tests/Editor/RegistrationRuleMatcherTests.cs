using System.Collections.Generic;
using NUnit.Framework;
using MAH.AddressableManager.Editor.Registration;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class RegistrationRuleMatcherTests
    {
        private static readonly string[] NONE = new string[0];

        // 拡張子を指定するルールを作る
        private static RegistrationRuleInput ExtRule(string name, string folder, string[] extensions, bool enabled = true, string[] exclude_patterns = null)
        {
            return new RegistrationRuleInput(
                name, name, folder, enabled, RegistrationTargetMode.Extensions,
                extensions, NONE, exclude_patterns ?? NONE,
                string.Empty, string.Empty, string.Empty, NONE, RegistrationAddressMode.Follow);
        }

        // All のルールを作る
        private static RegistrationRuleInput AllRule(string name, string folder, string[] excluded_extensions = null, string[] exclude_patterns = null)
        {
            return new RegistrationRuleInput(
                name, name, folder, true, RegistrationTargetMode.All,
                NONE, excluded_extensions ?? NONE, exclude_patterns ?? NONE,
                string.Empty, string.Empty, string.Empty, NONE, RegistrationAddressMode.Follow);
        }

        // ---- 対象フォルダ・拡張子 ----

        // 対象フォルダの配下で、拡張子が合えば当たる
        [Test]
        public void MatchesAssetUnderFolderWithExtension()
        {
            var rule = ExtRule("R", "Assets/App", new[] { ".prefab" });

            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Title.prefab"));
            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Sub/Deep/Title.prefab"));
        }

        // 対象フォルダの外・名前が似ているだけのフォルダは当たらない
        [TestCase("Assets/Other/Title.prefab")]
        [TestCase("Assets/AppX/Title.prefab")]
        [TestCase("Assets/Title.prefab")]
        [TestCase("Assets/App")]
        public void DoesNotMatchOutsideFolder(string asset_path)
        {
            var rule = ExtRule("R", "Assets/App", new[] { ".prefab" });

            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(rule, asset_path));
        }

        // 拡張子が合わなければ当たらない
        [Test]
        public void DoesNotMatchOtherExtension()
        {
            var rule = ExtRule("R", "Assets/App", new[] { ".prefab" });

            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Icon.png"));
            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/NoExtension"));
        }

        // 拡張子は大文字小文字を区別せず、ドットの有無も問わない
        [TestCase("png")]
        [TestCase(".png")]
        [TestCase(".PNG")]
        [TestCase("  Png ")]
        public void ExtensionIsNormalized(string configured)
        {
            var rule = ExtRule("R", "Assets/App", new[] { configured });

            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Icon.png"));
            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Icon.PNG"));
        }

        // 拡張子の指定が空なら、何にも当たらない
        [Test]
        public void EmptyExtensionsMatchNothing()
        {
            var rule = ExtRule("R", "Assets/App", NONE);

            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Title.prefab"));
        }

        // 空白だけの拡張子は無いものとして扱い、何にも当たらない
        [Test]
        public void BlankExtensionsMatchNothing()
        {
            var rule = ExtRule("R", "Assets/App", new[] { "", "  ", null });

            Assert.AreEqual(0, rule.AccExtensions.Count);
            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Title.prefab"));
        }

        // フォルダのパスは、末尾の / や \ の区切りがあっても同じ扱いになる
        [TestCase("Assets/App/")]
        [TestCase("Assets\\App")]
        public void FolderPathIsNormalized(string folder)
        {
            var rule = ExtRule("R", folder, new[] { ".prefab" });

            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Title.prefab"));
        }

        // 対象フォルダが空なら、何にも当たらない
        [Test]
        public void EmptyFolderMatchesNothing()
        {
            var rule = ExtRule("R", string.Empty, new[] { ".prefab" });

            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Title.prefab"));
        }

        // Enabled がオフのルールは、無いものとして扱う
        [Test]
        public void DisabledRuleDoesNotMatch()
        {
            var rule = ExtRule("R", "Assets/App", new[] { ".prefab" }, enabled: false);

            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Title.prefab"));
        }

        // ---- All ----

        // All は、拡張子に関係なく当たる
        [Test]
        public void AllMatchesAnyExtension()
        {
            var rule = AllRule("R", "Assets/App");

            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Title.prefab"));
            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Icon.png"));
            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/NoExtension"));
        }

        // All の除外する拡張子は当たらない（大文字小文字・ドットの有無を問わない）
        [Test]
        public void AllExcludesExtensions()
        {
            var rule = AllRule("R", "Assets/App", new[] { ".PNG", "mat" });

            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Icon.png"));
            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Mat.MAT"));
            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Title.prefab"));
        }

        // ---- 除外パターン（glob） ----

        // 除外パターンに当たるアセットは、対象フォルダの配下でも当たらない
        [Test]
        public void ExcludePatternRemovesMatches()
        {
            var rule = ExtRule("R", "Assets/App", new[] { ".prefab" }, exclude_patterns: new[] { "**/_Test/**" });

            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/_Test/Title.prefab"));
            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Sub/_Test/Deep/Title.prefab"));
            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Sub/Title.prefab"));
            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/_Tests/Title.prefab"));
        }

        // 空の除外パターンは無視される
        [Test]
        public void BlankExcludePatternIsIgnored()
        {
            var rule = ExtRule("R", "Assets/App", new[] { ".prefab" }, exclude_patterns: new[] { "", "  " });

            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(rule, "Assets/App/Title.prefab"));
        }

        [TestCase("**/_Test/**", "Assets/_Test/a.png", true)]
        [TestCase("**/_Test/**", "Assets/A/B/_Test/a.png", true)]
        [TestCase("**/_Test/**", "Assets/A/_Tests/a.png", false)]
        [TestCase("**/Editor/**", "Assets/App/Editor/Tool.asset", true)]
        [TestCase("**/Editor/**", "Assets/App/Editors/Tool.asset", false)]
        [TestCase("**/*.tmp", "Assets/A/b.tmp", true)]
        [TestCase("**/*.tmp", "Assets/A/b.png", false)]
        [TestCase("Assets/*/a.png", "Assets/X/a.png", true)]
        [TestCase("Assets/*/a.png", "Assets/X/Y/a.png", false)]
        [TestCase("Assets/?/a.png", "Assets/X/a.png", true)]
        [TestCase("Assets/?/a.png", "Assets/XY/a.png", false)]
        [TestCase("assets/app/**", "Assets/App/a.png", true)]
        [TestCase("Assets/a.b", "Assets/aXb", false)]
        public void GlobMatching(string pattern, string path, bool expected)
        {
            Assert.AreEqual(expected, RegistrationRuleMatcher.IsGlobMatch(pattern, path));
        }

        // ---- いつも登録しないもの ----

        [TestCase("Assets/App/Script.cs")]
        [TestCase("Assets/App/Game.asmdef")]
        [TestCase("Assets/App/Game.asmref")]
        [TestCase("Assets/App/Title.prefab.meta")]
        [TestCase("Assets/App/SCRIPT.CS")]
        [TestCase("Assets/App/Plugin.dll")]
        [TestCase("Assets/App/Preset.preset")]
        [TestCase("Assets/App/Editor/Tool.prefab")]
        [TestCase("Assets/Editor/Tool.prefab")]
        [TestCase("Packages/com.example/Title.prefab")]
        [TestCase("ProjectSettings/Foo.asset")]
        [TestCase("")]
        public void IgnoredPathsAreExcluded(string asset_path)
        {
            Assert.AreEqual(RegistrationExclusionKind.Ignored, RegistrationRuleMatcher.GetExclusionKind(asset_path));
        }

        // Resources の配下は、警告つきで登録しない（Assets 直下の Resources を含む）
        [TestCase("Assets/Resources/Title.prefab")]
        [TestCase("Assets/App/Resources/Title.prefab")]
        [TestCase("Assets/App/resources/Sub/Title.prefab")]
        public void ResourcesPathsAreFlagged(string asset_path)
        {
            Assert.AreEqual(RegistrationExclusionKind.Resources, RegistrationRuleMatcher.GetExclusionKind(asset_path));
        }

        // 名前が似ているだけのフォルダは Resources ではない
        [Test]
        public void ResourcesLikeNameIsNotExcluded()
        {
            Assert.AreEqual(RegistrationExclusionKind.None, RegistrationRuleMatcher.GetExclusionKind("Assets/App/MyResources/Title.prefab"));
            Assert.AreEqual(RegistrationExclusionKind.None, RegistrationRuleMatcher.GetExclusionKind("Assets/App/MyEditor/Title.prefab"));
            Assert.AreEqual(RegistrationExclusionKind.None, RegistrationRuleMatcher.GetExclusionKind("Assets/App/Title.prefab"));
        }

        // All でも、いつも登録しないものは当たるルールに入らない
        [Test]
        public void FindMatchesSkipsAlwaysExcluded()
        {
            var rules = new[] { AllRule("R", "Assets/App") };
            var result = new List<RegistrationRuleInput>();

            RegistrationRuleMatcher.FindMatches(rules, "Assets/App/Script.cs", result);
            Assert.AreEqual(0, result.Count);

            RegistrationRuleMatcher.FindMatches(rules, "Assets/App/Resources/Title.prefab", result);
            Assert.AreEqual(0, result.Count);
        }

        // ---- 当たるルールの収集 ----

        // 当たるルールが 0 件・1 件・2 件（競合）になる。result は呼ぶたびに作り直される
        [Test]
        public void FindMatchesCollectsAllMatchingRules()
        {
            var parent = ExtRule("Parent", "Assets", new[] { ".prefab" });
            var child = ExtRule("Child", "Assets/App", new[] { ".prefab" });
            var mat = ExtRule("Mat", "Assets/App", new[] { ".mat" });
            var rules = new[] { parent, child, mat };
            var result = new List<RegistrationRuleInput>();

            RegistrationRuleMatcher.FindMatches(rules, "Assets/App/Title.prefab", result);
            CollectionAssert.AreEquivalent(new[] { parent, child }, result);

            RegistrationRuleMatcher.FindMatches(rules, "Assets/App/Mat.mat", result);
            CollectionAssert.AreEqual(new[] { mat }, result);

            RegistrationRuleMatcher.FindMatches(rules, "Assets/App/Icon.png", result);
            Assert.AreEqual(0, result.Count);
        }

        // 拡張子が違えば、同じフォルダでも別のルールにできる（競合しない）
        [Test]
        public void SameFolderDifferentExtensionsDoNotConflict()
        {
            var prefab = ExtRule("Prefab", "Assets/App", new[] { ".prefab" });
            var mat = ExtRule("Mat", "Assets/App", new[] { ".mat" });
            var result = new List<RegistrationRuleInput>();

            RegistrationRuleMatcher.FindMatches(new[] { prefab, mat }, "Assets/App/Title.prefab", result);

            Assert.AreEqual(1, result.Count);
        }

        // ---- 拡張子の判定の境界（確保しない比較に書き換えたため） ----

        // フォルダ名にドットがあっても、拡張子はファイル名の最後のドットから見る
        [Test]
        public void DotInFolderNameIsNotExtension()
        {
            var png = ExtRule("Png", "Assets/App", new[] { ".png" });
            var all = AllRule("All", "Assets/App", new[] { ".png" });

            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(png, "Assets/App/Icon.png/NoExtension"));
            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(all, "Assets/App/Icon.png/NoExtension"));
            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(png, "Assets/App/v1.2/Icon.png"));
        }

        // ドットで終わる名前は拡張子なしとして扱う（All には当たり、拡張子の指定には当たらない）
        [Test]
        public void TrailingDotIsNoExtension()
        {
            var png = ExtRule("Png", "Assets/App", new[] { ".png" });
            var all = AllRule("All", "Assets/App", new[] { ".png" });

            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(png, "Assets/App/Icon."));
            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(all, "Assets/App/Icon."));
        }

        // 長さの違う拡張子は、前方が一致しても当たらない（png と pngx）
        [Test]
        public void PrefixOfExtensionDoesNotMatch()
        {
            var png = ExtRule("Png", "Assets/App", new[] { ".png" });

            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(png, "Assets/App/Icon.pngx"));
            Assert.IsFalse(RegistrationRuleMatcher.IsMatch(png, "Assets/App/Icon.pn"));
        }

        // \ 区切りのパスも、/ 区切りと同じに判定される
        [Test]
        public void BackslashPathIsNormalized()
        {
            var rule = ExtRule("R", "Assets/App", new[] { ".prefab" });

            Assert.IsTrue(RegistrationRuleMatcher.IsMatch(rule, "Assets\\App\\Title.prefab"));
            Assert.AreEqual(RegistrationExclusionKind.Resources, RegistrationRuleMatcher.GetExclusionKind("Assets\\App\\Resources\\Title.prefab"));
            Assert.AreEqual(RegistrationExclusionKind.Ignored, RegistrationRuleMatcher.GetExclusionKind("Assets\\App\\Script.cs"));
        }

        // ---- 有効なルールのフォルダの配下か（インポートの事前の絞り込み） ----

        // 有効なルールの対象フォルダの配下なら true、外なら false
        [Test]
        public void IsUnderAnyEnabledRuleFolderChecksFolders()
        {
            var rules = new[]
            {
                ExtRule("A", "Assets/App", new[] { ".prefab" }),
                ExtRule("B", "Assets/Item", new[] { ".png" }),
            };

            Assert.IsTrue(RegistrationRuleMatcher.IsUnderAnyEnabledRuleFolder(rules, "Assets/App/Title.prefab"));
            Assert.IsTrue(RegistrationRuleMatcher.IsUnderAnyEnabledRuleFolder(rules, "Assets/Item/Sub/Icon.png"));
            Assert.IsTrue(RegistrationRuleMatcher.IsUnderAnyEnabledRuleFolder(rules, "Assets\\App\\Title.prefab"));
            Assert.IsFalse(RegistrationRuleMatcher.IsUnderAnyEnabledRuleFolder(rules, "Assets/Other/Title.prefab"));
            Assert.IsFalse(RegistrationRuleMatcher.IsUnderAnyEnabledRuleFolder(rules, "Assets/AppX/Title.prefab"));
            Assert.IsFalse(RegistrationRuleMatcher.IsUnderAnyEnabledRuleFolder(rules, "Assets/App"));
        }

        // 拡張子が合わなくても、フォルダの配下なら true（拡張子の判定は、あとの段階で行う）
        [Test]
        public void IsUnderAnyEnabledRuleFolderIgnoresExtension()
        {
            var rules = new[] { ExtRule("A", "Assets/App", new[] { ".prefab" }) };

            Assert.IsTrue(RegistrationRuleMatcher.IsUnderAnyEnabledRuleFolder(rules, "Assets/App/Icon.png"));
        }

        // 無効なルール・対象フォルダが未指定のルールは、無いものとして扱う
        [Test]
        public void IsUnderAnyEnabledRuleFolderSkipsDisabledAndEmptyFolder()
        {
            var rules = new[]
            {
                ExtRule("Disabled", "Assets/App", new[] { ".prefab" }, enabled: false),
                ExtRule("NoFolder", string.Empty, new[] { ".prefab" }),
            };

            Assert.IsFalse(RegistrationRuleMatcher.IsUnderAnyEnabledRuleFolder(rules, "Assets/App/Title.prefab"));
        }

        // ルールが無い・パスが空のときは false
        [Test]
        public void IsUnderAnyEnabledRuleFolderHandlesEmptyInputs()
        {
            var rules = new[] { ExtRule("A", "Assets/App", new[] { ".prefab" }) };

            Assert.IsFalse(RegistrationRuleMatcher.IsUnderAnyEnabledRuleFolder(null, "Assets/App/Title.prefab"));
            Assert.IsFalse(RegistrationRuleMatcher.IsUnderAnyEnabledRuleFolder(NoRules(), "Assets/App/Title.prefab"));
            Assert.IsFalse(RegistrationRuleMatcher.IsUnderAnyEnabledRuleFolder(rules, string.Empty));
            Assert.IsFalse(RegistrationRuleMatcher.IsUnderAnyEnabledRuleFolder(rules, null));
        }

        private static RegistrationRuleInput[] NoRules()
        {
            return new RegistrationRuleInput[0];
        }

        // ---- ルールの重なり（競合しそうか） ----

        // 親フォルダと子フォルダで拡張子が重なると、重なる
        [Test]
        public void NestedFoldersWithSameExtensionOverlap()
        {
            var parent = ExtRule("Parent", "Assets", new[] { ".prefab" });
            var child = ExtRule("Child", "Assets/App", new[] { ".prefab" });

            Assert.IsTrue(RegistrationRuleMatcher.AreOverlapping(parent, child));
            Assert.IsTrue(RegistrationRuleMatcher.AreOverlapping(child, parent));
        }

        // 同じフォルダでも拡張子が違えば重ならない
        [Test]
        public void SameFolderDifferentExtensionsDoNotOverlap()
        {
            var prefab = ExtRule("Prefab", "Assets/App", new[] { ".prefab" });
            var mat = ExtRule("Mat", "Assets/App", new[] { ".mat" });

            Assert.IsFalse(RegistrationRuleMatcher.AreOverlapping(prefab, mat));
        }

        // 同じフォルダで拡張子が重なると、重なる
        [Test]
        public void SameFolderSameExtensionOverlap()
        {
            var a = ExtRule("A", "Assets/App", new[] { ".prefab", ".mat" });
            var b = ExtRule("B", "Assets/App/", new[] { "MAT" });

            Assert.IsTrue(RegistrationRuleMatcher.AreOverlapping(a, b));
        }

        // 別のフォルダ（名前が似ているだけを含む）は、拡張子が同じでも重ならない
        [Test]
        public void SiblingFoldersDoNotOverlap()
        {
            var a = ExtRule("A", "Assets/App", new[] { ".prefab" });
            var b = ExtRule("B", "Assets/AppX", new[] { ".prefab" });

            Assert.IsFalse(RegistrationRuleMatcher.AreOverlapping(a, b));
        }

        // All と拡張子の指定は、除外されていない拡張子があれば重なる（All を優先させない）
        [Test]
        public void AllOverlapsWithExtensionsUnlessExcluded()
        {
            var png = ExtRule("Png", "Assets/App", new[] { ".png" });
            var all = AllRule("All", "Assets/App");
            var all_without_png = AllRule("AllNoPng", "Assets/App", new[] { ".png" });

            Assert.IsTrue(RegistrationRuleMatcher.AreOverlapping(all, png));
            Assert.IsTrue(RegistrationRuleMatcher.AreOverlapping(png, all));
            Assert.IsFalse(RegistrationRuleMatcher.AreOverlapping(all_without_png, png));
        }

        // All 同士は、重なる
        [Test]
        public void AllRulesOverlap()
        {
            Assert.IsTrue(RegistrationRuleMatcher.AreOverlapping(AllRule("A", "Assets/App"), AllRule("B", "Assets")));
        }

        // Enabled がオフ・同じルール同士・null は重ならない
        [Test]
        public void DisabledSameAndNullDoNotOverlap()
        {
            var enabled = ExtRule("A", "Assets/App", new[] { ".prefab" });
            var disabled = ExtRule("B", "Assets/App", new[] { ".prefab" }, enabled: false);

            Assert.IsFalse(RegistrationRuleMatcher.AreOverlapping(enabled, disabled));
            Assert.IsFalse(RegistrationRuleMatcher.AreOverlapping(enabled, enabled));
            Assert.IsFalse(RegistrationRuleMatcher.AreOverlapping(enabled, null));
        }

        // 拡張子が空のルールは、何にも当たらないので重ならない
        [Test]
        public void EmptyExtensionsDoNotOverlap()
        {
            var empty = ExtRule("Empty", "Assets/App", NONE);
            var prefab = ExtRule("Prefab", "Assets/App", new[] { ".prefab" });

            Assert.IsFalse(RegistrationRuleMatcher.AreOverlapping(empty, prefab));
        }
    }
}
