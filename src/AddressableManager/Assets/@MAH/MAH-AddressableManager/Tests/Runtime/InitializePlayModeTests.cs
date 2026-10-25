using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core;
using MAH.AddressableManager.Core.Initialize;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.Util;
using UnityEngine.TestTools;

namespace MAH.AddressableManager.Tests.Runtime
{
    /// <summary>
    /// 初期化機能（initialize-spec）を、実際の Addressables と Manager で確かめる PlayMode テスト。
    /// 各テストは Addressables の内部インスタンスを作り直し、「未初期化」の状態から始める（AddressablesInitializeBehaviorTests と同じ手順）。
    /// 初期化の失敗は、Addressables を壊す必要があるため、Addressables 本体の挙動を確かめる AddressablesInitializeBehaviorTests と、
    /// 偽の Service を使う EditMode テスト（InitializeControllerTests）に任せる。
    /// </summary>
    public sealed class InitializePlayModeTests
    {
        private const string KEYS_ASSET_PATH = "Assets/@MAH/MAH-AddressableManager/Samples/AddressableLoad/ScriptableObjects/LoadingTestKeys.asset";

        private static readonly Type IMPL_TYPE = typeof(Addressables).Assembly.GetType("UnityEngine.AddressableAssets.AddressablesImpl");
        private static readonly FieldInfo INSTANCE_FIELD = typeof(Addressables).GetField("m_AddressablesInstance", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo RELEASE_SCENE_OPERATION_METHOD = IMPL_TYPE?.GetMethod("ReleaseSceneManagerOperation", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MANAGER_SETTINGS_FIELD = typeof(AddressableManager).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SETTINGS_INITIALIZE_FIELD = typeof(AddressableRuntimeSettingsSO).GetField("_initialize", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<UnityEngine.Object> _created = new(); // テストが作った Manager・設定 SO。TearDown で破棄する
        private LoadingTestKeysSO _keys;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Assert.IsNotNull(MANAGER_SETTINGS_FIELD, "AddressableManager._settings が見つかりません。");
            Assert.IsNotNull(SETTINGS_INITIALIZE_FIELD, "AddressableRuntimeSettingsSO._initialize が見つかりません。");
            _keys = AssetDatabase.LoadAssetAtPath<LoadingTestKeysSO>(KEYS_ASSET_PATH);
            Assert.IsNotNull(_keys, $"{KEYS_ASSET_PATH} が見つかりません。");
        }

        [SetUp]
        public void SetUp()
        {
            ResetAddressablesInstance();
        }

        [TearDown]
        public void TearDown()
        {
            // Manager を先に破棄する（OnDestroy が、初期化の停止と、変換関数の復元を行う）。次のテストに Addressables の状態を持ち越さない
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    UnityEngine.Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
            ResetAddressablesInstance();
        }

        /// <summary>
        /// 指定の初期化設定で、無効状態の Manager を作る。Awake は、呼び出し側が SetActive(true) したときに走る。
        /// 無効のまま作るのは、Awake が走る前に設定（_settings）を差し込むため。Handler は Awake で生成されるため、SetActive(true) より前に AccHandler は使えない
        /// </summary>
        private AddressableManager CreateInactiveManager(InitializeMode mode, LoadBeforeInitializePolicy policy = LoadBeforeInitializePolicy.PassThrough, bool enable_cache_busting = false)
        {
            var settings = ScriptableObject.CreateInstance<AddressableRuntimeSettingsSO>();
            SETTINGS_INITIALIZE_FIELD.SetValue(settings, new AddressableInitializeSettings(mode, policy, true, enable_cache_busting));
            _created.Add(settings);

            var go = new GameObject(nameof(AddressableManager));
            go.SetActive(false);
            var manager = go.AddComponent<AddressableManager>();
            MANAGER_SETTINGS_FIELD.SetValue(manager, settings);
            _created.Add(go);
            return manager;
        }

        /// <summary>
        /// 初期化の完了を数える。利用者向けの書き方と同じく、購読時に AccIsInitialized も確認する
        /// （Addressables が初期化済みなら、自動モードは Awake の中で完了し、購読する前にイベントが発火済みになるため）。SetActive(true) の後に呼ぶこと
        /// </summary>
        private static void CountInitialized(AddressableManager manager, Action onInitialized)
        {
            var handler = manager.AccHandler;
            if (handler.AccIsInitialized)
            {
                onInitialized();
                return;
            }
            handler.OnInitialized += onInitialized;
        }

        /// <summary> async Task を [UnityTest] の IEnumerator に橋渡しする（Task/UniTaskどちらでも同じ書き方で使える） </summary>
        private static IEnumerator RunAsync(Func<Task> action)
        {
            var task = action();
            while (!task.IsCompleted) yield return null;
            yield return null;
            if (task.IsFaulted)
                throw task.Exception?.InnerException ?? task.Exception ?? new Exception("非同期処理が失敗しました。");
        }

        // ---- 自動モード ----

        // 自動モード：Awake で初期化が始まり、await で完了を待てる。成功すると状態が Succeeded になり、OnInitialized が1回だけ発火する
        [UnityTest]
        public IEnumerator AutoModeInitializesOnAwakeAndSucceeds() => RunAsync(async () =>
        {
            var manager = CreateInactiveManager(InitializeMode.Auto);
            var initialized = 0;

            manager.gameObject.SetActive(true); // Awake で初期化が始まる
            CountInitialized(manager, () => initialized++);
            Assert.AreNotEqual(InitializeState.NotStarted, manager.AccHandler.AccInitializeState, "自動モードでは、Awake で初期化が始まっているはず");

            await manager.AccHandler.InitializeAsync(); // 実行中の初期化をそのまま待つ（新しく始めない）

            Assert.AreEqual(InitializeState.Succeeded, manager.AccHandler.AccInitializeState);
            Assert.IsTrue(manager.AccHandler.AccIsInitialized);
            Assert.AreEqual(1, initialized, "OnInitialized は1回だけ発火するはず");

            await manager.AccHandler.InitializeAsync(); // 成功済みなら、すぐ完了する
            Assert.AreEqual(1, initialized, "完了済みの InitializeAsync で、イベントは再発火しないはず");
        });

        // 自動モードで実行中に複数の呼び出し元が待っても、同じ初期化を共有し、全員が完了する
        [UnityTest]
        public IEnumerator AutoModeSharesRunningInitializationAmongCallers() => RunAsync(async () =>
        {
            var manager = CreateInactiveManager(InitializeMode.Auto);
            var initialized = 0;
            manager.gameObject.SetActive(true);
            CountInitialized(manager, () => initialized++);

            var task1 = manager.AccHandler.InitializeAsync();
            var task2 = manager.AccHandler.InitializeAsync();
            await task1;
            await task2;

            Assert.AreEqual(InitializeState.Succeeded, manager.AccHandler.AccInitializeState);
            Assert.AreEqual(1, initialized);
        });

        // 完了時には、待っていた呼び出し元に進捗 1 が届く
        [UnityTest]
        public IEnumerator AutoModeReportsProgressCompletingWithOne() => RunAsync(async () =>
        {
            var manager = CreateInactiveManager(InitializeMode.Auto);
            manager.gameObject.SetActive(true);
            var progress = new RecordingProgress();

            await manager.AccHandler.InitializeAsync(progress);

            Assert.IsNotEmpty(progress.Values);
            Assert.AreEqual(1f, progress.Values[progress.Values.Count - 1], "最後に必ず 1 が届くはず");
        });

        // ---- 手動モード ----

        // 手動モード：InitializeAsync を呼ぶまで始まらず、呼んだ後に Succeeded になって OnInitialized が発火する
        [UnityTest]
        public IEnumerator ManualModeStartsOnInitializeAsyncAndSucceeds() => RunAsync(async () =>
        {
            var manager = CreateInactiveManager(InitializeMode.Manual);
            var initialized = 0;
            manager.gameObject.SetActive(true);
            CountInitialized(manager, () => initialized++);

            await Task.Yield(); // 少し待っても、手動モードでは始まらない
            Assert.AreEqual(InitializeState.NotStarted, manager.AccHandler.AccInitializeState);
            Assert.IsFalse(manager.AccHandler.AccIsInitialized);
            Assert.AreEqual(0, initialized);

            await manager.AccHandler.InitializeAsync();

            Assert.AreEqual(InitializeState.Succeeded, manager.AccHandler.AccInitializeState);
            Assert.IsTrue(manager.AccHandler.AccIsInitialized);
            Assert.AreEqual(1, initialized);

            await manager.AccHandler.InitializeAsync(); // 2回目以降は、すぐ完了して再初期化しない
            Assert.AreEqual(1, initialized);
        });

        // 手動モード＋PassThrough（既定）：InitializeAsync より前の Load は、これまでどおり読み込める（Addressables が自動で初期化する）
        [UnityTest]
        public IEnumerator ManualModePassThroughLoadsBeforeInitializeAsync() => RunAsync(async () =>
        {
            var manager = CreateInactiveManager(InitializeMode.Manual, LoadBeforeInitializePolicy.PassThrough);
            manager.gameObject.SetActive(true);

            var ticket = await manager.AccHandler.LoadAsync<Sprite>(
                AssetKey.FromPath(_keys.AccSpriteAddress), AssetLifetime.Manual, CancellationToken.None);

            Assert.IsNotNull(ticket.AccAsset, "待たずに読み込めるはず");
            Assert.AreEqual(InitializeState.NotStarted, manager.AccHandler.AccInitializeState, "Manager を通らない初期化のため、状態は NotStarted のまま");
            manager.AccHandler.Release(ticket);
        });

        // 手動モード＋WaitForInitialize：InitializeAsync を呼ぶ前の Load は InvalidOperationException。呼んだ後は読み込める
        [UnityTest]
        public IEnumerator ManualModeWaitForInitializeThrowsBeforeInitializeAsyncThenLoads() => RunAsync(async () =>
        {
            var manager = CreateInactiveManager(InitializeMode.Manual, LoadBeforeInitializePolicy.WaitForInitialize);
            manager.gameObject.SetActive(true);
            var key = AssetKey.FromPath(_keys.AccSpriteAddress);

            InvalidOperationException caught = null;
            try
            {
                await manager.AccHandler.LoadAsync<Sprite>(key, AssetLifetime.Manual, CancellationToken.None);
            }
            catch (InvalidOperationException ex)
            {
                caught = ex;
            }
            Assert.IsNotNull(caught, "InitializeAsync を呼び忘れた Load は、すぐ例外になるはず");

            await manager.AccHandler.InitializeAsync();
            var ticket = await manager.AccHandler.LoadAsync<Sprite>(key, AssetLifetime.Manual, CancellationToken.None);

            Assert.IsNotNull(ticket.AccAsset);
            manager.AccHandler.Release(ticket);
        });

        // 自動モード＋WaitForInitialize：初期化中に始めた Load は、初期化の完了を待ってから読み込まれる
        [UnityTest]
        public IEnumerator AutoModeWaitForInitializeLoadWaitsForInitialization() => RunAsync(async () =>
        {
            var manager = CreateInactiveManager(InitializeMode.Auto, LoadBeforeInitializePolicy.WaitForInitialize);
            manager.gameObject.SetActive(true);

            var ticket = await manager.AccHandler.LoadAsync<Sprite>(
                AssetKey.FromPath(_keys.AccSpriteAddress), AssetLifetime.Manual, CancellationToken.None);

            Assert.IsNotNull(ticket.AccAsset);
            Assert.IsTrue(manager.AccHandler.AccIsInitialized, "待って読み込んだ後は、初期化が成功しているはず");
            manager.AccHandler.Release(ticket);
        });

        // ---- URL キャッシュ対策 ----

        // 設定が ON のとき、Awake で変換関数が設定され、Manager の破棄で元へ戻る。OFF のときは触らない
        [UnityTest]
        public IEnumerator CatalogCacheBustingIsAppliedOnAwakeAndRestoredOnDestroy() => RunAsync(async () =>
        {
            Assert.IsNull(Addressables.InternalIdTransformFunc, "SetUp で作り直した直後は、変換関数が無いはず");

            var off_manager = CreateInactiveManager(InitializeMode.Manual, enable_cache_busting: false);
            off_manager.gameObject.SetActive(true);
            Assert.IsNull(Addressables.InternalIdTransformFunc, "OFF のときは、変換関数に触れないはず");
            UnityEngine.Object.DestroyImmediate(off_manager.gameObject);

            var on_manager = CreateInactiveManager(InitializeMode.Manual, enable_cache_busting: true);
            on_manager.gameObject.SetActive(true);
            Assert.IsNotNull(Addressables.InternalIdTransformFunc, "ON のときは、Awake で変換関数が設定されるはず");

            UnityEngine.Object.DestroyImmediate(on_manager.gameObject); // OnDestroy で元へ戻る
            Assert.IsNull(Addressables.InternalIdTransformFunc, "Manager の破棄で、設定前（なし）へ戻るはず");
            await Task.Yield();
        });

        // 初期化中の Manager の破棄（待機中の InitializeAsync が OperationCanceledException で終わること）は、ここでは確認しない。
        // 実際の Addressables は、この環境では初期化が同期的に終わり、「実行中」を作れないため。
        // 偽の Service で確実に再現できる EditMode テスト（InitializeControllerTests.DisposeCancelsWaitingCallersAndKeepsState）で確認する

        // ---- 補助 ----

        /// <summary> Addressables の内部インスタンスを作り直す（Editor が Play のたびに行う処理と同じ。AddressablesInitializeBehaviorTests と同じ手順） </summary>
        private static void ResetAddressablesInstance()
        {
            var current = INSTANCE_FIELD.GetValue(null);
            if (current != null)
                RELEASE_SCENE_OPERATION_METHOD?.Invoke(current, null);
            INSTANCE_FIELD.SetValue(null, Activator.CreateInstance(IMPL_TYPE, new DefaultAllocationStrategy()));
        }

        // 値を記録するだけの IProgress（Progress<T> は SynchronizationContext に投げるため使わない）
        private sealed class RecordingProgress : IProgress<float>
        {
            internal readonly List<float> Values = new();

            public void Report(float value) => Values.Add(value);
        }
    }
}
