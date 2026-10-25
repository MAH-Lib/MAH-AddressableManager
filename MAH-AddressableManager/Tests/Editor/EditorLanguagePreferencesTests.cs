using NUnit.Framework;
using UnityEngine;
using MAH.AddressableManager.Editor.Localization;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// 言語を選んでいないときの初期値（PC の言語設定から決める。日本語を優先する）。
    /// 実際の PC の設定には左右されないよう、判定だけを、値を渡して確かめる。
    /// </summary>
    internal sealed class EditorLanguagePreferencesTests
    {
        // OS の言語が日本語なら、表示言語・地域が何であっても日本語
        [TestCase(SystemLanguage.Japanese, "en-US", "en-US", "ja")]
        [TestCase(SystemLanguage.Japanese, "", "", "ja")]
        // OS の言語が英語などでも、表示言語か地域のどちらかが日本語なら日本語（日本の PC で、表示言語だけ英語にしている人）
        [TestCase(SystemLanguage.English, "en-US", "ja-JP", "ja")]
        [TestCase(SystemLanguage.English, "ja-JP", "en-US", "ja")]
        [TestCase(SystemLanguage.Unknown, "JA", "", "ja")]
        // どれも日本語でなく、読めているときは英語
        [TestCase(SystemLanguage.English, "en-US", "en-US", "en")]
        [TestCase(SystemLanguage.French, "fr-FR", "fr-FR", "en")]
        [TestCase(SystemLanguage.Unknown, "de-DE", "", "en")]
        [TestCase(SystemLanguage.English, "", "", "en")]
        // どれも読めなかったときは、日本語を優先する
        [TestCase(SystemLanguage.Unknown, "", "", "ja")]
        [TestCase(SystemLanguage.Unknown, null, null, "ja")]
        public void DecideDefaultLanguageCodePrefersJapanese(SystemLanguage system_language, string ui_culture, string culture, string expected)
        {
            Assert.AreEqual(expected, EditorLanguagePreferences.DecideDefaultLanguageCode(system_language, ui_culture, culture));
        }

        // 実際の PC の設定で、決めた初期値が選べる言語（ja か en）になる
        [Test]
        public void GetDefaultLanguageCodeReturnsBundledLanguage()
        {
            CollectionAssert.Contains(new[] { "ja", "en" }, EditorLanguagePreferences.GetDefaultLanguageCode());
        }
    }
}
