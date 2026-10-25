using System.Collections.Generic;
using NUnit.Framework;
using MAH.AddressableManager.Editor.Localization;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// 同梱の言語ファイル（ja・en）を実際に読み、キーの抜けや、書式の引数のずれがないことを確かめる。
    /// 置き換えの漏れ・翻訳の漏れを、実行前に見つけるためのテスト。
    /// </summary>
    internal sealed class EditorLanguageFileConsistencyTests
    {
        private EditorLanguageTable _bundled;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var sources = new List<EditorLanguageSource>();
            EditorLanguageFileLocator.Collect(sources);

            _bundled = new EditorLanguageTable();
            foreach (var source in sources)
            {
                if (!source.AccIsBundled) continue;
                Assert.IsTrue(EditorLanguageTable.TryParse(source.AccJson, out var file, out var error), $"{source.AccPath}: {error}");
                _bundled.Add(file);
            }
        }

        // 同梱の ja と en が読める
        [Test]
        public void BundledJapaneseAndEnglishAreLoaded()
        {
            Assert.IsTrue(_bundled.HasLanguage("ja"));
            Assert.IsTrue(_bundled.HasLanguage("en"));
        }

        // en にあるキーが ja にすべてある
        [Test]
        public void JapaneseHasAllEnglishKeys()
        {
            var missing = new List<string>();
            _bundled.CollectMissingKeys("ja", "en", missing);

            Assert.IsEmpty(missing, "ja に無いキー：" + string.Join(", ", missing));
        }

        // ja にあるキーが en にすべてある
        [Test]
        public void EnglishHasAllJapaneseKeys()
        {
            var missing = new List<string>();
            _bundled.CollectMissingKeys("en", "ja", missing);

            Assert.IsEmpty(missing, "en に無いキー：" + string.Join(", ", missing));
        }

        // 同じキーで、書式の引数の数が ja と en で一致する（ずれると Format が失敗する）
        [Test]
        public void FormatArgumentCountsMatchBetweenJapaneseAndEnglish()
        {
            Assert.IsTrue(_bundled.TryGetLanguageTexts("ja", out var ja));
            Assert.IsTrue(_bundled.TryGetLanguageTexts("en", out var en));

            var mismatched = new List<string>();
            foreach (var pair in en)
            {
                if (!ja.TryGetValue(pair.Key, out var ja_text)) continue;
                if (EditorLanguageTable.CountFormatArguments(pair.Value) != EditorLanguageTable.CountFormatArguments(ja_text))
                    mismatched.Add(pair.Key);
            }

            Assert.IsEmpty(mismatched, "引数の数がずれているキー：" + string.Join(", ", mismatched));
        }
    }
}
