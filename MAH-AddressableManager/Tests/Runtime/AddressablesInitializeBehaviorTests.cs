using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.Util;
using UnityEngine.TestTools;

namespace MAH.AddressableManager.Tests.Runtime
{
    /// <summary>
    /// 本モジュールではなく、Addressables 本体の初期化の挙動を確かめる PlayMode テスト（initialize-spec 10.1）。
    /// Addressables を更新したときに、初期化機能が前提にしている挙動が変わっていないかを検知する回帰テストとして残す。
    /// 各テストは SetUp で Addressables の内部インスタンスを作り直し、「未初期化」の状態から始める。
    /// Editor が Play のたびに行っている作り直し（Addressables.m_Addressables の getter）と同じことを、リフレクションで行う。
    /// リフレクションはテストでのみ使う（internal の構造に依存するため、本番コードの初期化のやり直しには使わない）。
    /// </summary>
    public sealed class AddressablesInitializeBehaviorTests
    {
        private const string KEYS_ASSET_PATH = "Assets/@MAH/MAH-AddressableManager/Samples/AddressableLoad/ScriptableObjects/LoadingTestKeys.asset";
        private const float WAIT_TIMEOUT_SEC = 10f;
        // 接続を拒否されるURL（discard ポート）。Web リクエストを通るため、失敗が次フレーム以降（非同期）になる
        private const string UNREACHABLE_RUNTIME_DATA_URL = "http://127.0.0.1:9/settings.json";
        // runtime data が読めなかったときに Addressables が出すエラーログ（Addressables 2.9.1 の InitializationObjectsOperation）
        private const string RUNTIME_DATA_MISSING_ERROR = "RuntimeData is null.  Please ensure you have built the correct Player Content.";

        private static readonly Type IMPL_TYPE = typeof(Addressables).Assembly.GetType("UnityEngine.AddressableAssets.AddressablesImpl");
        private static readonly FieldInfo INSTANCE_FIELD = typeof(Addressables).GetField("m_AddressablesInstance", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo RELEASE_SCENE_OPERATION_METHOD = IMPL_TYPE?.GetMethod("ReleaseSceneManagerOperation", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo INITIALIZE_WITH_PATH_METHOD = IMPL_TYPE?.GetMethod(
            "InitializeAsync", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string), typeof(string), typeof(bool) }, null);

        private readonly List<Exception> _handledExceptions = new();
        private readonly Action<AsyncOperationHandle, Exception> _recordException;
        private Action<AsyncOperationHandle, Exception> _previousExceptionHandler;

        // 存在しないローカルの runtime data のパス。初期化を「設定が読めない」失敗にするために渡す（その場で同期的に失敗する）
        private static string MissingRuntimeDataPath => Path.Combine(Application.temporaryCachePath, "MissingRuntimeData", "settings.json");

        public AddressablesInitializeBehaviorTests()
        {
            _recordException = (_, ex) => _handledExceptions.Add(ex);
        }

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(IMPL_TYPE, "AddressablesImpl が見つかりません。Addressables の内部構成が変わった可能性があります。");
            Assert.IsNotNull(INSTANCE_FIELD, "Addressables.m_AddressablesInstance が見つかりません。");
            Assert.IsNotNull(INITIALIZE_WITH_PATH_METHOD, "AddressablesImpl.InitializeAsync(string, string, bool) が見つかりません。");

            ResetAddressablesInstance();

            // 失敗した操作の例外は ExceptionHandler（未設定なら LogException）経由でエラーログになり、テストが失敗扱いになる。
            // 意図して失敗させるテストのため、記録するだけのハンドラに差し替える（InitializeAsync は未設定のときだけ既定のハンドラを入れる）。
            // ExceptionHandler は static のため、元の値を退避して TearDown で戻す
            _previousExceptionHandler = ResourceManager.ExceptionHandler;
            _handledExceptions.Clear();
            ResourceManager.ExceptionHandler = _recordException;
        }

        [TearDown]
        public void TearDown()
        {
            // 後に走るテスト（LoadingPlayModeTests 等）が、失敗した初期化の状態とハンドラを引き継がないようにする
            ResourceManager.ExceptionHandler = _previousExceptionHandler;
            ResetAddressablesInstance();
        }

        // P1：失敗した初期化の handle が自動解放された後に呼び直すと、やり直さず同期で ArgumentOutOfRangeException になる
        [UnityTest]
        public IEnumerator ReinitializeAfterFailureWithAutoReleaseThrowsSynchronously()
        {
            var first = InitializeExpectingFailure(MissingRuntimeDataPath, true);
            yield return WaitUntilDone(first);
            yield return null; // 完了通知の中での自動解放が終わるのを待つ

            Assert.IsFalse(first.IsValid(), "autoReleaseHandle=true の handle は、完了後に解放されているはず");

            Exception thrown = null;
            try
            {
                Addressables.InitializeAsync();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
            Assert.IsInstanceOf<ArgumentOutOfRangeException>(thrown,
                "初期化を始めた印が戻らず、空のロケーター一覧の先頭を読むため、同期で例外になるはず");
        }

        // P2：handle を保持したまま呼び直すと同じ失敗済みの操作が返り、解放してから呼び直すと P1 と同じ例外になる
        [UnityTest]
        public IEnumerator ReinitializeAfterFailureWithoutAutoReleaseReturnsSameFailedOperation()
        {
            var first = InitializeExpectingFailure(MissingRuntimeDataPath, false);
            yield return WaitUntilDone(first);
            Assert.AreEqual(AsyncOperationStatus.Failed, first.Status, "存在しないパスでの初期化は失敗するはず");

            var second = Addressables.InitializeAsync();
            Assert.IsTrue(second.Equals(first), "handle を保持している間は、同じ操作が返るはず");
            Assert.AreEqual(AsyncOperationStatus.Failed, second.Status, "失敗した操作が、失敗のまま返るはず");

            Addressables.Release(first);

            Exception thrown = null;
            try
            {
                Addressables.InitializeAsync();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
            Assert.IsInstanceOf<ArgumentOutOfRangeException>(thrown, "解放してから呼び直しても、やり直さず P1 と同じ例外になるはず");
        }

        // P3：実行中に呼び直すと、同じ操作が返る（新しい初期化は始まらない）
        [UnityTest]
        public IEnumerator ReinitializeWhileRunningReturnsSameOperation()
        {
            // 状態を読むため handle を保持する（autoReleaseHandle=true だと完了と同時に解放され、Status を読めない）
            var first = InitializeExpectingFailure(UNREACHABLE_RUNTIME_DATA_URL, false);
            if (first.IsDone)
                Assert.Inconclusive("初期化が同期で終わったため、実行中の呼び直しを確認できませんでした。");

            var second = Addressables.InitializeAsync();
            Assert.IsTrue(second.Equals(first), "実行中は、同じ操作が返るはず");

            yield return WaitUntilDone(first); // 次のテストに実行中の操作を残さない
            Assert.AreEqual(AsyncOperationStatus.Failed, first.Status, "接続できない URL での初期化は失敗するはず");
            Addressables.Release(first);
        }

        // P4：初期化済みのときに呼ぶと、呼んだ直後に完了済みの操作が返る（同期で完了する）
        [UnityTest]
        public IEnumerator InitializeAfterSuccessCompletesSynchronously()
        {
            // 状態を読むため handle を保持する（autoReleaseHandle=true だと完了と同時に解放され、Status を読めない）
            var first = Addressables.InitializeAsync(false);
            yield return WaitUntilDone(first);
            Assert.AreEqual(AsyncOperationStatus.Succeeded, first.Status, "通常の初期化は成功するはず（Play Mode Script の設定を確認）");
            Addressables.Release(first);

            var second = Addressables.InitializeAsync(false);
            try
            {
                Assert.IsTrue(second.IsDone, "初期化済みなら、呼んだ直後に完了しているはず");
                Assert.AreEqual(AsyncOperationStatus.Succeeded, second.Status);
            }
            finally
            {
                if (second.IsValid())
                    Addressables.Release(second);
            }
        }

        // P5：初期化に失敗した後は、Load も失敗する（初期化済みの扱いのまま、ロケーターが空のため）
        [UnityTest]
        public IEnumerator LoadAfterInitializeFailureFails()
        {
            var keys = AssetDatabase.LoadAssetAtPath<LoadingTestKeysSO>(KEYS_ASSET_PATH);
            Assert.IsNotNull(keys, $"{KEYS_ASSET_PATH} が見つかりません。");

            var init = InitializeExpectingFailure(MissingRuntimeDataPath, true);
            yield return WaitUntilDone(init);

            var load = Addressables.LoadAssetAsync<UnityEngine.Object>(keys.AccSpriteAddress);
            yield return WaitUntilDone(load);
            try
            {
                Assert.AreEqual(AsyncOperationStatus.Failed, load.Status, "初期化に失敗した後は、Load も失敗するはず");
            }
            finally
            {
                if (load.IsValid())
                    Addressables.Release(load);
            }
        }

        /// <summary>
        /// 読めない runtime data のパスで初期化を始める（失敗させる）。
        /// 失敗時に Addressables が ExceptionHandler を通さずに直接出すエラーログ（InitializationObjectsOperation）を、出るものとして期待しておく
        /// </summary>
        private static AsyncOperationHandle<IResourceLocator> InitializeExpectingFailure(string runtime_data_path, bool auto_release_handle)
        {
            LogAssert.Expect(LogType.Error, RUNTIME_DATA_MISSING_ERROR);
            return InitializeWithPath(runtime_data_path, auto_release_handle);
        }

        /// <summary> internal の InitializeAsync(runtimeDataPath, providerSuffix, autoReleaseHandle) を呼ぶ。公開 API ではパスを指定できないため </summary>
        private static AsyncOperationHandle<IResourceLocator> InitializeWithPath(string runtime_data_path, bool auto_release_handle)
        {
            var impl = INSTANCE_FIELD.GetValue(null);
            return (AsyncOperationHandle<IResourceLocator>)INITIALIZE_WITH_PATH_METHOD.Invoke(impl, new object[] { runtime_data_path, null, auto_release_handle });
        }

        /// <summary> Addressables の内部インスタンスを作り直す（Editor が Play のたびに行う処理と同じ） </summary>
        private static void ResetAddressablesInstance()
        {
            var current = INSTANCE_FIELD.GetValue(null);
            if (current != null)
                RELEASE_SCENE_OPERATION_METHOD?.Invoke(current, null);
            INSTANCE_FIELD.SetValue(null, Activator.CreateInstance(IMPL_TYPE, new DefaultAllocationStrategy()));
        }

        private static IEnumerator WaitUntilDone(AsyncOperationHandle handle)
        {
            var start = Time.realtimeSinceStartup;
            while (!handle.IsDone)
            {
                if (Time.realtimeSinceStartup - start > WAIT_TIMEOUT_SEC)
                    Assert.Fail($"{WAIT_TIMEOUT_SEC} 秒以内に操作が完了しませんでした。");
                yield return null;
            }
        }
    }
}
