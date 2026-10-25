using System;
using System.Collections.Generic;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core.Loading;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class LeakRegistryModelTests
    {
        private const string TRACE_A = "at Test.A () in /path/A.cs:10";
        private const string TRACE_B = "at Test.B () in /path/B.cs:20";

        private LeakRegistryModel _model;
        private List<LeakSnapshot> _leaks;

        [SetUp]
        public void SetUp()
        {
            _model = new LeakRegistryModel();
            _leaks = new List<LeakSnapshot>();
        }

        private static AssetKey Key(string address) => AssetKey.FromPath(address);

        private static LifetimeScopeId Owner(int instance_id) => new(LifetimeKind.Owner, instance_id);

        private static LifetimeScopeId Scene(int handle) => new(LifetimeKind.Scene, handle);

        // 同じ確保元のリークは1行にまとまり、回数が加算される
        [Test]
        public void RecordTicketLeakSameSourceAggregatesIntoOneRow()
        {
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), "Owner1", TRACE_A);
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), "Owner1", TRACE_A);
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), "Owner1", TRACE_A);

            _model.CopyLeaksTo(_leaks);

            Assert.AreEqual(1, _leaks.Count);
            Assert.AreEqual(3, _leaks[0].AccCount);
            Assert.AreEqual(Key("a"), _leaks[0].AccKey);
            Assert.AreEqual(typeof(FakeAsset), _leaks[0].AccAssetType);
            Assert.AreEqual(TRACE_A, _leaks[0].AccStackTrace);
            Assert.IsFalse(_leaks[0].AccIsGroup);
        }

        // スタックトレースが違えば別の行になる
        [Test]
        public void RecordTicketLeakDifferentStackTraceMakesSeparateRows()
        {
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), "Owner1", TRACE_A);
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), "Owner1", TRACE_B);

            _model.CopyLeaksTo(_leaks);

            Assert.AreEqual(2, _leaks.Count);
        }

        // キーが違う・型が違えば別の行になる
        [Test]
        public void RecordTicketLeakDifferentKeyOrTypeMakesSeparateRows()
        {
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), "Owner1", TRACE_A);
            _model.RecordTicketLeak(Key("b"), typeof(FakeAsset), Owner(1), "Owner1", TRACE_A);
            _model.RecordTicketLeak(Key("a"), typeof(FakeOtherAsset), Owner(1), "Owner1", TRACE_A);

            _model.CopyLeaksTo(_leaks);

            Assert.AreEqual(3, _leaks.Count);
        }

        // 寿命の種類が違えば別の行になる。同じ種類で、スコープの値だけが違うときは同じ行にまとまる
        [Test]
        public void RecordTicketLeakScopeKindSeparatesRowsButScopeValueDoesNot()
        {
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), "Owner1", TRACE_A);
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(2), "Owner2", TRACE_A); // 別の Owner、同じ呼び出し元
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Scene(7), "SceneX", TRACE_A);

            _model.CopyLeaksTo(_leaks);

            Assert.AreEqual(2, _leaks.Count, "Owner の行と Scene の行の2行になるはず");
            var owner_row = _leaks.Find(l => l.AccScopeKind == LifetimeKind.Owner);
            var scene_row = _leaks.Find(l => l.AccScopeKind == LifetimeKind.Scene);
            Assert.AreEqual(2, owner_row.AccCount);
            Assert.AreEqual(1, scene_row.AccCount);
        }

        // 最後の発生の情報（スコープ・名前・通し番号・時刻）で上書きされる
        [Test]
        public void RecordTicketLeakLastOccurrenceUpdatesScopeSequenceAndTime()
        {
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), "First", TRACE_A);
            _model.CopyLeaksTo(_leaks);
            var first = _leaks[0];

            var before = DateTime.Now;
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(2), "Second", TRACE_A);
            var after = DateTime.Now;
            _model.CopyLeaksTo(_leaks);
            var second = _leaks[0];

            Assert.AreEqual(first.AccId, second.AccId, "同じ行の ID は変わらないはず");
            Assert.Greater(second.AccSequence, first.AccSequence);
            Assert.AreEqual(2, second.AccScopeValue);
            Assert.AreEqual("Second", second.AccScopeName);
            Assert.GreaterOrEqual(second.AccLastTime, before);
            Assert.LessOrEqual(second.AccLastTime, after);
        }

        // 通し番号は行をまたいで単調に増え、行の ID は行ごとに一意
        [Test]
        public void RecordTicketLeakSequenceIsGlobalAndIdIsUniquePerRow()
        {
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), "O", TRACE_A);
            _model.RecordTicketLeak(Key("b"), typeof(FakeAsset), Owner(1), "O", TRACE_A);

            _model.CopyLeaksTo(_leaks);

            var row_a = _leaks.Find(l => l.AccKey == Key("a"));
            var row_b = _leaks.Find(l => l.AccKey == Key("b"));
            Assert.Greater(row_b.AccSequence, row_a.AccSequence, "後から記録した行のほうが通し番号が大きいはず");
            Assert.AreNotEqual(row_a.AccId, row_b.AccId);
        }

        // スコープ名が null のときは空文字として持つ
        [Test]
        public void RecordTicketLeakNullScopeNameBecomesEmpty()
        {
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), null, TRACE_A);

            _model.CopyLeaksTo(_leaks);

            Assert.AreEqual(string.Empty, _leaks[0].AccScopeName);
        }

        // Label 一括は、Label・件数を持つ行になり、単体の行とは別になる
        [Test]
        public void RecordGroupLeakKeepsLabelAndCountAndIsSeparateFromTicketRows()
        {
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), "O", TRACE_A);
            _model.RecordGroupLeak("ui", 8, Owner(1), "O", TRACE_A);

            _model.CopyLeaksTo(_leaks);

            Assert.AreEqual(2, _leaks.Count);
            var group = _leaks.Find(l => l.AccIsGroup);
            Assert.AreEqual("ui", group.AccLabel);
            Assert.AreEqual(8, group.AccGroupCount);
            Assert.IsNull(group.AccAssetType);
            Assert.AreEqual(1, group.AccCount);
        }

        // Label 一括も、同じ Label・同じトレースなら1行にまとまり、件数は最後の発生の値になる。Label が違えば別の行
        [Test]
        public void RecordGroupLeakSameLabelAggregatesAndLatestGroupCountWins()
        {
            _model.RecordGroupLeak("ui", 8, Owner(1), "O", TRACE_A);
            _model.RecordGroupLeak("ui", 10, Owner(1), "O", TRACE_A);
            _model.RecordGroupLeak("enemy", 3, Owner(1), "O", TRACE_A);

            _model.CopyLeaksTo(_leaks);

            Assert.AreEqual(2, _leaks.Count);
            var ui = _leaks.Find(l => l.AccLabel == "ui");
            Assert.AreEqual(2, ui.AccCount);
            Assert.AreEqual(10, ui.AccGroupCount);
        }

        // 渡された List の中身は、Clear されてから書き出される
        [Test]
        public void CopyLeaksToClearsExistingContents()
        {
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), "O", TRACE_A);
            _leaks.Add(default);
            _leaks.Add(default);
            _leaks.Add(default);

            _model.CopyLeaksTo(_leaks);

            Assert.AreEqual(1, _leaks.Count);
            Assert.AreEqual(Key("a"), _leaks[0].AccKey);
        }

        // 取得後に記録が増えても、取得済みの値は変わらない
        [Test]
        public void CopyLeaksToSnapshotIsUnaffectedByLaterRecords()
        {
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), "O", TRACE_A);
            _model.CopyLeaksTo(_leaks);
            var snapshot = _leaks[0];

            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), "O", TRACE_A);

            Assert.AreEqual(1, snapshot.AccCount, "取得済みの Snapshot の回数は変わらないはず");
        }

        // 空のときは、空の List になる
        [Test]
        public void CopyLeaksToEmptyModelWritesNothing()
        {
            _leaks.Add(default);

            _model.CopyLeaksTo(_leaks);

            Assert.AreEqual(0, _leaks.Count);
        }

        // Clear で、溜めた行が空になる
        [Test]
        public void ClearRemovesAllRows()
        {
            _model.RecordTicketLeak(Key("a"), typeof(FakeAsset), Owner(1), "O", TRACE_A);

            _model.Clear();
            _model.CopyLeaksTo(_leaks);

            Assert.AreEqual(0, _leaks.Count);
        }
    }
}
