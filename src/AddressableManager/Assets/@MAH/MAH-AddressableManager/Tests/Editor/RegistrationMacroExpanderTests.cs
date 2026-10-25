using System.Collections.Generic;
using NUnit.Framework;
using MAH.AddressableManager.Editor.Registration;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class RegistrationMacroExpanderTests
    {
        private const string ASSET_PATH = "Assets/App/Audio/BGM/Title.wav";
        private const string ROOT = "Assets/App";

        private List<string> _warnings;

        [SetUp]
        public void SetUp()
        {
            _warnings = new List<string>();
        }

        private static RegistrationMacroContext Context(
            string asset_path = ASSET_PATH,
            string root = ROOT,
            string rule_name = "BGM_Rule",
            Dictionary<string, string> standard_values = null)
        {
            return new RegistrationMacroContext(asset_path, "3f2a", "AudioClip", root, rule_name, standard_values);
        }

        private string Expand(string template, RegistrationMacroContext context = null)
        {
            return RegistrationMacroExpander.Expand(template, context ?? Context(), _warnings);
        }

        // ---- アセット・フォルダのマクロ（仕様 6.2。例は Assets/App/Audio/BGM/Title.wav、対象フォルダは Assets/App） ----

        [TestCase("{Asset.Path}", "Assets/App/Audio/BGM/Title.wav")]
        [TestCase("{Asset.Name}", "Title")]
        [TestCase("{Asset.Extension}", "wav")]
        [TestCase("{Asset.Type}", "AudioClip")]
        [TestCase("{Asset.Guid}", "3f2a")]
        [TestCase("{Folder.Name}", "BGM")]
        [TestCase("{Folder.Top}", "Audio")]
        [TestCase("{Folder.Relative}", "Audio/BGM")]
        [TestCase("{Root}", "Assets/App")]
        [TestCase("{Path.Relative}", "Audio/BGM/Title")]
        [TestCase("{Rule.Name}", "BGM_Rule")]
        public void ExpandsAssetAndFolderMacros(string template, string expected)
        {
            Assert.AreEqual(expected, Expand(template));
            Assert.AreEqual(0, _warnings.Count);
        }

        // マクロは文字列の途中にも書け、複数並べられる。マクロの無い文字列はそのまま返る
        [Test]
        public void ExpandsMacrosInsideText()
        {
            Assert.AreEqual("Download/Audio/Title", Expand("Download/{Folder.Top}/{Asset.Name}"));
            Assert.AreEqual("plain-text", Expand("plain-text"));
            Assert.AreEqual(0, _warnings.Count);
        }

        // 空・null のテンプレートは、空文字になる
        [TestCase("")]
        [TestCase(null)]
        public void EmptyTemplateReturnsEmpty(string template)
        {
            Assert.AreEqual(string.Empty, Expand(template));
        }

        // 対象フォルダの直下のアセットでは、フォルダ系のマクロが空になる
        [Test]
        public void AssetDirectlyUnderRootHasEmptyFolderMacros()
        {
            var context = Context(asset_path: "Assets/App/Title.wav");

            Assert.AreEqual(string.Empty, Expand("{Folder.Top}", context));
            Assert.AreEqual(string.Empty, Expand("{Folder.Relative}", context));
            Assert.AreEqual("App", Expand("{Folder.Name}", context));
            Assert.AreEqual("Title", Expand("{Path.Relative}", context));
        }

        // 対象フォルダの外のアセットは、パス全体を使う
        [Test]
        public void AssetOutsideRootUsesWholePath()
        {
            var context = Context(asset_path: "Assets/Other/Sub/Icon.png", root: ROOT);

            Assert.AreEqual("Assets/Other/Sub/Icon", Expand("{Path.Relative}", context));
            Assert.AreEqual("Assets/Other/Sub", Expand("{Folder.Relative}", context));
        }

        // 拡張子が無いアセットでは、拡張子のマクロが空になる
        [Test]
        public void AssetWithoutExtension()
        {
            var context = Context(asset_path: "Assets/App/Data/NoExt");

            Assert.AreEqual(string.Empty, Expand("{Asset.Extension}", context));
            Assert.AreEqual("NoExt", Expand("{Asset.Name}", context));
        }

        // 手動一括登録では {Rule.Name} が空になる
        [Test]
        public void RuleNameIsEmptyWithoutRule()
        {
            Assert.AreEqual(string.Empty, Expand("{Rule.Name}", Context(rule_name: string.Empty)));
        }

        // 対象フォルダの末尾の / や、区切りの \ があっても同じ結果になる
        [Test]
        public void RootAndPathAreNormalized()
        {
            var context = Context(asset_path: "Assets\\App\\Audio\\Title.wav", root: "Assets/App/");

            Assert.AreEqual("Audio/Title", Expand("{Path.Relative}", context));
            Assert.AreEqual("Assets/App", Expand("{Root}", context));
        }

        // ---- 標準の値（仕様 6.3） ----

        [Test]
        public void ExpandsStandardValues()
        {
            var values = new Dictionary<string, string>
            {
                { "UnityEngine.Application.version", "1.2.0" },
                { "UnityEditor.EditorUserBuildSettings.activeBuildTarget", "Android" },
            };
            var context = Context(standard_values: values);

            Assert.AreEqual("v1.2.0", Expand("v{UnityEngine.Application.version}", context));
            Assert.AreEqual("Android/Title", Expand("{UnityEditor.EditorUserBuildSettings.activeBuildTarget}/{Asset.Name}", context));
            Assert.AreEqual(0, _warnings.Count);
        }

        // 一覧にある標準の値が、表に無い（取得できなかった）ときは、空になる。警告は出さない
        [Test]
        public void StandardValueMissingInTableIsEmpty()
        {
            Assert.AreEqual("v", Expand("v{UnityEngine.Application.productName}"));
            Assert.AreEqual(0, _warnings.Count);
        }

        // 一覧にない static プロパティは、評価せず、知らないマクロとして扱う
        [Test]
        public void ArbitraryStaticPropertyIsUnknown()
        {
            var values = new Dictionary<string, string> { { "UnityEngine.Application.dataPath", "x" } };

            Assert.AreEqual("{UnityEngine.Application.dataPath}", Expand("{UnityEngine.Application.dataPath}", Context(standard_values: values)));
            Assert.AreEqual(1, _warnings.Count);
        }

        // 標準の値を一覧どおりに8つ用意している
        [Test]
        public void StandardMacroNamesMatchSpec()
        {
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "UnityEngine.Application.version",
                    "UnityEngine.Application.productName",
                    "UnityEngine.Application.companyName",
                    "UnityEngine.Application.identifier",
                    "UnityEngine.Application.unityVersion",
                    "UnityEngine.Application.platform",
                    "UnityEditor.PlayerSettings.bundleVersion",
                    "UnityEditor.EditorUserBuildSettings.activeBuildTarget",
                },
                RegistrationMacroExpander.STANDARD_MACRO_NAMES);
        }

        // ---- 大文字・小文字の変換（仕様 6.4） ----

        [Test]
        public void LowerAndUpperTransforms()
        {
            Assert.AreEqual("title", Expand("{Asset.Name:lower}"));
            Assert.AreEqual("TITLE", Expand("{Asset.Name:upper}"));
            Assert.AreEqual("audio/bgm", Expand("{Folder.Relative:lower}"));
            Assert.AreEqual(0, _warnings.Count);
        }

        // 標準の値にも変換を付けられる
        [Test]
        public void TransformWorksOnStandardValue()
        {
            var values = new Dictionary<string, string> { { "UnityEngine.Application.platform", "WindowsEditor" } };

            Assert.AreEqual("windowseditor", Expand("{UnityEngine.Application.platform:lower}", Context(standard_values: values)));
        }

        // 知らない変換は、書かれたままにして警告する
        [TestCase("{Asset.Name:camel}")]
        [TestCase("{Asset.Name:LOWER}")]
        [TestCase("{Asset.Name:}")]
        public void UnknownTransformIsKeptAndWarned(string template)
        {
            Assert.AreEqual(template, Expand(template));
            Assert.AreEqual(1, _warnings.Count);
        }

        // ---- 知らないマクロ・大文字小文字の区別（仕様 6.5） ----

        // 知らないマクロは、書かれたままにして警告する。ほかのマクロは展開される
        [Test]
        public void UnknownMacroIsKeptAndWarned()
        {
            Assert.AreEqual("{Asset.Nmae}/Title", Expand("{Asset.Nmae}/{Asset.Name}"));
            Assert.AreEqual(1, _warnings.Count);
            StringAssert.Contains("{Asset.Nmae}", _warnings[0]);
        }

        // マクロの名前は大文字と小文字を区別する
        [Test]
        public void MacroNamesAreCaseSensitive()
        {
            Assert.AreEqual("{asset.name}", Expand("{asset.name}"));
            Assert.AreEqual(1, _warnings.Count);
        }

        // 知らないマクロが複数あれば、警告も複数出る
        [Test]
        public void EachUnknownMacroAddsWarning()
        {
            Expand("{A}{B}{Asset.Name}");

            Assert.AreEqual(2, _warnings.Count);
        }

        // 空の {} は、知らないマクロとして扱う
        [Test]
        public void EmptyBracesAreUnknown()
        {
            Assert.AreEqual("{}", Expand("{}"));
            Assert.AreEqual(1, _warnings.Count);
        }

        // 閉じていない { は、マクロとして扱わず、そのまま残す（警告もしない）
        [Test]
        public void UnclosedBraceIsLiteral()
        {
            Assert.AreEqual("abc{Asset.Name", Expand("abc{Asset.Name"));
            Assert.AreEqual("Title{", Expand("{Asset.Name}{"));
            Assert.AreEqual(0, _warnings.Count);
        }

        // 警告の置き場所に null を渡しても、例外にならない
        [Test]
        public void NullWarningsIsAllowed()
        {
            Assert.AreEqual("{Nope}", RegistrationMacroExpander.Expand("{Nope}", Context(), null));
        }

        // ---- 展開した結果が空 / グループ名 ----

        // 展開した結果が空になるときは、空文字を返す（空のときの置き換えは呼び出し側）
        [Test]
        public void ExpandedEmptyResultIsReturnedAsEmpty()
        {
            var context = Context(asset_path: "Assets/App/Title.wav");

            Assert.AreEqual(string.Empty, Expand("{Folder.Top}", context));
        }

        [TestCase("UI", true)]
        [TestCase("Audio BGM", true)]
        [TestCase("Group_01-A", true)]
        [TestCase("Audio/BGM", false)]
        [TestCase("a\\b", false)]
        [TestCase("a:b", false)]
        [TestCase("a*b", false)]
        [TestCase("", false)]
        [TestCase("   ", false)]
        [TestCase(null, false)]
        public void GroupNameValidity(string group_name, bool expected)
        {
            Assert.AreEqual(expected, RegistrationMacroExpander.IsValidGroupName(group_name));
        }
    }
}
