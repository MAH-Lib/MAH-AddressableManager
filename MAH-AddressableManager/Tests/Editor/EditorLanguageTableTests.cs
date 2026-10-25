using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using MAH.AddressableManager.Editor.Localization;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class EditorLanguageTableTests
    {
        private static string Json(string code, string name, params string[] key_value_pairs)
        {
            var entries = new List<string>();
            for (var i = 0; i < key_value_pairs.Length; i += 2)
                entries.Add($"{{\"key\":\"{key_value_pairs[i]}\",\"value\":\"{key_value_pairs[i + 1]}\"}}");
            return $"{{\"languageCode\":\"{code}\",\"languageName\":\"{name}\",\"entries\":[{string.Join(",", entries)}]}}";
        }

        private static EditorLanguageTable CreateTable(params string[] jsons)
        {
            var table = new EditorLanguageTable();
            foreach (var json in jsons)
            {
                Assert.IsTrue(EditorLanguageTable.TryParse(json, out var file, out _));
                table.Add(file);
            }
            return table;
        }

        // このクラスのテストは表を差し替えるため、終わったら、名前空間全体の固定（EditorTestLanguageFixture）に戻す
        [TearDown]
        public void TearDown() => EditorText.UseBundledLanguageForTest(EditorTestLanguageFixture.TEST_LANGUAGE_CODE);

        // 正しい形式の JSON は解析できる
        [Test]
        public void TryParseValidJsonReturnsFile()
        {
            var ok = EditorLanguageTable.TryParse(Json("fr", "Français", "a", "Bonjour"), out var file, out var error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
            Assert.AreEqual("fr", file.languageCode);
            Assert.AreEqual("Français", file.languageName);
            Assert.AreEqual(1, file.entries.Length);
        }

        // 形式の誤り（パースの失敗・languageCode が空・空のファイル）は失敗として扱う
        [TestCase("{ broken")]
        [TestCase("")]
        [TestCase("{\"languageCode\":\"\",\"entries\":[]}")]
        [TestCase("{\"entries\":[]}")]
        public void TryParseInvalidJsonReturnsFalse(string json)
        {
            var ok = EditorLanguageTable.TryParse(json, out var file, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(file);
            Assert.IsNotEmpty(error);
            // 基盤自身のメッセージは英語（翻訳に頼れないため。日本語が混ざらない）
            foreach (var character in error) Assert.Less(character, 128, $"error が英語ではない: {error}");
        }

        // 現在の言語にあるキーは、その言語の文言を返す
        [Test]
        public void TryGetReturnsTextOfRequestedLanguage()
        {
            var table = CreateTable(Json("en", "English", "a", "Hello"), Json("ja", "日本語", "a", "こんにちは"));

            Assert.IsTrue(table.TryGet("ja", "a", out var text));
            Assert.AreEqual("こんにちは", text);
        }

        // 現在の言語に無いキーは、英語にフォールバックする（一部だけ訳した言語ファイルでも動く）
        [Test]
        public void TryGetFallsBackToEnglishWhenKeyIsMissing()
        {
            var table = CreateTable(Json("en", "English", "a", "Hello", "b", "World"), Json("fr", "Français", "a", "Bonjour"));

            Assert.IsTrue(table.TryGet("fr", "b", out var text));
            Assert.AreEqual("World", text);
        }

        // 英語にも無いキーは見つからない（呼び出し側がキー文字列を出す）
        [Test]
        public void TryGetReturnsFalseWhenKeyIsMissingEverywhere()
        {
            var table = CreateTable(Json("en", "English", "a", "Hello"));

            Assert.IsFalse(table.TryGet("en", "zzz", out _));
        }

        // 同じ言語コードを後から足すと、キー単位で上書きされ、他のキーは残る（利用者のファイルが同梱の訳を一部だけ直せる）
        [Test]
        public void AddOverridesPerKeyAndKeepsOtherKeys()
        {
            var table = CreateTable(
                Json("ja", "日本語", "a", "A1", "b", "B1"),
                Json("ja", "日本語", "a", "A2"));

            Assert.IsTrue(table.TryGet("ja", "a", out var a));
            Assert.IsTrue(table.TryGet("ja", "b", out var b));
            Assert.AreEqual("A2", a);
            Assert.AreEqual("B1", b);
            Assert.AreEqual(1, table.AccLanguageCodes.Count);
        }

        // languageName が空の言語は、選択肢に言語コードを出す
        [Test]
        public void GetLanguageNameFallsBackToCode()
        {
            var table = CreateTable(Json("fr", "", "a", "Bonjour"), Json("en", "English", "a", "Hello"));

            Assert.AreEqual("fr", table.GetLanguageName("fr"));
            Assert.AreEqual("English", table.GetLanguageName("en"));
        }

        // 雛形には、英語の全キーが入り、言語コードと名前は指定したものになる
        [Test]
        public void BuildTemplateJsonContainsAllEnglishKeys()
        {
            var table = CreateTable(Json("en", "English", "b", "World", "a", "Hello"));

            var template = table.BuildTemplateJson("en", "fr", "fr");

            Assert.IsTrue(EditorLanguageTable.TryParse(template, out var file, out _));
            Assert.AreEqual("fr", file.languageCode);
            Assert.AreEqual(2, file.entries.Length);
            Assert.AreEqual("a", file.entries[0].key); // キーの順
            Assert.AreEqual("Hello", file.entries[0].value);
        }

        // 英語に在って、その言語に無いキーを検出できる
        [Test]
        public void CollectMissingKeysFindsUntranslatedKeys()
        {
            var table = CreateTable(Json("en", "English", "a", "Hello", "b", "World"), Json("fr", "Français", "a", "Bonjour"));
            var missing = new List<string>();

            table.CollectMissingKeys("fr", "en", missing);

            CollectionAssert.AreEqual(new[] { "b" }, missing);
        }

        [TestCase("none", 0)]
        [TestCase("{0} items", 1)]
        [TestCase("{1} of {0}", 2)]
        [TestCase("{0:F1}%", 1)]
        [TestCase("{{literal}}", 0)]
        [TestCase("{{0}}", 0)] // エスケープされた波括弧の中の数字は、引数ではない
        [TestCase("{{0}} {1}", 2)]
        public void CountFormatArgumentsReturnsMaxIndexPlusOne(string text, int expected)
        {
            Assert.AreEqual(expected, EditorLanguageTable.CountFormatArguments(text));
        }

        // EditorText.Get は、現在の言語 → 英語 → キー文字列の順で返す
        [Test]
        public void EditorTextGetFallsBackToKeyString()
        {
            EditorText.SetTableForTest(CreateTable(Json("en", "English", "a", "Hello")), "en");
            LogAssert.Expect(UnityEngine.LogType.Warning, "[AddressableManager] Missing text key \"zzz\".");

            Assert.AreEqual("Hello", EditorText.Get("a"));
            Assert.AreEqual("zzz", EditorText.Get("zzz"));
        }

        // EditorText.Format は、引数の数がずれた言語ファイルでも例外を出さず、英語の文言で出し直す
        [Test]
        public void EditorTextFormatFallsBackToEnglishOnArgumentMismatch()
        {
            EditorText.SetTableForTest(CreateTable(Json("en", "English", "a", "Count {0}"), Json("fr", "Français", "a", "Compte {0} {1}")), "fr");
            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("does not match its arguments"));

            Assert.AreEqual("Count 3", EditorText.Format("a", 3));
        }

        // 引数の数ごとのオーバーロード（1〜3個・params）のどれでも、同じフォールバック（英語の文言）になる
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void EditorTextFormatFallsBackForEveryOverload(int arg_count)
        {
            EditorText.SetTableForTest(
                CreateTable(Json("en", "English", "a", "Count {0}"), Json("fr", "Français", "a", "Compte {0} {1} {2} {3} {4}")), "fr");
            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("does not match its arguments"));

            var text = arg_count switch
            {
                1 => EditorText.Format("a", 7),
                2 => EditorText.Format("a", 7, 8),
                3 => EditorText.Format("a", 7, 8, 9),
                _ => EditorText.Format("a", 7, 8, 9, 10),
            };

            Assert.AreEqual("Count 7", text);
        }

        // 英語の文言も引数と合わないときは、例外を出さずキー文字列を返す
        [Test]
        public void EditorTextFormatReturnsKeyWhenEnglishAlsoMismatches()
        {
            EditorText.SetTableForTest(CreateTable(Json("en", "English", "a", "Count {0} {1}")), "en");
            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("does not match its arguments"));

            Assert.AreEqual("a", EditorText.Format("a", 3));
        }
    }
}
