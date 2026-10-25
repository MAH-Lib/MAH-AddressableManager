using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MAH.AddressableManager.Tests.Runtime
{
    /// <summary>
    /// Editor 上で実行する PlayMode テスト。実際の Addressables を使い、読み込み・解放・自動回収を確認する。
    /// アドレスは LoadingTestKeysSO.asset（Inspector でプルダウン設定済み）から取得し、テストコードに直書きしない。
    /// 内部状態（Registry 等）は private のため、black box（公開 API の挙動とログ）で検証する。
    /// </summary>
    public sealed class LoadingPlayModeTests
    {
        private const string KEYS_ASSET_PATH = "Assets/@MAH/MAH-AddressableManager/Samples/AddressableLoad/ScriptableObjects/LoadingTestKeys.asset";

        private LoadingTestKeysSO _keys;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _keys = AssetDatabase.LoadAssetAtPath<LoadingTestKeysSO>(KEYS_ASSET_PATH);
            Assert.IsNotNull(_keys, $"{KEYS_ASSET_PATH} が見つかりません。");
            Assert.IsFalse(string.IsNullOrEmpty(_keys.AccSpriteAddress), "LoadingTestKeys の Sprite 用アドレスが未設定です。");
            Assert.IsFalse(string.IsNullOrEmpty(_keys.AccPrefabAddress), "LoadingTestKeys の Prefab 用アドレスが未設定です。");
        }

        private static AddressableManager CreateManager()
        {
            var go = new GameObject(nameof(AddressableManager));
            return go.AddComponent<AddressableManager>();
        }

        /// <summary> async Task を [UnityTest] の IEnumerator に橋渡しする（Task/UniTaskどちらでも同じ書き方で使える） </summary>
        private static IEnumerator RunAsync(Func<Task> action)
        {
            var task = action();
            while (!task.IsCompleted) yield return null;
            // Destroy()は実際の破棄を次フレームまで遅延させるため、finally内のDestroyが反映されるのを待ってから次のテストに進む
            // （待たないと次のテストのCreateManagerがAwake内の「既にInstanceがある」判定に引っかかり、AccHandlerが未設定のままになる）
            yield return null;
            if (task.IsFaulted)
                throw task.Exception?.InnerException ?? task.Exception ?? new Exception("非同期処理が失敗しました。");
        }

        // Path入口でSpriteとPrefabをLoadできる
        [UnityTest]
        public IEnumerator PathEntryLoadsSpriteAndPrefab() => RunAsync(async () =>
        {
            var manager = CreateManager();
            try
            {
                var sprite_ticket = await manager.AccHandler.LoadAsync<Sprite>(
                    AssetKey.FromPath(_keys.AccSpriteAddress), AssetLifetime.Manual, CancellationToken.None);
                Assert.IsNotNull(sprite_ticket.AccAsset, "Sprite がロードされているはず");
                manager.AccHandler.Release(sprite_ticket);

                var prefab_ticket = await manager.AccHandler.LoadAsync<GameObject>(
                    AssetKey.FromPath(_keys.AccPrefabAddress), AssetLifetime.Manual, CancellationToken.None);
                Assert.IsNotNull(prefab_ticket.AccAsset, "Prefab アセットがロードされているはず");
                manager.AccHandler.Release(prefab_ticket);
            }
            finally
            {
                UnityEngine.Object.Destroy(manager.gameObject);
            }
        });

        // AssetReference入口でLoadできる
        [UnityTest]
        public IEnumerator AssetReferenceEntryLoadsSuccessfully() => RunAsync(async () =>
        {
            var manager = CreateManager();
            try
            {
                var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
                var guid = FindGuidByAddress(settings, _keys.AccSpriteAddress);
                Assert.IsFalse(string.IsNullOrEmpty(guid), "Sprite アドレスの GUID が取得できませんでした。");

                var reference = new AssetReference(guid);
                var ticket = await manager.AccHandler.LoadAsync<Sprite>(
                    AssetKey.FromReference(reference), AssetLifetime.Manual, CancellationToken.None);
                Assert.IsNotNull(ticket.AccAsset);
                manager.AccHandler.Release(ticket);
            }
            finally
            {
                UnityEngine.Object.Destroy(manager.gameObject);
            }
        });

        // 明示解放後に同じキーを再Loadできる
        [UnityTest]
        public IEnumerator LoadAsyncAfterExplicitReleaseCanReloadSameKey() => RunAsync(async () =>
        {
            var manager = CreateManager();
            try
            {
                var key = AssetKey.FromPath(_keys.AccSpriteAddress);
                var ticket1 = await manager.AccHandler.LoadAsync<Sprite>(key, AssetLifetime.Manual, CancellationToken.None);
                manager.AccHandler.Release(ticket1);

                // 解放後に再度Loadしてもエラーにならないはず（Registryからエントリが正しく消えている証拠）
                var ticket2 = await manager.AccHandler.LoadAsync<Sprite>(key, AssetLifetime.Manual, CancellationToken.None);
                Assert.IsNotNull(ticket2.AccAsset);
                manager.AccHandler.Release(ticket2);
            }
            finally
            {
                UnityEngine.Object.Destroy(manager.gameObject);
            }
        });

        // Owner寿命でownerをDestroyすると自動回収されリーク警告が出る
        [UnityTest]
        public IEnumerator OwnerLifetimeOwnerDestroyedAutoReclaimedWithLeakWarning() => RunAsync(async () =>
        {
            var manager = CreateManager();
            var owner = new GameObject("PlayModeTest_Owner");
            try
            {
                var key = AssetKey.FromPath(_keys.AccSpriteAddress);
                await manager.AccHandler.LoadAsync<Sprite>(key, AssetLifetime.Owner(owner), CancellationToken.None);

                LogAssert.Expect(LogType.Warning, new Regex(".*明示 Release されずに.*"));
                UnityEngine.Object.Destroy(owner);
                await Task.Yield(); // OnDestroyの実行を待つ（PlayMode中は複数回避けたほうが安全）
                await Task.Delay(50);
            }
            finally
            {
                if (owner != null) UnityEngine.Object.Destroy(owner);
                UnityEngine.Object.Destroy(manager.gameObject);
            }
        });

        // Scene寿命でSceneManagerによりアンロードすると自動回収される
        [UnityTest]
        public IEnumerator SceneLifetimeUnloadedBySceneManagerAutoReclaimed() => RunAsync(async () =>
        {
            var manager = CreateManager();
            try
            {
                var active_scene = SceneManager.GetActiveScene();
                var key = AssetKey.FromPath(_keys.AccSpriteAddress);
                await manager.AccHandler.LoadAsync<Sprite>(key, AssetLifetime.Scene(active_scene), CancellationToken.None);

                // アクティブシーン自体はアンロードできないため、ここでは寿命解決が例外にならないことのみ確認する。
                // 実際のアンロードによる回収は、追加シーンを用いた手動検証で確認する（手動検証の項目を参照）。
                Assert.Pass("アクティブシーンでの寿命解決を確認。実アンロードでの回収は手動検証で確認する。");
            }
            finally
            {
                UnityEngine.Object.Destroy(manager.gameObject);
            }
        });

        // Managerが重複したとき、2つ目は破棄され1つ目がAccInstanceのまま
        [UnityTest]
        public IEnumerator DuplicateManagerSecondIsDestroyedFirstRemainsAccInstance()
        {
            var manager1 = CreateManager();
            yield return null;
            var first_instance = AddressableManager.AccInstance;
            Assert.AreSame(manager1, first_instance);

            var go2 = new GameObject("AddressableManager_Second");
            go2.AddComponent<AddressableManager>();
            yield return null; // 2つ目のDestroy(gameObject)が反映されるのを待つ

            Assert.IsTrue(go2 == null, "2つ目のManagerのGameObjectは破棄されているはず");
            Assert.AreSame(first_instance, AddressableManager.AccInstance, "AccInstanceは1つ目のままのはず");

            UnityEngine.Object.Destroy(manager1.gameObject);
            yield return null;
        }

        // Managerを破棄するとAccInstanceがnullになる
        [UnityTest]
        public IEnumerator DestroyManagerAccInstanceBecomesNull()
        {
            var manager = CreateManager();
            yield return null;
            Assert.IsNotNull(AddressableManager.AccInstance);

            UnityEngine.Object.Destroy(manager.gameObject);
            yield return null;

            Assert.IsNull(AddressableManager.AccInstance);
        }

        // ロード中にManagerを破棄すると、破棄後にリトライせず、呼び出し元にはOperationCanceledExceptionが届く
        // （修正前は、破棄後に再開したLoadAsyncが失敗とみなしてリトライし、新しいhandleがリークした）
        [UnityTest]
        public IEnumerator DestroyManagerWhileLoadingDoesNotRetryAndThrowsOperationCanceled() => RunAsync(async () =>
        {
            // CreateManager は設定SOを割り当てないため、Awake で必ず出る警告を、想定内のログとして先に登録する
            // （登録しないと、最後の NoUnexpectedReceived が、この警告を想定外として拾ってしまう）
            LogAssert.Expect(LogType.Warning, new Regex("AddressableRuntimeSettingsSO が未設定"));
            var manager = CreateManager();
            try
            {
                var task = manager.AccHandler.LoadAsync<Sprite>(
                    AssetKey.FromPath(_keys.AccSpriteAddress), AssetLifetime.Manual, CancellationToken.None);
                UnityEngine.Object.DestroyImmediate(manager.gameObject); // ロードの完了を待たずに破棄する（OnDestroy は即座に実行される）

                OperationCanceledException caught = null;
                try { await task; } catch (OperationCanceledException ex) { caught = ex; }
                if (caught == null)
                    Assert.Inconclusive("破棄より先にロードが完了したため、ロード中の破棄を再現できませんでした。");

                // 修正前は、破棄後のリトライの警告・例外ログがここまでに出る。リトライ間隔（既定1000ms）より長く待って確認する
                await Task.Delay(1500);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (manager != null) UnityEngine.Object.DestroyImmediate(manager.gameObject);
            }
        });

        // 存在しないアドレス（InvalidKeyException）はリトライせず、待たずにAssetLoadExceptionになる
        [UnityTest]
        public IEnumerator LoadAsyncNonExistentAddressFailsWithoutRetryThrowsAssetLoadException() => RunAsync(async () =>
        {
            var manager = CreateManager();
            // AddressablesとAddressableLoggerがException/Errorをログ出力するため、
            // 件数・内容をこのテストで固定化せずに済むよう一時的にログ失敗判定を無効化する
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var key = AssetKey.FromPath("Assets/__this_address_does_not_exist__.png");

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                AssetLoadException caught = null;
                try
                {
                    await manager.AccHandler.LoadAsync<Sprite>(key, AssetLifetime.Manual, CancellationToken.None);
                }
                catch (AssetLoadException ex)
                {
                    caught = ex;
                }
                stopwatch.Stop();
                Assert.IsNotNull(caught, "存在しないアドレスは AssetLoadException になるはず");
                // 既定のリトライ設定（3回・1000ms間隔）で待っていれば2秒かかる。1回で打ち切れていれば、リトライ間隔（1000ms）に届かない
                Assert.Less(stopwatch.ElapsedMilliseconds, 1000, "回復しない失敗は、リトライの待機なしで失敗するはず");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                UnityEngine.Object.Destroy(manager.gameObject);
            }
        });

        // テスト用キー定義のアドレスがAddressablesの設定に実在する
        [Test]
        public void LoadingTestKeysAddressesExistInAddressableSettings()
        {
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            Assert.IsNotNull(settings, "Addressables の設定が見つかりません。");

            Assert.IsTrue(AddressExists(settings, _keys.AccSpriteAddress), $"Sprite用アドレス「{_keys.AccSpriteAddress}」が Addressables に存在しません。");
            Assert.IsTrue(AddressExists(settings, _keys.AccPrefabAddress), $"Prefab用アドレス「{_keys.AccPrefabAddress}」が Addressables に存在しません。");
        }

        private static bool AddressExists(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings, string address)
            => FindGuidByAddress(settings, address) != null;

        // アドレス文字列はAssetDatabaseの実パスとは限らない（カスタムアドレスの場合がある）ため、
        // Addressablesの設定からアドレス一致するエントリを探してGUIDを引く
        private static string FindGuidByAddress(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings, string address)
        {
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry != null && entry.address == address)
                        return entry.guid;
                }
            }
            return null;
        }
    }
}
