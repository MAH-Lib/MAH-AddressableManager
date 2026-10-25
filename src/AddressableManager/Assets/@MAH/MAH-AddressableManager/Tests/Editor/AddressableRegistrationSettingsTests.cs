using System.Collections.Generic;
using NUnit.Framework;
using MAH.AddressableManager.Editor.Registration;
using MAH.AddressableManager.Editor.SettingsSync;
using UnityEditor;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class AddressableRegistrationSettingsTests
    {
        // 既定値は、自動登録オン・まとめだけ・アドレス {Asset.Path}・Label 空・除外 2 件
        [Test]
        public void DefaultValues()
        {
            var settings = new AddressableRegistrationSettings();

            Assert.IsTrue(settings.AccAutoRegistration);
            Assert.AreEqual(RegistrationLogDetail.Summary, settings.AccLogDetail);
            Assert.AreEqual("{Asset.Path}", settings.AccDefaultAddressTemplate);
            Assert.AreEqual(0, settings.AccDefaultLabels.Count);
            CollectionAssert.AreEqual(new[] { "**/_Test/**", "**/Editor/**" }, settings.AccDefaultExcludePatterns);
        }

        // アドレスのテンプレートが空・空白・null のときは既定値に戻る
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void ConstructorBlankAddressTemplateFallsBackToDefault(string address_template)
        {
            var settings = new AddressableRegistrationSettings(
                true, RegistrationLogDetail.Summary, address_template, new List<string>(), new List<string>());

            Assert.AreEqual(AddressableRegistrationSettings.DEFAULT_ADDRESS_TEMPLATE, settings.AccDefaultAddressTemplate);
        }

        // 指定したアドレスのテンプレートは、前後の空白だけ取り除かれる
        [Test]
        public void ConstructorTrimsAddressTemplate()
        {
            var settings = new AddressableRegistrationSettings(
                true, RegistrationLogDetail.Summary, "  {Path.Relative}  ", new List<string>(), new List<string>());

            Assert.AreEqual("{Path.Relative}", settings.AccDefaultAddressTemplate);
        }

        // Label・除外パターンは、空白だけの項目が取り除かれ、前後の空白が削られる。null のリストは空になる
        [Test]
        public void ConstructorCleansLists()
        {
            var settings = new AddressableRegistrationSettings(
                false, RegistrationLogDetail.PerAsset, "{Asset.Name}",
                new List<string> { " UI ", "", "  ", "Common" },
                null);

            Assert.IsFalse(settings.AccAutoRegistration);
            Assert.AreEqual(RegistrationLogDetail.PerAsset, settings.AccLogDetail);
            CollectionAssert.AreEqual(new[] { "UI", "Common" }, settings.AccDefaultLabels);
            Assert.AreEqual(0, settings.AccDefaultExcludePatterns.Count);
        }

        // 手動一括登録の既定の追加先グループは、既定では空（DefaultGroup）。指定した値は前後の空白が取り除かれ、null は空になる
        [Test]
        public void DefaultTargetGroupIsEmptyAndTrimmed()
        {
            Assert.AreEqual(string.Empty, new AddressableRegistrationSettings().AccDefaultTargetGroup);

            var specified = new AddressableRegistrationSettings(
                true, RegistrationLogDetail.Summary, "{Asset.Path}", new List<string>(), new List<string>(), "  Manual  ");
            Assert.AreEqual("Manual", specified.AccDefaultTargetGroup);

            var null_group = new AddressableRegistrationSettings(
                true, RegistrationLogDetail.Summary, "{Asset.Path}", new List<string>(), new List<string>(), null);
            Assert.AreEqual(string.Empty, null_group.AccDefaultTargetGroup);
        }

        // SO の既定値は、登録の設定の既定値になる
        [Test]
        public void EditorSettingsSoDefaultHasDefaultRegistrationSettings()
        {
            var so = ScriptableObject.CreateInstance<AddressableEditorSettingsSO>();
            try
            {
                Assert.IsNotNull(so.AccRegistration);
                Assert.IsTrue(so.AccRegistration.AccAutoRegistration);
                Assert.AreEqual(AddressableRegistrationSettings.DEFAULT_ADDRESS_TEMPLATE, so.AccRegistration.AccDefaultAddressTemplate);
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }

        // Inspector でアドレスのテンプレートを空にすると、OnValidate で既定値に戻る
        [Test]
        public void EditorSettingsSoOnValidateRestoresBlankAddressTemplate()
        {
            var so = ScriptableObject.CreateInstance<AddressableEditorSettingsSO>();
            try
            {
                var serialized = new SerializedObject(so);
                serialized.FindProperty("_registration._defaultAddressTemplate").stringValue = string.Empty;
                serialized.ApplyModifiedProperties();

                Assert.AreEqual(AddressableRegistrationSettings.DEFAULT_ADDRESS_TEMPLATE, so.AccRegistration.AccDefaultAddressTemplate);
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }
    }
}
