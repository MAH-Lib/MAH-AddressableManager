using NUnit.Framework;
using MAH.AddressableManager.Editor.Localization;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// この名前空間のテスト全体で、Editor の表示言語を日本語に固定する。
    /// Formatter などのテストは日本語の文言を期待値にしているため、実行する人の EditorPrefs・エディタの言語・
    /// Assets 配下の言語ファイルに左右されないようにする。
    /// </summary>
    [SetUpFixture]
    internal sealed class EditorTestLanguageFixture
    {
        internal const string TEST_LANGUAGE_CODE = "ja";

        [OneTimeSetUp]
        public void OneTimeSetUp() => EditorText.UseBundledLanguageForTest(TEST_LANGUAGE_CODE);

        [OneTimeTearDown]
        public void OneTimeTearDown() => EditorText.ResetForTest();
    }
}
