using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Editor.Localization;
using MAH.AddressableManager.Editor.SettingsSync;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// 設定 SO の Inspector の見出し・ツールチップが、表示言語に従うことを確かめる。
    /// 描画（IMGUI）そのものではなく、描画に渡す文言（LocalizedPropertyView.GetLabel / GetHeader）を検証する。
    /// </summary>
    internal sealed class LocalizedPropertyViewTests
    {
        private AddressableEditorSettingsSO _editorSettings;
        private AddressableRuntimeSettingsSO _runtimeSettings;
        private SerializedObject _editorSerialized;
        private SerializedObject _runtimeSerialized;

        [SetUp]
        public void SetUp()
        {
            _editorSettings = ScriptableObject.CreateInstance<AddressableEditorSettingsSO>();
            _runtimeSettings = ScriptableObject.CreateInstance<AddressableRuntimeSettingsSO>();
            _editorSerialized = new SerializedObject(_editorSettings);
            _runtimeSerialized = new SerializedObject(_runtimeSettings);
        }

        [TearDown]
        public void TearDown()
        {
            _editorSerialized.Dispose();
            _runtimeSerialized.Dispose();
            Object.DestroyImmediate(_editorSettings);
            Object.DestroyImmediate(_runtimeSettings);
            UseLanguage(EditorTestLanguageFixture.TEST_LANGUAGE_CODE);
        }

        private static void UseLanguage(string language_code)
        {
            EditorText.UseBundledLanguageForTest(language_code);
            LocalizedPropertyView.ClearCacheForTest(); // 言語を差し替えたら、作った GUIContent を捨てる（本番では、言語の変更のイベントで捨てる）
        }

        // 入れ子のクラスの折りたたみの名前は、変数名由来の英語ではなく、見出しの文言（日本語を選べば日本語）
        [TestCase("ja", "_debugWindow", "デバッグウィンドウ")]
        [TestCase("ja", "_leakLog", "リーク")]
        [TestCase("ja", "_registration", "アセットの登録")]
        [TestCase("en", "_debugWindow", "Debug Window")]
        [TestCase("en", "_leakLog", "Leaks")]
        [TestCase("en", "_registration", "Asset Registration")]
        public void EditorSettingsFoldoutLabelFollowsLanguage(string language_code, string property_path, string expected)
        {
            UseLanguage(language_code);

            var label = LocalizedPropertyView.GetLabel(_editorSerialized, _editorSerialized.FindProperty(property_path));

            Assert.AreEqual(expected, label.text);
        }

        [TestCase("ja", "_initialize", "初期化")]
        [TestCase("ja", "_download", "ダウンロード")]
        [TestCase("ja", "_loading", "読み込み・解放")]
        [TestCase("en", "_initialize", "Initialize")]
        [TestCase("en", "_download", "Download")]
        [TestCase("en", "_loading", "Load and Release")]
        public void RuntimeSettingsFoldoutLabelFollowsLanguage(string language_code, string property_path, string expected)
        {
            UseLanguage(language_code);

            var label = LocalizedPropertyView.GetLabel(_runtimeSerialized, _runtimeSerialized.FindProperty(property_path));

            Assert.AreEqual(expected, label.text);
        }

        // 入れ子ではない項目（ログレベル）の見出しは、項目の上に別に描く見出し。言語に従う
        [TestCase("ja", "ログ設定")]
        [TestCase("en", "Log")]
        public void LeafPropertyHeaderFollowsLanguage(string language_code, string expected)
        {
            UseLanguage(language_code);

            var header = LocalizedPropertyView.GetHeader(_runtimeSerialized, _runtimeSerialized.FindProperty("_logLevel"));

            Assert.AreEqual(expected, header);
        }

        // 見出しのキーが無い項目は、見出しなし（null）で、名前は Unity が付ける名前のまま
        [Test]
        public void PropertyWithoutHeaderKeyHasNoHeaderAndKeepsDisplayName()
        {
            UseLanguage("ja");
            var property = _editorSerialized.FindProperty("_debugWindow._refreshIntervalSec");

            Assert.IsNull(LocalizedPropertyView.GetHeader(_editorSerialized, property));
            Assert.AreEqual(property.displayName, LocalizedPropertyView.GetLabel(_editorSerialized, property).text);
        }

        // Foldout で展開するのは、このモジュール自身の型だけ。独自の描画を持ち得る型（UnityEvent・AssetReference など）は PropertyField に任せる
        [Test]
        public void OnlyModuleOwnedTypesAreExpanded()
        {
            Assert.IsTrue(LocalizedPropertyView.IsModuleOwnedType(typeof(MAH.AddressableManager.Core.Loading.AddressableLoadingSettings)));
            Assert.IsTrue(LocalizedPropertyView.IsModuleOwnedType(typeof(AddressableDebugWindowSettings)));
            Assert.IsFalse(LocalizedPropertyView.IsModuleOwnedType(typeof(UnityEngine.Events.UnityEvent)));
            Assert.IsFalse(LocalizedPropertyView.IsModuleOwnedType(typeof(Vector3)));
            Assert.IsFalse(LocalizedPropertyView.IsModuleOwnedType(null));
        }

        // 設定 SO の入れ子のクラスは、これまでどおり折りたたみの名前に見出しが使われる（展開の対象が変わっていない）
        [Test]
        public void ModuleOwnedNestedClassIsStillLabeledByHeader()
        {
            UseLanguage("en");

            var label = LocalizedPropertyView.GetLabel(_runtimeSerialized, _runtimeSerialized.FindProperty("_download"));

            Assert.AreEqual("Download", label.text);
        }

        // 入れ子の項目のツールチップも、言語に従う
        [TestCase("ja", "取り直す間隔")]
        [TestCase("en", "re-reads the running state")]
        public void NestedPropertyTooltipFollowsLanguage(string language_code, string expected_part)
        {
            UseLanguage(language_code);
            var property = _editorSerialized.FindProperty("_debugWindow._refreshIntervalSec");

            var label = LocalizedPropertyView.GetLabel(_editorSerialized, property);

            StringAssert.Contains(expected_part, label.tooltip);
        }
    }
}
