using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.TestTools;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// LabelLocationService.Collect の単体テスト。実際の Addressables は呼ばず、ロケーションを直接渡して、
    /// 型の除外と重複の除去を確認する（ロケーションの取得そのものは Addressables の責務）。
    /// </summary>
    internal sealed class LabelLocationServiceTests
    {
        private static IResourceLocation CreateLocation(string primary_key, Type resource_type)
            => new ResourceLocationBase(primary_key, primary_key, "test-provider", resource_type);

        // 画像（Sprite 設定のテクスチャ）は、同じ PrimaryKey で Texture2D と Sprite の2件として残る
        [Test]
        public void CollectSameKeyWithDifferentTypesKeepsBoth()
        {
            var locations = new List<IResourceLocation>
            {
                CreateLocation("icon", typeof(Texture2D)),
                CreateLocation("icon", typeof(Sprite)),
            };
            var result = new List<LabelLocation>();

            LabelLocationService.Collect("label", locations, result);

            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Exists(x => x.AccPrimaryKey == "icon" && x.AccResourceType == typeof(Texture2D)));
            Assert.IsTrue(result.Exists(x => x.AccPrimaryKey == "icon" && x.AccResourceType == typeof(Sprite)));
        }

        // UnityEngine.Object ではない型（Scene エントリの SceneInstance など）は、警告を出して除外される
        [Test]
        public void CollectNonUnityObjectTypeIsExcludedWithWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("UnityEngine.Object ではない型"));
            var locations = new List<IResourceLocation>
            {
                CreateLocation("clip", typeof(AudioClip)),
                CreateLocation("scene", typeof(string)), // UnityEngine.Object ではない型の代わり
            };
            var result = new List<LabelLocation>();

            LabelLocationService.Collect("label", locations, result);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("clip", result[0].AccPrimaryKey);
        }

        // 型が null のロケーションも、警告を出して除外される
        [Test]
        public void CollectNullTypeIsExcludedWithWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("UnityEngine.Object ではない型"));
            var locations = new List<IResourceLocation> { CreateLocation("unknown", null) };
            var result = new List<LabelLocation>();

            LabelLocationService.Collect("label", locations, result);

            Assert.AreEqual(0, result.Count);
        }

        // 同じ (PrimaryKey, 型) が複数あるとき（アドレスの重複）は、警告を出して1件にまとめる
        [Test]
        public void CollectDuplicateKeyAndTypeIsDeduplicatedWithWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("アドレスが重複しています"));
            var locations = new List<IResourceLocation>
            {
                CreateLocation("dup", typeof(Sprite)),
                CreateLocation("dup", typeof(Sprite)),
                CreateLocation("other", typeof(Sprite)),
            };
            var result = new List<LabelLocation>();

            LabelLocationService.Collect("label", locations, result);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result.FindAll(x => x.AccPrimaryKey == "dup").Count);
        }

        // 該当が0件、または null のときは何も詰めない（例外にもならない）
        [Test]
        public void CollectEmptyOrNullDoesNothing()
        {
            var result = new List<LabelLocation>();

            Assert.DoesNotThrow(() => LabelLocationService.Collect("label", new List<IResourceLocation>(), result));
            Assert.DoesNotThrow(() => LabelLocationService.Collect("label", null, result));
            Assert.AreEqual(0, result.Count);
        }

        // 既に詰めてある要素は消さず、末尾に追加する
        [Test]
        public void CollectAppendsToExistingResult()
        {
            var result = new List<LabelLocation> { new LabelLocation("existing", typeof(Sprite)) };
            var locations = new List<IResourceLocation> { CreateLocation("added", typeof(Sprite)) };

            LabelLocationService.Collect("label", locations, result);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("existing", result[0].AccPrimaryKey);
            Assert.AreEqual("added", result[1].AccPrimaryKey);
        }
    }
}
