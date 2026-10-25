using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary> AssetGroupTicket の単体テスト。Controller を通さず、中身を直接詰めて、取り出しと無効化を確認する。 </summary>
    internal sealed class AssetGroupTicketTests
    {
        private FakeAsset _assetA;
        private FakeAsset _assetB;
        private FakeOtherAsset _otherA;

        [SetUp]
        public void SetUp()
        {
            _assetA = ScriptableObject.CreateInstance<FakeAsset>();
            _assetB = ScriptableObject.CreateInstance<FakeAsset>();
            _otherA = ScriptableObject.CreateInstance<FakeOtherAsset>();
        }

        // 中身を詰めたまとめチケット（"a" の FakeAsset と FakeOtherAsset、"b" の FakeAsset の3件）
        private AssetGroupTicket CreateFilledGroup()
        {
            var group = new AssetGroupTicket("label", 3);
            var addresses = new[] { "a", "a", "b" };
            var asset_types = new[] { typeof(FakeAsset), typeof(FakeOtherAsset), typeof(FakeAsset) };
            var assets = new UnityEngine.Object[] { _assetA, _otherA, _assetB };
            group.Fill(addresses, asset_types, assets, new long[] { 1, 2, 3 });
            return group;
        }

        // 件数は生成時に決まり、中身を詰める前でも取得できる。中身を詰める前は、何も取り出せない
        [Test]
        public void BeforeFillCountIsKnownButNothingCanBeRetrieved()
        {
            var group = new AssetGroupTicket("label", 3);

            Assert.AreEqual("label", group.AccLabel);
            Assert.AreEqual(3, group.AccCount);
            Assert.IsFalse(group.AccIsReleased);
            Assert.IsFalse(group.TryGetAsset<FakeAsset>("a", out var asset));
            Assert.IsNull(asset);
            Assert.AreEqual(0, group.GetAssets<FakeAsset>().Count);
        }

        // 生成時の件数と、詰める通し番号の件数が違えば例外
        [Test]
        public void FillCountMismatchThrows()
        {
            var group = new AssetGroupTicket("label", 2);

            Assert.Throws<ArgumentException>(() => group.Fill(
                new[] { "a" }, new[] { typeof(FakeAsset) }, new UnityEngine.Object[] { _assetA }, new long[] { 1 }));
        }

        // 通し番号の件数が合っていても、アドレス・型・アセットの配列の長さが違えば例外
        [Test]
        public void FillArrayLengthMismatchThrows()
        {
            var group = new AssetGroupTicket("label", 2);

            Assert.Throws<ArgumentException>(() => group.Fill(
                new[] { "a" }, new[] { typeof(FakeAsset), typeof(FakeAsset) }, new UnityEngine.Object[] { _assetA, _assetB }, new long[] { 1, 2 }));
            Assert.Throws<ArgumentException>(() => group.Fill(
                new[] { "a", "b" }, new[] { typeof(FakeAsset) }, new UnityEngine.Object[] { _assetA, _assetB }, new long[] { 1, 2 }));
            Assert.Throws<ArgumentException>(() => group.Fill(
                new[] { "a", "b" }, new[] { typeof(FakeAsset), typeof(FakeAsset) }, new UnityEngine.Object[] { _assetA }, new long[] { 1, 2 }));
        }

        // 読み込んだ型が T と完全一致するものだけを返す
        [Test]
        public void GetAssetsReturnsOnlyExactTypeMatches()
        {
            var group = CreateFilledGroup();

            CollectionAssert.AreEquivalent(new[] { _assetA, _assetB }, group.GetAssets<FakeAsset>());
            CollectionAssert.AreEquivalent(new[] { _otherA }, group.GetAssets<FakeOtherAsset>());
        }

        // 基底型では探さない（Object を指定しても、何も返らない）
        [Test]
        public void GetAssetsBaseTypeReturnsEmpty()
        {
            var group = CreateFilledGroup();

            Assert.AreEqual(0, group.GetAssets<UnityEngine.Object>().Count);
            Assert.AreEqual(0, group.GetAssets<ScriptableObject>().Count);
        }

        // 2回目以降は同じインスタンスを返し、List<T> にキャストして書き換えることはできない
        [Test]
        public void GetAssetsSecondCallReturnsSameInstanceAndIsReadOnly()
        {
            var group = CreateFilledGroup();

            var first = group.GetAssets<FakeAsset>();
            var second = group.GetAssets<FakeAsset>();

            Assert.AreSame(first, second, "キャッシュされた同じインスタンスのはず");
            Assert.IsNull(first as List<FakeAsset>, "List<T> にキャストして書き換えられないはず");
        }

        // 該当が0件なら、空を返す
        [Test]
        public void GetAssetsNoMatchReturnsEmpty()
        {
            var group = CreateFilledGroup();

            Assert.AreEqual(0, group.GetAssets<Texture2D>().Count);
        }

        // 同じアドレスでも、型が一致するものだけが取り出せる
        [Test]
        public void TryGetAssetReturnsAssetOnlyForExactTypeAtAddress()
        {
            var group = CreateFilledGroup();

            Assert.IsTrue(group.TryGetAsset<FakeAsset>("a", out var asset_a));
            Assert.AreSame(_assetA, asset_a);
            Assert.IsTrue(group.TryGetAsset<FakeOtherAsset>("a", out var other_a));
            Assert.AreSame(_otherA, other_a);

            Assert.IsFalse(group.TryGetAsset<FakeOtherAsset>("b", out var missing), "b は FakeAsset だけなので取り出せないはず");
            Assert.IsNull(missing);
            Assert.IsFalse(group.TryGetAsset<FakeAsset>("none", out _));
            Assert.IsFalse(group.TryGetAsset<UnityEngine.Object>("a", out _), "基底型では取り出せないはず");
        }

        // アドレスが null なら false
        [Test]
        public void TryGetAssetNullAddressReturnsFalse()
        {
            var group = CreateFilledGroup();

            Assert.IsFalse(group.TryGetAsset<FakeAsset>(null, out var asset));
            Assert.IsNull(asset);
        }

        // 無効化すると、解放済みになり、何も取り出せなくなる（キャッシュ済みの結果も返さない）
        [Test]
        public void InvalidateMakesGroupReleasedAndEmpty()
        {
            var group = CreateFilledGroup();
            Assert.AreEqual(2, group.GetAssets<FakeAsset>().Count); // キャッシュを作っておく

            group.Invalidate();

            Assert.IsTrue(group.AccIsReleased);
            Assert.AreEqual(3, group.AccCount, "件数は変わらないはず");
            Assert.AreEqual(0, group.GetAssets<FakeAsset>().Count);
            Assert.IsFalse(group.TryGetAsset<FakeAsset>("a", out _));
            Assert.AreEqual(1, group.GetSerial(0), "通し番号は、解放のために残るはず");
        }
    }
}
