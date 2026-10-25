using System;
using System.Collections.Generic;
using NUnit.Framework;
using MAH.AddressableManager.Core.Loading;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class OwnerReclaimRegistryModelTests
    {
        private OwnerReclaimRegistryModel _model;
        private List<OwnerReclaimSnapshot> _reclaims;

        [SetUp]
        public void SetUp()
        {
            _model = new OwnerReclaimRegistryModel();
            _reclaims = new List<OwnerReclaimSnapshot>();
        }

        // 同じ経路・同じ名前の回収は1行にまとまり、回数とチケット累計が加算される
        [Test]
        public void RecordSameRouteAndNameAggregatesIntoOneRow()
        {
            _model.Record(OwnerReclaimRoute.OnDestroy, 1, "Enemy", 2);
            _model.Record(OwnerReclaimRoute.OnDestroy, 2, "Enemy", 3);
            _model.Record(OwnerReclaimRoute.OnDestroy, 3, "Enemy", 1);

            _model.CopyReclaimsTo(_reclaims);

            Assert.AreEqual(1, _reclaims.Count);
            Assert.AreEqual(OwnerReclaimRoute.OnDestroy, _reclaims[0].AccRoute);
            Assert.AreEqual("Enemy", _reclaims[0].AccOwnerName);
            Assert.AreEqual(3, _reclaims[0].AccCount);
            Assert.AreEqual(6, _reclaims[0].AccTicketCount, "回収したチケット数は累計になるはず");
        }

        // 経路が違えば別の行になる（OnDestroy とポーリングを区別する）
        [Test]
        public void RecordDifferentRouteMakesSeparateRows()
        {
            _model.Record(OwnerReclaimRoute.OnDestroy, 1, "Enemy", 1);
            _model.Record(OwnerReclaimRoute.Polling, 2, "Enemy", 1);

            _model.CopyReclaimsTo(_reclaims);

            Assert.AreEqual(2, _reclaims.Count);
            Assert.AreEqual(1, _reclaims.FindAll(r => r.AccRoute == OwnerReclaimRoute.OnDestroy).Count);
            Assert.AreEqual(1, _reclaims.FindAll(r => r.AccRoute == OwnerReclaimRoute.Polling).Count);
        }

        // 名前が違えば別の行になる
        [Test]
        public void RecordDifferentNameMakesSeparateRows()
        {
            _model.Record(OwnerReclaimRoute.Polling, 1, "Enemy", 1);
            _model.Record(OwnerReclaimRoute.Polling, 2, "Boss", 1);

            _model.CopyReclaimsTo(_reclaims);

            Assert.AreEqual(2, _reclaims.Count);
        }

        // InstanceID は集約の判定に含めず、最後に回収された ID だけを持つ
        [Test]
        public void RecordDifferentInstanceIdSameNameKeepsLastInstanceId()
        {
            _model.Record(OwnerReclaimRoute.OnDestroy, 100, "Enemy", 1);
            _model.Record(OwnerReclaimRoute.OnDestroy, 200, "Enemy", 1);

            _model.CopyReclaimsTo(_reclaims);

            Assert.AreEqual(1, _reclaims.Count);
            Assert.AreEqual(200, _reclaims[0].AccLastInstanceId);
        }

        // 通し番号は単調に増え、行 ID は最初の発生で採番されて変わらない。最終時刻が記録される
        [Test]
        public void RecordAssignsStableIdAndIncreasingSequence()
        {
            _model.Record(OwnerReclaimRoute.OnDestroy, 1, "A", 1);
            _model.Record(OwnerReclaimRoute.Polling, 2, "B", 1);
            _model.CopyReclaimsTo(_reclaims);
            var a = _reclaims.Find(r => r.AccOwnerName == "A");
            var b = _reclaims.Find(r => r.AccOwnerName == "B");
            Assert.AreNotEqual(a.AccId, b.AccId);
            Assert.Greater(b.AccSequence, a.AccSequence);

            _model.Record(OwnerReclaimRoute.OnDestroy, 3, "A", 1); // A が最後に発生した
            _model.CopyReclaimsTo(_reclaims);
            var a_after = _reclaims.Find(r => r.AccOwnerName == "A");
            var b_after = _reclaims.Find(r => r.AccOwnerName == "B");
            Assert.AreEqual(a.AccId, a_after.AccId, "行 ID は変わらないはず");
            Assert.Greater(a_after.AccSequence, b_after.AccSequence, "最後に発生した行の通し番号が最大になるはず");
            Assert.AreNotEqual(default(DateTime), a_after.AccLastTime);
        }

        // 名前が取れなかった（null）ときは空文字として扱い、空文字と同じ行にまとまる
        [Test]
        public void RecordNullNameIsTreatedAsEmpty()
        {
            _model.Record(OwnerReclaimRoute.Polling, 1, null, 1);
            _model.Record(OwnerReclaimRoute.Polling, 2, string.Empty, 1);

            _model.CopyReclaimsTo(_reclaims);

            Assert.AreEqual(1, _reclaims.Count);
            Assert.AreEqual(string.Empty, _reclaims[0].AccOwnerName);
            Assert.AreEqual(2, _reclaims[0].AccCount);
        }

        // Clear で空になる
        [Test]
        public void ClearRemovesAllRows()
        {
            _model.Record(OwnerReclaimRoute.OnDestroy, 1, "Enemy", 1);

            _model.Clear();
            _model.CopyReclaimsTo(_reclaims);

            Assert.AreEqual(0, _reclaims.Count);
        }

        // ゴミの入った List は Clear されてから書き出される
        [Test]
        public void CopyReclaimsToClearsExistingItems()
        {
            _model.Record(OwnerReclaimRoute.OnDestroy, 1, "Enemy", 1);
            _reclaims.Add(default);
            _reclaims.Add(default);

            _model.CopyReclaimsTo(_reclaims);

            Assert.AreEqual(1, _reclaims.Count);
        }

        // 空の Model でも例外にならず、空の List になる
        [Test]
        public void CopyReclaimsToEmptyModelReturnsEmptyList()
        {
            _reclaims.Add(default);

            _model.CopyReclaimsTo(_reclaims);

            Assert.AreEqual(0, _reclaims.Count);
        }

        // 取得後に記録が増えても、取得済みの値は変わらない
        [Test]
        public void CopiedSnapshotDoesNotChangeAfterLaterRecords()
        {
            _model.Record(OwnerReclaimRoute.OnDestroy, 1, "Enemy", 2);
            _model.CopyReclaimsTo(_reclaims);
            var snapshot = _reclaims[0];

            _model.Record(OwnerReclaimRoute.OnDestroy, 2, "Enemy", 5);

            Assert.AreEqual(1, snapshot.AccCount);
            Assert.AreEqual(2, snapshot.AccTicketCount);
            Assert.AreEqual(1, snapshot.AccLastInstanceId);
        }
    }
}
