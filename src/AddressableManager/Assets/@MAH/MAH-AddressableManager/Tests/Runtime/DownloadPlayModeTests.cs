using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using MAH.AddressableManager;
using MAH.AddressableManager.Core;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.Util;
using UnityEngine.TestTools;

namespace MAH.AddressableManager.Tests.Runtime
{
    /// <summary>
    /// ダウンロード機能（download-spec）を、実際の Addressables と Manager で確かめる PlayMode テスト。
    /// Play Mode Script が「Use Asset Database」のとき（既定）は、バンドルが無いため、ダウンロードが必要なものは常に 0 件になる。
    /// そのため、このテストで確かめるのは「Manager・Handler 経由で、確認・判定・ダウンロードが例外なく動くこと」と「handle の解放漏れが無いこと」で、
    /// 実際のダウンロード（リモート配信）は、ローカルのホスティングを使った手動検証で確かめる（steering の design.md 6章）。
    /// 各テストは Addressables の内部インスタンスを作り直し、「未初期化」の状態から始める（InitializePlayModeTests と同じ手順）。
    /// </summary>
    public sealed class DownloadPlayModeTests
    {
        // どのグループにも付いていない Label。該当するアセットが無いため、ダウンロードが必要なものは 0 件になる
        private const string UNUSED_LABEL = "mah-download-playmode-test";

        private static readonly Type IMPL_TYPE = typeof(Addressables).Assembly.GetType("UnityEngine.AddressableAssets.AddressablesImpl");
        private static readonly FieldInfo INSTANCE_FIELD = typeof(Addressables).GetField("m_AddressablesInstance", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo RELEASE_SCENE_OPERATION_METHOD = IMPL_TYPE?.GetMethod("ReleaseSceneManagerOperation", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MANAGER_SETTINGS_FIELD = typeof(AddressableManager).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic);
        // ResourceManager が保持している操作の数（internal）。handle の解放漏れを、完了後に操作が残っていないことで確かめる
        private static readonly PropertyInfo OPERATION_CACHE_COUNT_PROPERTY =
            typeof(UnityEngine.ResourceManagement.ResourceManager).GetProperty("OperationCacheCount", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<UnityEngine.Object> _created = new(); // テストが作った Manager・設定 SO。TearDown で破棄する

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Assert.IsNotNull(MANAGER_SETTINGS_FIELD, "AddressableManager._settings が見つかりません。");
            Assert.IsNotNull(OPERATION_CACHE_COUNT_PROPERTY, "ResourceManager.OperationCacheCount が見つかりません（Addressables の内部構成が変わった可能性があります）。");
        }

        [SetUp]
        public void SetUp()
        {
            ResetAddressablesInstance();
        }

        [TearDown]
        public void TearDown()
        {
            // Manager を先に破棄する（OnDestroy が、Download・Initialize の Dispose を行う）。次のテストに Addressables の状態を持ち越さない
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    UnityEngine.Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
            ResetAddressablesInstance();
        }

        /// <summary> 既定の設定（自動初期化）で、Manager を作って有効にする。Download は、初期化の成功を待ってから動く </summary>
        private AddressableManager CreateManager()
        {
            var settings = ScriptableObject.CreateInstance<AddressableRuntimeSettingsSO>();
            _created.Add(settings);

            var go = new GameObject(nameof(AddressableManager));
            go.SetActive(false); // Awake が走る前に、設定（_settings）を差し込む
            var manager = go.AddComponent<AddressableManager>();
            MANAGER_SETTINGS_FIELD.SetValue(manager, settings);
            _created.Add(go);
            go.SetActive(true);
            return manager;
        }

        private static int GetOperationCacheCount()
            => (int)OPERATION_CACHE_COUNT_PROPERTY.GetValue(Addressables.ResourceManager);

        /// <summary> async Task を [UnityTest] の IEnumerator に橋渡しする（Task/UniTaskどちらでも同じ書き方で使える） </summary>
        private static IEnumerator RunAsync(Func<Task> action)
        {
            var task = action();
            while (!task.IsCompleted) yield return null;
            yield return null;
            if (task.IsFaulted)
                throw task.Exception?.InnerException ?? task.Exception ?? new Exception("非同期処理が失敗しました。");
        }

        // 何も処理していない間の状態は None（ダウンロードなし）
        [UnityTest]
        public IEnumerator StatusIsNoneBeforeAnyProcess() => RunAsync(async () =>
        {
            var manager = CreateManager();

            Assert.AreEqual(DownloadPhase.None, manager.AccHandler.AccDownloadStatus.AccPhase);
            Assert.IsFalse(manager.AccHandler.AccDownloadStatus.AccIsBusy);
            await Task.Yield();
        });

        // Use Asset Database では、更新データは無い（判定は false）。カタログは更新されず、状態は Completed になる
        [UnityTest]
        public IEnumerator HasUpdateReturnsFalseWithAssetDatabase() => RunAsync(async () =>
        {
            var manager = CreateManager();

            var has_update = await manager.AccHandler.HasUpdateAsync(new[] { UNUSED_LABEL });

            Assert.IsFalse(has_update);
            Assert.AreEqual(DownloadPhase.Completed, manager.AccHandler.AccDownloadStatus.AccPhase);
        });

        // 確認は、0 件・0 バイトを返す
        [UnityTest]
        public IEnumerator CheckUpdateReturnsZeroCountsWithAssetDatabase() => RunAsync(async () =>
        {
            var manager = CreateManager();

            var result = await manager.AccHandler.CheckUpdateAsync(new[] { UNUSED_LABEL });

            Assert.AreEqual(0, result.AccBundleCount);
            Assert.AreEqual(0L, result.AccTotalBytes);
            Assert.IsFalse(result.AccHasDownload);
            Assert.IsFalse(result.AccIsCatalogUpdatePending);
            Assert.AreEqual(DownloadPhase.Completed, manager.AccHandler.AccDownloadStatus.AccPhase);
        });

        // ダウンロードは、例外なく Completed で終わり、最後の通知（Completed）が届く。割合は 1
        [UnityTest]
        public IEnumerator DownloadCompletesAndNotifiesCompleted() => RunAsync(async () =>
        {
            var manager = CreateManager();
            var progress = new RecordingProgress();

            await manager.AccHandler.DownloadAsync(new[] { UNUSED_LABEL }, progress);

            Assert.AreEqual(DownloadPhase.Completed, manager.AccHandler.AccDownloadStatus.AccPhase);
            Assert.AreEqual(1f, manager.AccHandler.AccDownloadStatus.AccPercent);
            Assert.IsNotEmpty(progress.Values);
            Assert.AreEqual(DownloadPhase.Completed, progress.Values[progress.Values.Count - 1].AccPhase, "最後の通知は Completed のはず");
        });

        // 確認・判定・ダウンロードを通しても、ResourceManager に操作（handle）が残らない
        [UnityTest]
        public IEnumerator ProcessesDoNotLeakOperations() => RunAsync(async () =>
        {
            var manager = CreateManager();
            await manager.AccHandler.InitializeAsync(); // 初期化の操作は、基準を取る前に終わらせる
            var before = GetOperationCacheCount();

            await manager.AccHandler.HasUpdateAsync(new[] { UNUSED_LABEL });
            await manager.AccHandler.CheckUpdateAsync(new[] { UNUSED_LABEL });
            await manager.AccHandler.DownloadAsync(new[] { UNUSED_LABEL });

            Assert.LessOrEqual(GetOperationCacheCount(), before, "完了後に、handle の解放漏れで操作が残っていないはず");
        });

        // Label を渡さない版は、SO の Label が空なら InvalidOperationException。状態は変わらない
        [UnityTest]
        public IEnumerator NoArgumentWithEmptySettingsLabelsThrowsInvalidOperation() => RunAsync(async () =>
        {
            var manager = CreateManager();

            var threw = false;
            try
            {
                await manager.AccHandler.HasUpdateAsync();
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "SO の Label が空なら、InvalidOperationException のはず");
            Assert.AreEqual(DownloadPhase.None, manager.AccHandler.AccDownloadStatus.AccPhase);
        });

        // Manager の破棄（OnDestroy）が、例外なく終わる。破棄後に、Manager 経由の Handler は使えない
        [UnityTest]
        public IEnumerator ManagerDestroyDoesNotThrowAfterProcesses() => RunAsync(async () =>
        {
            var manager = CreateManager();
            await manager.AccHandler.DownloadAsync(new[] { UNUSED_LABEL });

            Assert.DoesNotThrow(() => UnityEngine.Object.DestroyImmediate(manager.gameObject));
            Assert.IsNull(AddressableManager.AccInstance);
        });

        private static void ResetAddressablesInstance()
        {
            var current = INSTANCE_FIELD.GetValue(null);
            if (current != null)
                RELEASE_SCENE_OPERATION_METHOD?.Invoke(current, null);
            INSTANCE_FIELD.SetValue(null, Activator.CreateInstance(IMPL_TYPE, new DefaultAllocationStrategy()));
        }

        /// <summary> 届いた進捗を、届いた順に記録するだけの IProgress（Progress&lt;T&gt; は同期コンテキストに投げ直すため、テストでは使わない） </summary>
        private sealed class RecordingProgress : IProgress<DownloadStatus>
        {
            internal readonly List<DownloadStatus> Values = new();

            public void Report(DownloadStatus value) => Values.Add(value);
        }
    }
}
