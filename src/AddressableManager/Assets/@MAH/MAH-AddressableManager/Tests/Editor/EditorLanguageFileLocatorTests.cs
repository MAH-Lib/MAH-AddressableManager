using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using MAH.AddressableManager.Editor.Localization;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// 言語ファイルを実際に探して読む処理（同梱分・利用者が Assets 配下に置いた分）を、AssetDatabase を使って確かめる。
    /// 利用者のファイルは、テスト用の一時フォルダに作り、終わったら消す。
    /// </summary>
    internal sealed class EditorLanguageFileLocatorTests
    {
        private const string TEMP_FOLDER = "Assets/__MahLocalizationTest";

        private readonly List<EditorLanguageSource> _sources = new();

        [SetUp]
        public void SetUp() => DeleteTempFolder();

        [TearDown]
        public void TearDown()
        {
            DeleteTempFolder();
            EditorText.UseBundledLanguageForTest(EditorTestLanguageFixture.TEST_LANGUAGE_CODE);
        }

        private static void DeleteTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TEMP_FOLDER)) AssetDatabase.DeleteAsset(TEMP_FOLDER);
        }

        private static string CreateUserFile(string file_name, string json)
        {
            if (!AssetDatabase.IsValidFolder(TEMP_FOLDER)) AssetDatabase.CreateFolder("Assets", "__MahLocalizationTest");
            var path = $"{TEMP_FOLDER}/{file_name}";
            File.WriteAllText(path, json);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        // 名前の規則に合うファイルだけを、言語ファイルとして扱う（言語コードも取り出せる）
        [TestCase("MAH.AddressableManager.Editor.fr.json", true, "fr")]
        [TestCase("MAH.AddressableManager.Editor.pt-BR.json", true, "pt-BR")]
        [TestCase("MAH.AddressableManager.Editor.json", false, null)]
        [TestCase("MAH.AddressableManager.Editor.fr.txt", false, null)]
        [TestCase("Other.fr.json", false, null)]
        public void FileNamePatternIsStrict(string file_name, bool expected, string expected_code)
        {
            Assert.AreEqual(expected, EditorLanguageFileLocator.IsLanguageFilePath("Assets/Any/" + file_name));
            Assert.AreEqual(expected, EditorLanguageFileLocator.TryGetLanguageCodeFromFileName(file_name, out var code));
            Assert.AreEqual(expected_code, code);
        }

        // 同梱の ja・en が見つかり、同梱として印が付く
        [Test]
        public void CollectFindsBundledFiles()
        {
            EditorLanguageFileLocator.Collect(_sources);

            var bundled_paths = _sources.FindAll(source => source.AccIsBundled);
            Assert.AreEqual(2, bundled_paths.Count, "同梱の言語ファイルは、ja と en の2つのはず");
            StringAssert.Contains(".ja.json", bundled_paths[0].AccPath + bundled_paths[1].AccPath);
            StringAssert.Contains(".en.json", bundled_paths[0].AccPath + bundled_paths[1].AccPath);
        }

        // 利用者が Assets 配下に置いた言語ファイルが見つかり、同梱の後ろ（優先される側）に並ぶ。名前の規則に合わないファイルは無視する
        [Test]
        public void CollectFindsUserFileAfterBundledOnesAndIgnoresOtherNames()
        {
            var fr = CreateUserFile("MAH.AddressableManager.Editor.fr.json", "{\"languageCode\":\"fr\",\"languageName\":\"Français\",\"entries\":[]}");
            CreateUserFile("Unrelated.json", "{\"languageCode\":\"xx\",\"entries\":[]}");

            EditorLanguageFileLocator.Collect(_sources);

            var user = _sources.FindAll(source => !source.AccIsBundled);
            Assert.AreEqual(1, user.Count);
            Assert.AreEqual(fr, user[0].AccPath);
            Assert.IsFalse(_sources[_sources.Count - 1].AccIsBundled, "利用者のファイルは、同梱の後ろ（後から足すほうが優先）");
        }

        // 利用者の ja のファイルは、同梱の ja を、キー単位で上書きする（他のキーは同梱のまま）
        [Test]
        public void UserFileOverridesBundledLanguagePerKey()
        {
            CreateUserFile("MAH.AddressableManager.Editor.ja.json", "{\"languageCode\":\"ja\",\"entries\":[{\"key\":\"leak.empty\",\"value\":\"OVERRIDE\"}]}");
            EditorLanguageFileLocator.Collect(_sources);

            var table = new EditorLanguageTable();
            foreach (var source in _sources)
            {
                Assert.IsTrue(EditorLanguageTable.TryParse(source.AccJson, out var file, out _));
                table.Add(file);
            }

            Assert.IsTrue(table.TryGet("ja", "leak.empty", out var overridden));
            Assert.AreEqual("OVERRIDE", overridden);
            Assert.IsTrue(table.TryGet("ja", "debug.tab.leak", out var untouched));
            Assert.AreEqual("リーク", untouched);
        }

        // 利用者のファイルを足すと選べる言語に出て、消すと選べなくなる（Preferences の選択肢の元）
        [Test]
        public void ReloadReflectsAddedAndDeletedUserFile()
        {
            CreateUserFile("MAH.AddressableManager.Editor.fr.json", "{\"languageCode\":\"fr\",\"languageName\":\"Français\",\"entries\":[{\"key\":\"debug.notRunning\",\"value\":\"Non démarré.\"}]}");
            EditorText.Reload();
            CollectionAssert.Contains(EditorText.AccLanguageCodes, "fr");
            Assert.AreEqual("Français", EditorText.GetLanguageName("fr"));

            DeleteTempFolder();
            EditorText.Reload();
            CollectionAssert.DoesNotContain(EditorText.AccLanguageCodes, "fr");
        }

        // 形式を誤ったファイルは無視され、他の言語は読める
        [Test]
        public void BrokenUserFileIsIgnoredWithWarning()
        {
            // 警告はここでの読み直しで出る（EditorLanguagePostprocessor の読み直しは遅れて呼ばれ、テスト中は見送られる）。回数は決めず、出たことだけ確かめる
            var warnings = new List<string>();
            void Capture(string message, string stack_trace, UnityEngine.LogType type)
            {
                if (type == UnityEngine.LogType.Warning) warnings.Add(message);
            }

            UnityEngine.Application.logMessageReceived += Capture;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                CreateUserFile("MAH.AddressableManager.Editor.de.json", "{ broken");
                EditorText.Reload();
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
                UnityEngine.Application.logMessageReceived -= Capture;
            }

            Assert.IsTrue(warnings.Exists(message => message.Contains("Ignored the language file") && message.Contains("de.json")), "警告が出ていない");
            CollectionAssert.DoesNotContain(EditorText.AccLanguageCodes, "de");
            CollectionAssert.Contains(EditorText.AccLanguageCodes, "ja");
            CollectionAssert.Contains(EditorText.AccLanguageCodes, "en");
        }
    }
}
