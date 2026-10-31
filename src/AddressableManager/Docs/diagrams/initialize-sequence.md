# シーケンス図：初期化（Initialize）

> 最終更新日：2026-10-05
> 仕様は [`Docs/specs/initialize-spec.md`](../specs/initialize-spec.md) 2章・5章・7章を参照。

## 1. 自動モード：Awake で始まり、利用者が後から待つ

```mermaid
sequenceDiagram
    participant M as AddressableManager
    participant H as AddressableHandler
    participant B as CatalogCacheBustingService
    participant C as InitializeController
    participant S as AddressablesInitializeService
    participant U as 利用者

    M->>B: Apply()（キャッシュ対策 ON のときのみ）
    M->>C: new（設定・InitializeStateModel・Service を渡す）
    M->>H: new（InitializeController・LoadingController を渡す）
    M->>C: Tick を購読
    Note over M,C: Addressables が初期化済みだと、開始と同時（Awake の中）に完了して<br/>イベントが発火する。利用者の購読は Awake の後になるため、<br/>購読時に AccIsInitialized も確認する
    M->>C: StartAutoInitialize()（結果は待たず、Controller が例外を観測する）
    Note over C: 共有完了を作って NotStarted → Running
    C->>S: StartInitialize(autoReleaseHandle)
    U->>H: await InitializeAsync(progress, ct)
    H->>C: InitializeAsync(progress, ct)
    C-->>H: 実行中の共有完了（新しく始めない）
    loop LateUpdate ごと
        M->>C: Tick(unscaledDeltaTime)
        C->>U: progress.Report(PercentComplete)
    end
    S-->>C: 完了（成功）
    Note over C: 状態 Running → Succeeded・待っている全員に進捗 1
    C->>U: OnSucceeded（Handler の OnInitialized の購読者へ直接）
    C-->>H: 共有完了へ結果を渡す（状態遷移とイベントの後）
    H-->>U: await が戻る
```

## 2. 手動モード：利用者が呼んだ時点で始まる（失敗）

```mermaid
sequenceDiagram
    participant U as 利用者
    participant H as AddressableHandler
    participant C as InitializeController
    participant D as InitializeStateModel
    participant S as AddressablesInitializeService

    Note over D: Awake の後も状態は NotStarted
    U->>H: await InitializeAsync(progress, ct)
    H->>C: InitializeAsync(progress, ct)
    C->>D: MarkRunning(共有完了)
    Note over C,D: 共有完了を先に作って Running にしてから Service を呼ぶ<br/>（同期で完了しても Running を経由する）
    C->>S: StartInitialize(autoReleaseHandle)
    S-->>C: 失敗（操作の失敗、または同期の例外）
    C->>D: MarkFailed(AddressableInitializeException)
    Note over D: 状態 Running → Failed（終端）
    C->>U: OnFailed(ex)（Handler の OnInitializeFailed の購読者へ直接）
    C-->>H: AddressableInitializeException（共有完了の失敗）
    H-->>U: AddressableInitializeException
    U->>H: InitializeAsync()（2回目）
    H->>C: InitializeAsync()
    C->>D: AccState?
    D-->>C: Failed
    Note over C,S: Service は呼ばない（Addressables は初期化をやり直せず、<br/>呼ぶと同期で例外になるため）
    C-->>H: 同じ AddressableInitializeException
    H-->>U: AddressableInitializeException
```

- 失敗は、待っている `InitializeAsync` と `OnInitializeFailed` の両方に届く。イベントは1回だけ発火する（initialize-spec 4章）
- 失敗した後は、Handler の読み込みもゲートで止まる（3章の図の後の注記）

## 3. WaitForInitialize：初期化中の Load は完了を待つ

```mermaid
sequenceDiagram
    participant U as 利用者
    participant L as LoadingController
    participant G as IInitializationGate（InitializeController）

    U->>L: LoadAsync<Sprite>(key, lifetime, ct)
    L->>L: 破棄済み・key の検証
    L->>G: AccIsOpen?
    G-->>L: false（Running）
    L->>G: await WaitUntilOpenAsync(ct)
    Note over L,G: 待っている間は RefCount・チケット・Watcher に何も登録していない
    G-->>L: 初期化成功で完了
    L->>L: 破棄済み・寿命を再確認
    L->>L: 寿命の解決・RefCount 加算・チケット発行（以降は loading-spec 3.1 と同じ）
    L-->>U: AssetTicket<Sprite>
```

- 手動モードで未開始なら `WaitUntilOpenAsync` は `InvalidOperationException`、初期化が失敗していれば `AddressableInitializeException` を投げる（initialize-spec 5章）
- 待機中に Manager が破棄されると、ゲートからは `OperationCanceledException` が届き、LoadingController が `ObjectDisposedException` に変換する。待機中に寿命が終わった（owner の破棄・シーンのアンロード）場合は `OperationCanceledException`（initialize-spec 5章・5.1）
- `PassThrough`（初期化に失敗していないとき）、または初期化に成功済みなら `AccIsOpen` が `true` で、`await` は起きない
- **初期化に失敗した後は、`PassThrough` でも `AccIsOpen` が `false` になり**、`WaitUntilOpenAsync` はすぐに `AddressableInitializeException` を投げる。Addressables は呼ばれず、RefCount・チケット・Watcher にも何も残らない（initialize-spec 5章）

## 4. 状態遷移

```mermaid
stateDiagram-v2
    [*] --> NotStarted
    NotStarted --> Running : 自動モードの Awake／InitializeAsync の最初の呼び出し
    Running --> Succeeded : 初期化に成功
    Running --> Failed : 初期化に失敗（操作の失敗、または同期の例外）
    Succeeded --> [*]
    Failed --> [*]
```

- `Succeeded` / `Failed` は終端。`InitializeStateModel` の遷移メソッドがこれを守る
- `Failed` の後の `InitializeAsync` は、Service を呼ばずに同じ例外を返し続ける（Addressables はアプリを再起動するまで初期化をやり直せないため。initialize-spec 10.1）
- Manager の破棄（`Dispose`）では状態は変化せず、待っている呼び出し元には `OperationCanceledException` が届く
