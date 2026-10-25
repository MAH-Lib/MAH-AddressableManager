using NUnit.Framework;
using MAH.AddressableManager.Editor.SettingsSync;
using UnityEditor;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class AddressableDependencyCheckSettingsTests
    {
        // 既定の出力先は Logs/AddressableManager
        [Test]
        public void DefaultValueIsDefaultFolder()
        {
            var settings = new AddressableDependencyCheckSettings();

            Assert.AreEqual("Logs/AddressableManager", settings.AccOutputFolder);
            Assert.AreEqual(AddressableDependencyCheckSettings.DEFAULT_OUTPUT_FOLDER, settings.AccOutputFolder);
        }

        // 空・空白・null は既定値に戻る
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void ConstructorBlankFallsBackToDefault(string output_folder)
        {
            var settings = new AddressableDependencyCheckSettings(output_folder);

            Assert.AreEqual(AddressableDependencyCheckSettings.DEFAULT_OUTPUT_FOLDER, settings.AccOutputFolder);
        }

        // 指定した値は、前後の空白だけ取り除いて使われる
        [Test]
        public void ConstructorTrimsOutputFolder()
        {
            Assert.AreEqual("Logs/My", new AddressableDependencyCheckSettings("  Logs/My  ").AccOutputFolder);
        }

        // SO の既定値は、Dependency Check の既定値になる
        [Test]
        public void EditorSettingsSoDefaultHasDefaultDependencyCheckSettings()
        {
            var so = ScriptableObject.CreateInstance<AddressableEditorSettingsSO>();
            try
            {
                Assert.IsNotNull(so.AccDependencyCheck);
                Assert.AreEqual(AddressableDependencyCheckSettings.DEFAULT_OUTPUT_FOLDER, so.AccDependencyCheck.AccOutputFolder);
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
                serialized.FindProperty("_dependencyCheck._outputFolder").stringValue = string.Empty;
                serialized.ApplyModifiedProperties();

                Assert.AreEqual(AddressableDependencyCheckSettings.DEFAULT_OUTPUT_FOLDER, so.AccDependencyCheck.AccOutputFolder);
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }
    }
}
