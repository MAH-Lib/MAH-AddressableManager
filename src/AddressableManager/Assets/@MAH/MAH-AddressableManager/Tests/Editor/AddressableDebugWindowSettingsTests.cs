using NUnit.Framework;
using MAH.AddressableManager.Editor.SettingsSync;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class AddressableDebugWindowSettingsTests
    {
        // 既定の更新間隔は 0.5 秒
        [Test]
        public void DefaultRefreshIntervalIsHalfSecond()
        {
            var settings = new AddressableDebugWindowSettings();

            Assert.AreEqual(0.5f, settings.AccRefreshIntervalSec);
        }

        // 下限（0.1 秒）未満は、下限に補正される
        [TestCase(0.05f)]
        [TestCase(0f)]
        [TestCase(-1f)]
        public void ConstructorBelowMinimumClampsToMinimum(float refresh_interval_sec)
        {
            var settings = new AddressableDebugWindowSettings(refresh_interval_sec);

            Assert.AreEqual(AddressableDebugWindowSettings.MIN_REFRESH_INTERVAL_SEC, settings.AccRefreshIntervalSec);
        }

        // 下限以上の値は、そのまま使われる
        [TestCase(0.1f)]
        [TestCase(2f)]
        public void ConstructorAtOrAboveMinimumKeepsValue(float refresh_interval_sec)
        {
            var settings = new AddressableDebugWindowSettings(refresh_interval_sec);

            Assert.AreEqual(refresh_interval_sec, settings.AccRefreshIntervalSec);
        }

        // SO の既定値は、Window 設定の既定値になる（OnValidate で壊れた値が入らない）
        [Test]
        public void EditorSettingsSoDefaultHasDefaultDebugWindowSettings()
        {
            var so = ScriptableObject.CreateInstance<AddressableEditorSettingsSO>();
            try
            {
                Assert.IsNotNull(so.AccDebugWindow);
                Assert.AreEqual(0.5f, so.AccDebugWindow.AccRefreshIntervalSec);
            }
            finally
            {
                Object.DestroyImmediate(so);
            }
        }
    }
}
