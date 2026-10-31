# クラス図：初期化（Initialize）

> 最終更新日：2026-10-05
> 仕様は [`Docs/specs/initialize-spec.md`](../specs/initialize-spec.md) 7章を参照。

## 1. Manager・Controller・Service

Manager が InitializeController・`InitializeStateModel`・Service・Handler を生成し、InitializeController を `IInitializationGate` として LoadingController に注入する。利用者の入口は Handler（`IAddressableHandler`）で、初期化の API を InitializeController へ転送する。InitializeController と LoadingController は互いを参照しない。

```mermaid
classDiagram
    class AddressableManager {
        +AccInstance : AddressableManager$
        +AccHandler : IAddressableHandler
        -OnLateUpdate : event Action~float~
    }
    class IAddressableHandler {
        <<interface>>
        +AccInitializeState : InitializeState
        +AccIsInitialized : bool
        +OnInitialized : event Action
        +OnInitializeFailed : event Action~AddressableInitializeException~
        +InitializeAsync(progress, ct)
    }
    class AddressableHandler {
        +OnInitialized add/remove → Controller.OnSucceeded
        +OnInitializeFailed add/remove → Controller.OnFailed
    }
    class InitializeController {
        -_settings : AddressableInitializeSettings
        -_stateModel : InitializeStateModel
        -_service : IAddressablesInitializeService
        -_disposeCts : CancellationTokenSource
        -_sharedSource : SharedCompletion
        +AccState : InitializeState
        +AccIsInitialized : bool
        ~OnSucceeded : event Action
        ~OnFailed : event Action~AddressableInitializeException~
        +InitializeAsync(progress, ct) UniTask/Awaitable
        +StartAutoInitialize()
        +Tick(deltaTime)
        +Dispose()
    }
    class InitializeStateModel {
        -_state : InitializeState
        -_failure : AddressableInitializeException
        -_sharedTask : SharedCompletion
        -_progressTargets : List~IProgress~
        +AccState : InitializeState
        +AccFailure : AddressableInitializeException
        +AccSharedTask : SharedCompletion
        +MarkRunning(task)
        +MarkSucceeded()
        +MarkFailed(exception)
        +AddProgressTarget(progress)
        +RemoveProgressTarget(progress)
        +ReportProgress(value)
    }
    class IInitializationGate {
        <<interface>>
        +AccIsOpen : bool
        +WaitUntilOpenAsync(ct) UniTask/Awaitable
    }
    class IAddressablesInitializeService {
        <<interface>>
        +StartInitialize(autoReleaseHandle) UniTask/Awaitable
        +GetPercentComplete() float
        +ReleaseHandle()
    }
    class AddressablesInitializeService
    class CatalogCacheBustingService {
        -_sessionValue : long
        -_serial : long
        -_previousTransform : Func~IResourceLocation, string~
        -_appliedTransform : Func~IResourceLocation, string~
        +Apply()
        +Restore()
        +TransformInternalId(location) string
    }
    class AddressableInitializeSettings {
        -_mode : InitializeMode
        -_loadBeforeInitialize : LoadBeforeInitializePolicy
        -_autoReleaseHandle : bool
        -_enableCatalogCacheBusting : bool
    }
    class LoadingController {
        -_initializationGate : IInitializationGate
    }
    class AddressableRuntimeSettingsSO {
        +AccInitialize : AddressableInitializeSettings
        +AccLoading : AddressableLoadingSettings
    }

    AddressableManager --> IAddressableHandler : 公開(AccHandler)
    IAddressableHandler <|.. AddressableHandler
    AddressableHandler --> InitializeController : 転送のみ（イベントは add/remove で直接つなぐ）
    AddressableManager --> AddressableHandler : 生成
    AddressableManager --> InitializeController : 生成・Dispose・Tick中継
    AddressableManager --> InitializeStateModel : 生成して注入
    AddressableManager --> CatalogCacheBustingService : Awakeで適用・OnDestroyで復元
    AddressableManager --> LoadingController : ゲートを注入
    AddressableManager --> AddressableRuntimeSettingsSO
    InitializeController ..|> IInitializationGate
    InitializeController --> InitializeStateModel : 状態遷移
    InitializeController --> IAddressablesInitializeService
    InitializeController --> AddressableInitializeSettings
    AddressablesInitializeService ..|> IAddressablesInitializeService
    LoadingController --> IInitializationGate : 読み込んでよいかを問い合わせるだけ
    AddressableRuntimeSettingsSO --> AddressableInitializeSettings
```

- `IInitializationGate` は `Runtime/Core/Interfaces/`（共通物）に置く。Initialize・Loading の両方から参照されるため（initialize-spec 7.4）
- `InitializeStateModel` は `Runtime/Core/Initialize/Model/` に置く。触るのは InitializeController だけで、LoadingController はゲートを通す（initialize-spec 7.2）。`MarkRunning` は `NotStarted` から、`MarkSucceeded` / `MarkFailed` は `Running` からのみ遷移させる（`Succeeded` / `Failed` は終端）
- `IAddressablesInitializeService` は、初期化1回分の開始・進捗・完了の受け渡し（同期の例外も失敗として渡す）・handle の解放を持つ。進捗はアクセサではなく `GetPercentComplete()`（handle の状態確認を伴うため。conventions 2章「アクセサ内で関数を呼ばない」）。完了後は 1 を返す
- 公開イベント（`OnInitialized` / `OnInitializeFailed`）は、Handler が `add` / `remove` で InitializeController の内部イベント（`OnSucceeded` / `OnFailed`）へ直接つなぐ。Handler は購読の状態を持たず、Manager は購読も中継もしない。購読側の例外は Controller が受け止めてログにする（initialize-spec 7.7）
- `AddressableHandler` は `Runtime/Core/Handler/`（namespace `MAH.AddressableManager.Core`）に置く。複数機能の Controller を知るため、機能フォルダの外に置く

## 2. 設定と外部依存型

```mermaid
classDiagram
    class InitializeMode {
        <<enumeration>>
        Auto
        Manual
    }
    class LoadBeforeInitializePolicy {
        <<enumeration>>
        PassThrough
        WaitForInitialize
    }
    class InitializeState {
        <<enumeration>>
        NotStarted
        Running
        Succeeded
        Failed
    }
    class AddressableInitializeException {
        +InnerException : Exception
    }
    AddressableInitializeSettings --> InitializeMode
    AddressableInitializeSettings --> LoadBeforeInitializePolicy
```

- `InitializeState` と `AddressableInitializeException` は利用者が直接触れる外部依存型（namespace `MAH.AddressableManager`）。2つの設定用 enum は内部実装型（`MAH.AddressableManager.Core.Initialize`）
