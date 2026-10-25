using System;
using System.Collections.Generic;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Core.Loading;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MAH.AddressableManager.Tests.Editor
{
    internal sealed class AddressableDebugReaderTests
    {
        private AssetRegistryModel _assetRegistry;
        private IssuedTicketRegistryModel _ticketRegistry;
        private LeakRegistryModel _leakRegistry;
        private OwnerWatchRegistryModel _ownerWatch;
        private OwnerReclaimRegistryModel _ownerReclaims;
        private readonly List<GameObject> _gameObjects = new();
        private AddressableDebugReader _reader;
        private List<AssetEntrySnapshot> _entries;
        private List<ScopeCountSnapshot> _scopeCounts;
        private List<IssuedTicketSnapshot> _tickets;

        [SetUp]
        public void SetUp()
        {
            _assetRegistry = new AssetRegistryModel();
            _ticketRegistry = new IssuedTicketRegistryModel();
            _leakRegistry = new LeakRegistryModel();
            _ownerWatch = new OwnerWatchRegistryModel();
            _ownerReclaims = new OwnerReclaimRegistryModel();
            _reader = new AddressableDebugReader(_assetRegistry, _ticketRegistry, _leakRegistry, _ownerWatch, _ownerReclaims);
            _entries = new List<AssetEntrySnapshot>();
            _scopeCounts = new List<ScopeCountSnapshot>();
            _tickets = new List<IssuedTicketSnapshot>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _gameObjects)
            {
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            }
            _gameObjects.Clear();
        }

        private GameObject CreateGameObject(string name)
        {
            var go = new GameObject(name);
            _gameObjects.Add(go);
            return go;
        }

        // Registry に Entry を足し、指定したスコープの確保数を加算する（ロード完了は呼び出し側が制御する）
        private AssetEntryModel AddEntry(string path, Type asset_type, out FakeAssetLoaderWorker worker)
        {
            worker = new FakeAssetLoaderWorker(asset_type);
            return _assetRegistry.AddEntry(new AssetEntryKey(AssetKey.FromPath(path), asset_type), worker);
        }

        private void RegisterTicket(AssetEntryModel entry, LifetimeScopeId scope_id, AssetGroupTicket group = null)
        {
            entry.AddCount(scope_id);
            _ticketRegistry.Register(_ticketRegistry.IssueSerial(), entry.AccEntryKey, entry, scope_id, group);
        }

        private AssetEntrySnapshot FindEntry(string path)
        {
            var key = AssetKey.FromPath(path);
            foreach (var entry in _entries)
            {
                if (entry.AccKey == key) return entry;
            }
            Assert.Fail($"{path} の Snapshot が見つかりません。");
            return default;
        }

        // Entry の内訳（連結された List の中の、開始位置から件数分）から、指定スコープの確保数を引く。無ければ -1
        private int FindScopeCount(AssetEntrySnapshot entry, LifetimeKind kind, int value)
        {
            for (var i = entry.AccScopeCountStart; i < entry.AccScopeCountStart + entry.AccScopeCountLength; i++)
            {
                var scope = _scopeCounts[i];
                if (scope.AccScopeKind == kind && scope.AccScopeValue == value) return scope.AccCount;
            }
            return -1;
        }

        // Key・型・RefCount・ロード状態が Snapshot にコピーされる
        [Test]
        public void CopyAssetEntriesToCopiesKeyTypeRefCountAndStatus()
        {
            var sprite_entry = AddEntry("test/sprite", typeof(Sprite), out var sprite_worker);
            sprite_entry.AddCount(LifetimeScopeId.Manual);
            sprite_entry.AddCount(LifetimeScopeId.Manual);
            sprite_worker.Complete(null);
            var texture_entry = AddEntry("test/texture", typeof(Texture2D), out _); // 未完了のまま
            texture_entry.AddCount(LifetimeScopeId.Manual);

            _reader.CopyAssetEntriesTo(_entries, _scopeCounts);

            Assert.AreEqual(2, _entries.Count);
            var sprite = FindEntry("test/sprite");
            Assert.AreEqual(typeof(Sprite), sprite.AccAssetType);
            Assert.AreEqual(2, sprite.AccRefCount);
            Assert.AreEqual(AsyncOperationStatus.Succeeded, sprite.AccStatus);
            var texture = FindEntry("test/texture");
            Assert.AreEqual(typeof(Texture2D), texture.AccAssetType);
            Assert.AreEqual(1, texture.AccRefCount);
            Assert.AreEqual(AsyncOperationStatus.None, texture.AccStatus);
        }

        // ロード完了後は、読み込んだアセットの名前が Snapshot にコピーされる。ロード完了前は空文字
        [Test]
        public void CopyAssetEntriesToCopiesAssetNameOnlyAfterLoaded()
        {
            var asset = ScriptableObject.CreateInstance<FakeAsset>();
            asset.name = "icon_sword";
            try
            {
                AddEntry("test/loaded", typeof(FakeAsset), out var loaded_worker);
                loaded_worker.Complete(asset);
                AddEntry("test/loading", typeof(FakeAsset), out _); // 未完了のまま

                _reader.CopyAssetEntriesTo(_entries, _scopeCounts);

                Assert.AreEqual("icon_sword", FindEntry("test/loaded").AccAssetName);
                Assert.AreEqual(string.Empty, FindEntry("test/loading").AccAssetName);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        // アセットが破棄済み（偽 null）でも例外にならず、名前は空文字になる
        [Test]
        public void CopyAssetEntriesToDestroyedAssetHasEmptyName()
        {
            var asset = ScriptableObject.CreateInstance<FakeAsset>();
            asset.name = "destroyed";
            AddEntry("test/key", typeof(FakeAsset), out var worker);
            worker.Complete(asset);
            UnityEngine.Object.DestroyImmediate(asset);

            _reader.CopyAssetEntriesTo(_entries, _scopeCounts);

            Assert.AreEqual(string.Empty, FindEntry("test/key").AccAssetName);
        }

        // 同じキーでも型が違う Entry は、別々の Snapshot になる
        [Test]
        public void CopyAssetEntriesToSameKeyDifferentTypeAreSeparateSnapshots()
        {
            AddEntry("test/key", typeof(Sprite), out _);
            AddEntry("test/key", typeof(Texture2D), out _);

            _reader.CopyAssetEntriesTo(_entries, _scopeCounts);

            Assert.AreEqual(2, _entries.Count);
            Assert.AreNotEqual(_entries[0].AccAssetType, _entries[1].AccAssetType);
        }

        // 複数 Entry・複数スコープの内訳が連結され、各 Entry の開始位置・件数で、自分の分だけを引ける
        [Test]
        public void CopyAssetEntriesToScopeCountsMatchStartAndLengthPerEntry()
        {
            var owner_scope = new LifetimeScopeId(LifetimeKind.Owner, 12345);
            var scene_scope = new LifetimeScopeId(LifetimeKind.Scene, 7);
            var first = AddEntry("test/first", typeof(Sprite), out _);
            first.AddCount(LifetimeScopeId.Manual);
            first.AddCount(owner_scope);
            first.AddCount(owner_scope);
            var second = AddEntry("test/second", typeof(Sprite), out _);
            second.AddCount(scene_scope);
            AddEntry("test/third", typeof(Sprite), out _); // 確保なし（内訳は 0 件）

            _reader.CopyAssetEntriesTo(_entries, _scopeCounts);

            var first_snapshot = FindEntry("test/first");
            Assert.AreEqual(2, first_snapshot.AccScopeCountLength);
            Assert.AreEqual(1, FindScopeCount(first_snapshot, LifetimeKind.Manual, 0));
            Assert.AreEqual(2, FindScopeCount(first_snapshot, LifetimeKind.Owner, 12345));
            Assert.AreEqual(-1, FindScopeCount(first_snapshot, LifetimeKind.Scene, 7), "他の Entry のスコープは含まれないはず");

            var second_snapshot = FindEntry("test/second");
            Assert.AreEqual(1, second_snapshot.AccScopeCountLength);
            Assert.AreEqual(1, FindScopeCount(second_snapshot, LifetimeKind.Scene, 7));

            Assert.AreEqual(0, FindEntry("test/third").AccScopeCountLength);
            Assert.AreEqual(3, _scopeCounts.Count, "全 Entry の内訳が過不足なく連結されるはず");
        }

        // 発行済みチケットのキー・型・スコープ・受け渡し済みの印がコピーされる
        [Test]
        public void CopyIssuedTicketsToCopiesKeyTypeScopeAndDelivered()
        {
            var owner_scope = new LifetimeScopeId(LifetimeKind.Owner, 999);
            var entry = AddEntry("test/key", typeof(Sprite), out _);
            RegisterTicket(entry, LifetimeScopeId.Manual);
            var owner_serial = _ticketRegistry.IssueSerial();
            entry.AddCount(owner_scope);
            _ticketRegistry.Register(owner_serial, entry.AccEntryKey, entry, owner_scope, null);
            _ticketRegistry.MarkDelivered(owner_serial);

            _reader.CopyIssuedTicketsTo(_tickets);

            Assert.AreEqual(2, _tickets.Count);
            foreach (var ticket in _tickets)
            {
                Assert.AreEqual(AssetKey.FromPath("test/key"), ticket.AccKey);
                Assert.AreEqual(typeof(Sprite), ticket.AccAssetType);
                Assert.IsFalse(ticket.AccIsGroup);
                if (ticket.AccSerial == owner_serial)
                {
                    Assert.AreEqual(LifetimeKind.Owner, ticket.AccScopeKind);
                    Assert.AreEqual(999, ticket.AccScopeValue);
                    Assert.IsTrue(ticket.AccIsDelivered);
                }
                else
                {
                    Assert.AreEqual(LifetimeKind.Manual, ticket.AccScopeKind);
                    Assert.IsFalse(ticket.AccIsDelivered, "発行しただけで、まだ返し終えていないはず");
                }
            }
        }

        // Label 一括ロード由来のチケットは、まとめの印とスタックトレース（まとめチケットのものを共有）が付く
        [Test]
        public void CopyIssuedTicketsToGroupTicketHasGroupFlagAndSharedStackTrace()
        {
            var group = new AssetGroupTicket("label", 1);
            var entry = AddEntry("test/key", typeof(Sprite), out _);
            RegisterTicket(entry, LifetimeScopeId.Manual, group);

            _reader.CopyIssuedTicketsTo(_tickets);

            Assert.AreEqual(1, _tickets.Count);
            Assert.IsTrue(_tickets[0].AccIsGroup);
            Assert.IsNotEmpty(_tickets[0].AccStackTrace);
            Assert.AreEqual(group.AccStackTrace, _tickets[0].AccStackTrace);
        }

        // 単体のチケットにも、確保時のスタックトレースが記録される
        [Test]
        public void CopyIssuedTicketsToSingleTicketHasStackTrace()
        {
            var entry = AddEntry("test/key", typeof(Sprite), out _);
            RegisterTicket(entry, LifetimeScopeId.Manual);

            _reader.CopyIssuedTicketsTo(_tickets);

            Assert.IsNotEmpty(_tickets[0].AccStackTrace);
        }

        // ゴミの入った List を渡しても、Clear されてから書き込まれる（List の使い回しで古い値が残らない）
        [Test]
        public void CopyToClearsListsBeforeWriting()
        {
            var entry = AddEntry("test/key", typeof(Sprite), out _);
            RegisterTicket(entry, LifetimeScopeId.Manual);
            var garbage_key = AssetKey.FromPath("garbage");
            _entries.Add(new AssetEntrySnapshot(garbage_key, typeof(Sprite), "garbage", 9, AsyncOperationStatus.None, 0, 0));
            _scopeCounts.Add(new ScopeCountSnapshot(LifetimeKind.Scene, 1, 9));
            _tickets.Add(new IssuedTicketSnapshot(99, garbage_key, typeof(Sprite), LifetimeKind.Scene, 1, true, false, "garbage"));

            _reader.CopyAssetEntriesTo(_entries, _scopeCounts);
            _reader.CopyIssuedTicketsTo(_tickets);

            Assert.AreEqual(1, _entries.Count);
            Assert.AreEqual(AssetKey.FromPath("test/key"), _entries[0].AccKey);
            Assert.AreEqual(1, _scopeCounts.Count);
            Assert.AreEqual(LifetimeKind.Manual, _scopeCounts[0].AccScopeKind);
            Assert.AreEqual(1, _tickets.Count);
            Assert.AreNotEqual(99, _tickets[0].AccSerial);
        }

        // 取得後に Model が変わっても（確保の減算・Entry の削除）、取得済みの Snapshot の値は変わらない
        [Test]
        public void SnapshotDoesNotChangeAfterModelChanges()
        {
            var entry = AddEntry("test/key", typeof(Sprite), out _);
            RegisterTicket(entry, LifetimeScopeId.Manual);
            _reader.CopyAssetEntriesTo(_entries, _scopeCounts);
            _reader.CopyIssuedTicketsTo(_tickets);

            entry.TryDecrementScope(LifetimeScopeId.Manual);
            _assetRegistry.RemoveEntryIfSame(entry.AccEntryKey, entry);
            _ticketRegistry.TryRemove(_tickets[0].AccSerial, out _, out _);

            Assert.AreEqual(1, _entries.Count);
            Assert.AreEqual(1, _entries[0].AccRefCount, "取得時点の RefCount のままのはず");
            Assert.AreEqual(1, _scopeCounts[0].AccCount);
            Assert.AreEqual(1, _tickets.Count);

            // 取り直せば、現在の状態（空）になる
            _reader.CopyAssetEntriesTo(_entries, _scopeCounts);
            _reader.CopyIssuedTicketsTo(_tickets);
            Assert.AreEqual(0, _entries.Count);
            Assert.AreEqual(0, _scopeCounts.Count);
            Assert.AreEqual(0, _tickets.Count);
        }

        // 空の Registry では、空の List になる
        [Test]
        public void CopyToEmptyRegistryProducesEmptyLists()
        {
            _reader.CopyAssetEntriesTo(_entries, _scopeCounts);
            _reader.CopyIssuedTicketsTo(_tickets);

            Assert.AreEqual(0, _entries.Count);
            Assert.AreEqual(0, _scopeCounts.Count);
            Assert.AreEqual(0, _tickets.Count);
        }

        // リークを、確保元ごとに集約した行として書き出す（Model の内容がそのまま渡る）
        [Test]
        public void CopyLeaksToCopiesAggregatedRowsFromModel()
        {
            var scope = new LifetimeScopeId(LifetimeKind.Owner, 12345);
            _leakRegistry.RecordTicketLeak(AssetKey.FromPath("leak/a"), typeof(FakeAsset), scope, "Enemy_01", "trace-a");
            _leakRegistry.RecordTicketLeak(AssetKey.FromPath("leak/a"), typeof(FakeAsset), scope, "Enemy_02", "trace-a");
            _leakRegistry.RecordGroupLeak("ui", 4, new LifetimeScopeId(LifetimeKind.Scene, 7), "Stage1", "trace-g");
            var leaks = new List<LeakSnapshot>();

            _reader.CopyLeaksTo(leaks);

            Assert.AreEqual(2, leaks.Count);
            var ticket_row = leaks.Find(l => !l.AccIsGroup);
            Assert.AreEqual(AssetKey.FromPath("leak/a"), ticket_row.AccKey);
            Assert.AreEqual(2, ticket_row.AccCount);
            Assert.AreEqual("Enemy_02", ticket_row.AccScopeName, "最後に発生したスコープ名になるはず");
            var group_row = leaks.Find(l => l.AccIsGroup);
            Assert.AreEqual("ui", group_row.AccLabel);
            Assert.AreEqual(4, group_row.AccGroupCount);
            Assert.AreEqual(LifetimeKind.Scene, group_row.AccScopeKind);
        }

        // ゴミの入った List は Clear されてから書き出される。空の Model では空になる
        [Test]
        public void CopyLeaksToClearsExistingContents()
        {
            var leaks = new List<LeakSnapshot> { default, default };

            _reader.CopyLeaksTo(leaks);

            Assert.AreEqual(0, leaks.Count);
        }

        // 取得後に記録が増えても、取得済みの値は変わらない
        [Test]
        public void CopyLeaksToSnapshotIsUnaffectedByLaterRecords()
        {
            var scope = new LifetimeScopeId(LifetimeKind.Owner, 1);
            _leakRegistry.RecordTicketLeak(AssetKey.FromPath("leak/a"), typeof(FakeAsset), scope, "O", "trace-a");
            var leaks = new List<LeakSnapshot>();
            _reader.CopyLeaksTo(leaks);
            var snapshot = leaks[0];

            _leakRegistry.RecordTicketLeak(AssetKey.FromPath("leak/a"), typeof(FakeAsset), scope, "O", "trace-a");

            Assert.AreEqual(1, snapshot.AccCount);
        }

        // 監視中の Owner が、InstanceID・登録時の名前・生存の状態で書き出される（Watcher を貼っていないときは、貼り付けなし）
        [Test]
        public void CopyWatchedOwnersToCopiesAliveOwnerWithoutWatcher()
        {
            var go = CreateGameObject("Enemy_01");
            var scope = new LifetimeScopeId(LifetimeKind.Owner, go.GetInstanceID());
            _ownerWatch.Watch(scope, go);
            var owners = new List<OwnerWatchSnapshot>();

            _reader.CopyWatchedOwnersTo(owners);

            Assert.AreEqual(1, owners.Count);
            Assert.AreEqual(go.GetInstanceID(), owners[0].AccInstanceId);
            Assert.AreEqual("Enemy_01", owners[0].AccOwnerName);
            Assert.IsTrue(owners[0].AccIsAlive);
            Assert.IsFalse(owners[0].AccHasWatcher, "Watcher を貼っていないため false のはず");
            Assert.IsFalse(owners[0].AccIsRegistered);
        }

        // 本物の Binder で Bind すると、Watcher の貼り付け・登録ありになる
        [Test]
        public void CopyWatchedOwnersToReportsWatcherBoundByRealBinder()
        {
            var go = CreateGameObject("Boss");
            var scope = new LifetimeScopeId(LifetimeKind.Owner, go.GetInstanceID());
            _ownerWatch.Watch(scope, go);
            var binder = new AssetLifetimeWatcherBinder();
            binder.Bind(go, scope, _ => { });
            var owners = new List<OwnerWatchSnapshot>();

            _reader.CopyWatchedOwnersTo(owners);

            Assert.IsTrue(owners[0].AccHasWatcher);
            Assert.IsTrue(owners[0].AccIsRegistered);
        }

        // Watcher は貼られているが、そのスコープが登録されていない（登録なし）状態を区別できる
        [Test]
        public void CopyWatchedOwnersToReportsWatcherWithoutScopeRegistration()
        {
            var go = CreateGameObject("Boss");
            var scope = new LifetimeScopeId(LifetimeKind.Owner, go.GetInstanceID());
            _ownerWatch.Watch(scope, go);
            var binder = new AssetLifetimeWatcherBinder();
            binder.Bind(go, scope, _ => { });
            binder.Unbind(go, scope); // Watcher は残り、スコープの登録だけが外れる
            var owners = new List<OwnerWatchSnapshot>();

            _reader.CopyWatchedOwnersTo(owners);

            Assert.IsTrue(owners[0].AccHasWatcher);
            Assert.IsFalse(owners[0].AccIsRegistered);
        }

        // GameObject が破棄済み（偽 null）でも、登録時の名前を持ち、破棄済み・Watcher なしになる
        [Test]
        public void CopyWatchedOwnersToReportsDestroyedOwnerWithRegisteredName()
        {
            var go = CreateGameObject("Bullet");
            var scope = new LifetimeScopeId(LifetimeKind.Owner, go.GetInstanceID());
            _ownerWatch.Watch(scope, go);
            UnityEngine.Object.DestroyImmediate(go);
            var owners = new List<OwnerWatchSnapshot>();

            _reader.CopyWatchedOwnersTo(owners);

            Assert.AreEqual(1, owners.Count);
            Assert.AreEqual("Bullet", owners[0].AccOwnerName);
            Assert.IsFalse(owners[0].AccIsAlive);
            Assert.IsFalse(owners[0].AccHasWatcher);
            Assert.IsFalse(owners[0].AccIsRegistered);
        }

        // 監視が外れた Owner は含まれない。複数の Owner はすべて書き出される
        [Test]
        public void CopyWatchedOwnersToExcludesUnwatchedOwners()
        {
            var first = CreateGameObject("A");
            var second = CreateGameObject("B");
            var scope_first = new LifetimeScopeId(LifetimeKind.Owner, first.GetInstanceID());
            var scope_second = new LifetimeScopeId(LifetimeKind.Owner, second.GetInstanceID());
            _ownerWatch.Watch(scope_first, first);
            _ownerWatch.Watch(scope_second, second);
            _ownerWatch.Unwatch(scope_first);
            var owners = new List<OwnerWatchSnapshot>();

            _reader.CopyWatchedOwnersTo(owners);

            Assert.AreEqual(1, owners.Count);
            Assert.AreEqual("B", owners[0].AccOwnerName);
        }

        // ゴミの入った List は Clear されてから書き出される。空の Model では空になる
        [Test]
        public void CopyWatchedOwnersToClearsExistingContents()
        {
            var owners = new List<OwnerWatchSnapshot> { default, default };

            _reader.CopyWatchedOwnersTo(owners);

            Assert.AreEqual(0, owners.Count);
        }

        // 取得後に Owner が破棄されても、取得済みの値は変わらない
        [Test]
        public void CopyWatchedOwnersToSnapshotIsUnaffectedByLaterDestroy()
        {
            var go = CreateGameObject("Enemy_02");
            _ownerWatch.Watch(new LifetimeScopeId(LifetimeKind.Owner, go.GetInstanceID()), go);
            var owners = new List<OwnerWatchSnapshot>();
            _reader.CopyWatchedOwnersTo(owners);
            var snapshot = owners[0];

            UnityEngine.Object.DestroyImmediate(go);

            Assert.IsTrue(snapshot.AccIsAlive);
        }

        // Owner の回収の記録が、経路・名前・回数・チケット累計・最後の ID とともに書き出される
        [Test]
        public void CopyOwnerReclaimsToCopiesRecordedRows()
        {
            _ownerReclaims.Record(OwnerReclaimRoute.OnDestroy, 11, "Enemy", 2);
            _ownerReclaims.Record(OwnerReclaimRoute.OnDestroy, 12, "Enemy", 1);
            _ownerReclaims.Record(OwnerReclaimRoute.Polling, 13, "Bullet", 4);
            var reclaims = new List<OwnerReclaimSnapshot>();

            _reader.CopyOwnerReclaimsTo(reclaims);

            Assert.AreEqual(2, reclaims.Count);
            var on_destroy = reclaims.Find(r => r.AccRoute == OwnerReclaimRoute.OnDestroy);
            Assert.AreEqual("Enemy", on_destroy.AccOwnerName);
            Assert.AreEqual(2, on_destroy.AccCount);
            Assert.AreEqual(3, on_destroy.AccTicketCount);
            Assert.AreEqual(12, on_destroy.AccLastInstanceId);
            var polling = reclaims.Find(r => r.AccRoute == OwnerReclaimRoute.Polling);
            Assert.AreEqual("Bullet", polling.AccOwnerName);
            Assert.AreEqual(4, polling.AccTicketCount);
        }

        // ゴミの入った List は Clear されてから書き出される。空の Model では空になる
        [Test]
        public void CopyOwnerReclaimsToClearsExistingContents()
        {
            var reclaims = new List<OwnerReclaimSnapshot> { default, default };

            _reader.CopyOwnerReclaimsTo(reclaims);

            Assert.AreEqual(0, reclaims.Count);
        }

        // Model が null のままでは窓口を作れない
        [Test]
        public void ConstructorNullModelThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new AddressableDebugReader(null, _ticketRegistry, _leakRegistry, _ownerWatch, _ownerReclaims));
            Assert.Throws<ArgumentNullException>(() => new AddressableDebugReader(_assetRegistry, null, _leakRegistry, _ownerWatch, _ownerReclaims));
            Assert.Throws<ArgumentNullException>(() => new AddressableDebugReader(_assetRegistry, _ticketRegistry, null, _ownerWatch, _ownerReclaims));
            Assert.Throws<ArgumentNullException>(() => new AddressableDebugReader(_assetRegistry, _ticketRegistry, _leakRegistry, null, _ownerReclaims));
            Assert.Throws<ArgumentNullException>(() => new AddressableDebugReader(_assetRegistry, _ticketRegistry, _leakRegistry, _ownerWatch, null));
        }
    }
}
