using NUnit.Framework;
using MAH.AddressableManager.Editor.SettingsSync;
using UnityEditor;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class AddressableLeakLogSettingsTests
    {
        // 既定は、出力先 Logs/AddressableManager・Play 終了時の自動出力 OFF
        [Test]
        public void DefaultValuesAreDefaultFolderAndAutoExportOff()
        {
            var settings = new AddressableLeakLogSettings();

            Assert.AreEqual("Logs/AddressableManager", settings.AccOutputFolder);
            Assert.AreEqual(AddressableLeakLogSettings.DEFAULT_OUTPUT_FOLDER, settings.AccOutputFolder);
            Assert.IsFalse(settings.AccExportOnExitPlayMode);
        }

        // 出力先が空・空白・null のときは、既定値に戻る
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void ConstructorBlankOutputFolderFallsBackToDefault(string output_folder)
        {
            var settings = new AddressableLeakLogSettings(output_folder, false);

            Assert.AreEqual(AddressableLeakLogSettings.DEFAULT_OUTPUT_FOLDER, settings.AccOutputFolder);
        }

        // 指定した値は、そのまま使われる（前後の空白だけ取り除く）
        [Test]
        public void ConstructorKeepsSpecifiedValuesAndTrimsOutputFolder()
        {
            var settings = new AddressableLeakLogSettings("  Logs/MyLeaks  ", true);

            Assert.AreEqual("Logs/MyLeaks", settings.AccOutputFolder);
            Assert.IsTrue(settings.AccExportOnExitPlayMode);
        }

        // SO の既定値は、ログ出力の既定値になる
        [Test]
        public void EditorSettingsSoDefaultHasDefaultLeakLogSettings()
        {
            var so = ScriptableObject.CreateInstance<AddressableEditorSettingsSO>();
            try
            {
                Assert.IsNotNull(so.AccLeakLog);
                Assert.AreEqual(AddressableLeakLogSettings.DEFAULT_OUTPUT_FOLDER, so.AccLeakLog.AccOutputFolder);
                Assert.IsFalse(so.AccLeakLog.AccExportOnExitPlayMode);
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }

        // Inspector で出力先を空にすると、OnValidate で既定値に戻る
        [Test]
        public void EditorSettingsSoOnValidateRestoresBlankOutputFolder()
        {
            var so = ScriptableObject.CreateInstance<AddressableEditorSettingsSO>();
            try
            {
                var serialized = new SerializedObject(so);
                serialized.FindProperty("_leakLog._outputFolder").stringValue = string.Empty;
                serialized.ApplyModifiedProperties();

                Assert.AreEqual(AddressableLeakLogSettings.DEFAULT_OUTPUT_FOLDER, so.AccLeakLog.AccOutputFolder);
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }
    }
}
