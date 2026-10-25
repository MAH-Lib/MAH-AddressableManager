# シーケンス図：読み込み・解放（Loading）

> 最終更新日：2026-10-05
> 仕様は [`Docs/specs/loading-spec.md`](../specs/loading-spec.md) 3章・5章・13章を参照。

## 1. 正常系：読み込みと重複ロードの共有

```mermaid
sequenceDiagram
    participant A as 呼び出し元A
    participant B as 呼び出し元B
    participant C as LoadingController
    participant G as IInitializationGate
    participant E as AssetEntryModel
    participant W as AssetLoaderService

    A->>C: LoadAsync<Sprite>(key, Owner(go), ct)
    C->>G: AccIsOpen?
    opt ゲートが閉じている（WaitForInitialize で初期化中など）
        C->>G: await WaitUntilOpenAsync(ct)
        G-->>C: 初期化の完了で再開
        C->>C: 破棄済み・寿命を再確認
    end
    Note over C,G: 確保（RefCount 加算・チケット登録）はこの後。<br/>開いているときは await しない（initialize-spec 5.1）
    C->>E: AddEntry・StartLoad
    E->>W: LoadAsync(_disposeCts.Token)
    C->>C: AddCount・チケット発行（await前）
    B->>C: LoadAsync<Sprite>(key, Manual, ct)
    C->>C: 既存エントリ（同じ key・同じ型）を確認しAddCount
    Note over C,E: 実ロードは1回だけ。A・Bは同じLoadTaskを待つ
    W-->>E: Addressablesロード完了
    E-->>C: LoadTask完了
    C-->>A: AssetTicket<Sprite>
    C-->>B: AssetTicket<Sprite>
    A->>C: Release(ticket)
    C->>C: 発行済みチケットから外してReleaseInternal（RefCount 2→1）
```

A と B は1つの `LoadTask` を共有する。RefCount が 0 になるまで Worker は Dispose されない。初期化ゲートが閉じているときの例外（失敗・未開始・待機中のキャンセル・破棄・寿命の終了）は [`initialize-sequence.md`](initialize-sequence.md) 3章を参照。`LoadByLabelAsync` も、ロケーションの解決より前に同じ位置で待つ。

## 2. キャンセル・失敗

```mermaid
sequenceDiagram
    participant A as 呼び出し元
    participant C as LoadingController
    participant E as AssetEntryModel
    participant W as AssetLoaderService

    A->>C: LoadAsync<T>(key, lifetime, ct)
    C->>C: AddCount・チケット発行
    C->>E: LoadTask.WithCancellation(ct)
    alt ctがキャンセルされた
        E-->>C: OperationCanceledException
        C->>C: チケットを外しReleaseInternal
        Note over C,W: RefCountが0ならRegistryから外し、Workerはロード完了後にDispose
        C-->>A: OperationCanceledException
    else リトライを使い切って失敗
        W-->>E: AssetLoadException
        E->>C: エントリを即座にRegistryから外しWorkerをDispose
        C->>C: チケットを外し巻き戻し
        C-->>A: AssetLoadException
    end
```

キャンセルで止まるのは呼び出し元の待機だけで、実ロードは止めない。失敗したエントリはすぐ消えるので、次の Load では新しく実ロードが始まる。

## 3. 自動回収：Owner の破棄

```mermaid
sequenceDiagram
    participant G as owner(GameObject)
    participant Wt as AssetLifetimeWatcher
    participant M as AddressableManager
    participant C as LoadingController

    G->>Wt: Destroy → OnDestroy
    Wt->>C: _onOwnerDestroyed(scopeId)
    C->>C: ReleaseLifetime(scopeId)
    Note over C: 残っているチケットだけを回収し、返し終えた分にはリーク警告を出す（Editorではスタックトレース付き。ロード中に寿命が終わった分は警告しない）
    M->>C: LateUpdate → OnLateUpdate → Tick(unscaledDeltaTime)
    Note over C: 3秒ごとに_watchedOwnersの偽nullを調べ、Watcherが取りこぼした分を回収する
```

回収の経路は3つ。Watcher が主経路、ポーリングがその保険、Manager の破棄が最終防衛線。Scene 寿命の場合は、Manager が中継した `sceneUnloaded` を `HandleSceneUnloaded` が受け取り、`ReleaseLifetime` を呼ぶ。

## 4. 正常系：Label 一括ロードと解放

```mermaid
sequenceDiagram
    actor U as 利用者コード
    participant C as LoadingController
    participant L as LabelLocationService
    participant R as AssetRegistryModel
    participant T as IssuedTicketRegistryModel
    participant F as IAssetLoaderServiceFactory

    U->>C: LoadByLabelAsync(label, lifetime, ct)
    C->>L: ResolveAsync(label, 呼び出しごとのList, ct)
    L-->>C: (PrimaryKey, ResourceType)[]（Object以外を除外・重複を除外）
    C->>C: 寿命を解決（確保の直前）・AssetGroupTicket を生成
    loop 組ごと（同期で確保）
        C->>R: TryGetEntry((key, 型))
        alt 未登録
            C->>F: Create(key, 型)
            C->>R: AddEntry・StartLoad
        end
        C->>T: AddCount・Register(serial, entryKey, entry, scope, group)
    end
    C->>C: 全件の LoadTask を待機（最初の失敗で打ち切る）
    C-->>U: AssetGroupTicket
    U->>U: GetAssets<Sprite>() / TryGetAsset<AudioClip>(address)
    U->>C: Release(group)
    C->>C: group.Invalidate()
    loop 通し番号ごと
        C->>T: TryRemoveTicket(serial) → record
        C->>C: ReleaseInternal(record)
    end
```

確保（同期）で全件の通し番号が先にそろい、待機はその後に行う。解放は、1件ずつの解放と同じ `ReleaseInternal` を通る。Label で読んだ Sprite は、単体の `LoadAsync<Sprite>` と同じエントリを共有する。

## 5. 異常系：Label 一括ロードの失敗・キャンセル・寿命の終了

```mermaid
sequenceDiagram
    actor U as 利用者コード
    participant C as LoadingController
    participant E as 各エントリの LoadTask
    participant RL as ReleaseLifetime

    U->>C: LoadByLabelAsync(label, lifetime, ct)
    C->>C: 全件を同期で確保（全通し番号が確定）
    C->>E: 全件を待機（最初の失敗で打ち切る）
    alt ctがキャンセルされた
        C->>C: 全通し番号を ReleaseBySerial で巻き戻す
        C-->>U: OperationCanceledException
    else ロード中に寿命が終わった
        RL->>C: ReleaseLifetime(scope)：全件を回収し、group を無効化、リーク警告は1件
        E-->>C: 待機が完了
        C->>C: チケットが残っていない → 残りを巻き戻す
        C-->>U: OperationCanceledException（寿命が終了）
    else いずれかが失敗した
        E-->>C: AssetLoadException（最初の1件）
        C->>C: 残りを待たずに全通し番号を巻き戻す
        Note over C,E: 失敗したエントリは削除済み。巻き戻しは、記録のエントリが Registry のものと同じ場合だけ取り除く
        C-->>U: AssetLoadException
    else 該当0件
        C-->>U: 警告ログ＋空の AssetGroupTicket
    end
```

例外の優先順位は、キャンセル、寿命の終了、ロードの失敗の順（仕様書13.10）。ロード中のエントリは、RefCount が 0 になると Registry から外れ、Worker はロード完了後に解放される。
