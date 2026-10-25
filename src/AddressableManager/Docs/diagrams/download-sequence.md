# シーケンス図：ダウンロード（Download）

> 最終更新日：2026-10-08
> 仕様は [`Docs/specs/download-spec.md`](../specs/download-spec.md) 2章・5章・7章を参照。

## 1. 更新の確認 → 確認ダイアログ → ダウンロード（正常系）

### 1.1 更新の確認（`CheckUpdateAsync`）

```mermaid
sequenceDiagram
    participant U as 利用者
    participant H as AddressableHandler
    participant C as DownloadController
    participant G as IInitializationGate（InitializeController）
    participant D as DownloadStatusModel
    participant S as AddressablesDownloadService

    U->>H: await CheckUpdateAsync(ct)
    H->>C: CheckUpdateAsync(null, ct)
    Note over C: null は SO の Label に解決する。空なら InvalidOperationException（状態は変えない）
    C->>D: TryBegin(CheckingCatalog, null)
    Note over D: 実行中なら false で、C が InvalidOperationException。成功すると値を 0 に戻す
    C->>G: await WaitUntilInitializedAsync(ct)
    G-->>C: 初期化の成功
    C->>S: CheckForCatalogUpdatesAsync()
    S-->>C: 更新のあるカタログの ID
    opt CheckAndUpdate かつ更新あり
        C->>D: MarkPhase(UpdatingCatalog)
        C->>S: UpdateCatalogsAsync(確認で得た ID, autoClean)
    end
    C->>D: MarkPhase(CalculatingSize)
    C->>S: CalculateDownloadSizeAsync(labels)
    S-->>C: DownloadSizeInfo（バンドル数・バイト数）
    C->>D: MarkCompleted()
    C->>D: End()
    C-->>H: DownloadCheckResult
    H-->>U: DownloadCheckResult
```

- 方針が `None` のときは、カタログの確認と更新を飛ばし、最初の段階が `CalculatingSize` になる。`CheckOnly` では更新（`UpdateCatalogsAsync`）を呼ばない（download-spec 2.1）
- `UpdateCatalogsAsync` には、確認で得たカタログ ID を明示して渡す（`null` を渡すと、別の確認で立ったフラグの分まで更新するため。download-spec 6章）
- 利用者は、戻り値の件数と容量を確認ダイアログに出して、同意されたら 1.2 の `DownloadAsync` を呼ぶ

### 1.2 ダウンロードと進捗（`DownloadAsync`）

```mermaid
sequenceDiagram
    participant U as 利用者
    participant H as AddressableHandler
    participant C as DownloadController
    participant D as DownloadStatusModel
    participant S as AddressablesDownloadService
    participant M as AddressableManager

    U->>H: await DownloadAsync(progress, ct)
    H->>C: DownloadAsync(null, progress, ct)
    C->>D: TryBegin(CalculatingSize, progress)
    Note over D: 通知先を置き換え、開始を1回通知する
    Note over C: 初期化の待機（1.1 と同じ）の後
    C->>S: CalculateDownloadSizeAsync(labels)
    S-->>C: DownloadSizeInfo
    C->>D: SetBytes(0, 合計バイト数)
    alt ダウンロードが必要なバンドルが 0 件
        C->>D: MarkCompleted()
    else 1 件以上
        C->>D: MarkPhase(Downloading)
        C->>S: StartDownloadAsync(labels)
        loop LateUpdate ごと（Downloading の間だけ）
            M->>C: Tick(unscaledDeltaTime)
            C->>S: GetDownloadBytes()
            C->>D: SetBytes(ダウンロード済み, 合計)
            D->>U: progress.Report(DownloadStatus のコピー)
        end
        S-->>C: 完了（handle は Service が解放）
        C->>D: MarkCompleted()
    end
    D->>U: progress.Report(Completed)（最後の通知。通知先を外す前）
    C->>D: End()
    C-->>H: 完了
    H-->>U: await が戻る
```

- `Tick` は `Downloading` のときだけ働く。ダウンロードしていないときは、先頭で戻り、通知もしない（download-spec 7.6）
- 通知で届く値は、その時点のコピー（`readonly struct`）。後から変わらない。別の UI は、毎回 `AccDownloadStatus` を取得し直せば、同じ値を読める（download-spec 4.1）
- **最後の通知（`Completed` / `Failed` / `Canceled`）は、通知先を外す前に1回だけ届く**。順序は、結果の記録 → `Report` → `End` → 呼び出し元へ完了・例外（download-spec 7.3）
- `DownloadAsync` は、バンドルをキャッシュに保存するだけで、カタログは更新しない。完了したら handle をすぐ解放し、アセットは読み込まない（download-spec 1章・7.5）

## 2. 更新の有無の判定（`HasUpdateAsync`）

```mermaid
sequenceDiagram
    participant U as 利用者
    participant H as AddressableHandler
    participant C as DownloadController
    participant G as IInitializationGate（InitializeController）
    participant D as DownloadStatusModel
    participant S as AddressablesDownloadService

    U->>H: await HasUpdateAsync(ct)
    H->>C: HasUpdateAsync(null, ct)
    C->>D: TryBegin(最初の段階, null)
    Note over C,D: 方針が None なら CalculatingSize、それ以外は CheckingCatalog
    C->>G: await WaitUntilInitializedAsync(ct)
    opt 方針が None 以外
        C->>S: CheckForCatalogUpdatesAsync()
        S-->>C: 更新のあるカタログの ID
    end
    alt 更新のあるカタログがある
        Note over C: サイズを計算せずに true（通信が .hash の取得だけで済む）
    else 更新のあるカタログが無い
        C->>S: CalculateDownloadSizeAsync(labels)
        S-->>C: DownloadSizeInfo
        Note over C: バンドルが 1 件以上なら true、0 件なら false
    end
    C->>D: MarkCompleted()
    C->>D: End()
    C-->>U: bool
```

- **どの方針でも `UpdateCatalogs` は呼ばない**（download-spec 2.2）。カタログの確認だけでは、読み込みに使うカタログは書き換わらない（download-spec 6章）
- カタログを更新したが、まだダウンロードしていない状態でも、サイズの計算（今のカタログのバンドルを数える）で `true` になる

## 3. 失敗・リトライ・キャンセル・初期化の待機

### 3.1 失敗とリトライ（リトライを使い切る場合）

```mermaid
sequenceDiagram
    participant U as 利用者
    participant C as DownloadController
    participant D as DownloadStatusModel
    participant S as AddressablesDownloadService

    C->>S: StartDownloadAsync(labels)
    S-->>C: 失敗（通信エラー。Service が handle を解放）
    loop attempt が maxRetry 未満（段階ごとに数える）
        C->>D: IncrementRetry()
        D->>U: progress.Report（AccRetryCount が 1 増える）
        C->>C: AsyncCompat.Delay(retryDelayMs, ct)（実時間で待つ）
        C->>S: StartDownloadAsync(labels)
        S-->>C: 失敗（成功すればループを抜けて完了へ）
    end
    C->>D: MarkFailed()
    D->>U: progress.Report(Failed)（最後の通知）
    C->>D: End()
    C-->>U: AddressableDownloadException（段階・リトライ回数・元の例外）
```

- リトライは、カタログの確認・更新・サイズ計算・ダウンロードの各段階で、同じ流れ。リトライ回数は段階ごとに数え、段階が進むと `AccRetryCount` は 0 に戻る（download-spec 2.5）
- 失敗の例外は `DownloadStatus` には持たせず、呼び出し元の `await` に返す。受け取る側は、最後の通知の `AccPhase == Failed` で失敗を判定する

### 3.2 キャンセルと `Dispose`

```mermaid
sequenceDiagram
    participant U as 利用者
    participant C as DownloadController
    participant D as DownloadStatusModel
    participant S as AddressablesDownloadService
    participant M as AddressableManager

    Note over U,S: ct のキャンセル
    U->>C: ct をキャンセル
    C->>S: ReleaseDownloadHandle()（開始済みの通信は止まらない場合がある）
    C->>D: MarkCanceled()
    D->>U: progress.Report(Canceled)（最後の通知）
    C->>D: End()
    C-->>U: OperationCanceledException

    Note over U,S: カタログの更新中（UpdatingCatalog）の ct のキャンセル
    U->>C: ct をキャンセル
    Note over C,S: 待機は打ち切らない（更新は途中で止められないため）
    S-->>C: 更新が完了
    C->>D: MarkCanceled()（サイズ計算は始めない。カタログは更新済み）
    C->>D: End()
    C-->>U: OperationCanceledException

    Note over U,M: Manager の破棄（Dispose）
    M->>C: Dispose()
    C->>C: _disposeCts をキャンセル（待機中の呼び出し元を止める）
    C->>D: MarkCanceled()（実行中のときだけ）
    D->>U: progress.Report(Canceled)
    C->>D: End()
    C->>S: ReleaseDownloadHandle()
    C-->>U: OperationCanceledException（待機中の呼び出し元）
```

- **`UpdatingCatalog` 中は、`ct` のキャンセルで待機を打ち切らず、更新の完了後に `Canceled` で終える**。Addressables の更新は途中で止められず、待機だけをやめて `Canceled` を返すと、返した後に裏でカタログが更新されるため（download-spec 2.5）。`Dispose` では、更新の完了を待たずに終わる。更新が失敗してリトライを待っている間は、`ct` ですぐ打ち切れる
- `Dispose` は、継続の実行順に頼らず、`Canceled` の通知と `End` を自分で行う。後から再開した継続側の `MarkCanceled` / `End` は、何も変えずに戻る（Model が、結果の記録後の遷移を無視するため。download-spec 7.2）
- `Dispose` の順序は、Loading → Download → Initialize（Download は初期化の待機を持つため、Initialize より先に止める。download-spec 2.5）
- 破棄後の呼び出しは `ObjectDisposedException`

### 3.3 初期化の待機で失敗する場合

```mermaid
sequenceDiagram
    participant U as 利用者
    participant C as DownloadController
    participant D as DownloadStatusModel
    participant G as IInitializationGate（InitializeController）

    U->>C: DownloadAsync / CheckUpdateAsync / HasUpdateAsync
    C->>D: TryBegin(最初の段階, progress)
    C->>G: await WaitUntilInitializedAsync(ct)
    alt 手動モードで初期化が未開始
        G-->>C: InvalidOperationException（初期化は始めない）
    else 初期化に失敗済み
        G-->>C: AddressableInitializeException
    else 待機中に ct をキャンセル
        G-->>C: OperationCanceledException
    end
    C->>D: MarkFailed()（キャンセルなら MarkCanceled()）
    D->>U: progress.Report(Failed または Canceled)
    C->>D: End()
    C-->>U: 初期化の例外をそのまま返す（AddressableDownloadException に包まない）
```

- この場合、Service は呼ばれない。待機中の段階は、その処理の最初の段階（`AccIsBusy` が `true`）で、前の処理の結果が残らない（download-spec 5章）

## 4. 段階の遷移

```mermaid
stateDiagram-v2
    [*] --> None
    None --> CheckingCatalog : TryBegin（確認・判定。方針が None 以外）
    None --> CalculatingSize : TryBegin（方針が None の確認・判定、ダウンロード）
    CheckingCatalog --> UpdatingCatalog : CheckAndUpdate で更新のあるカタログあり
    CheckingCatalog --> CalculatingSize : 更新の適用が不要
    CheckingCatalog --> Completed : 判定でカタログに更新あり
    UpdatingCatalog --> CalculatingSize
    CalculatingSize --> Downloading : DownloadAsync でバンドルあり
    CalculatingSize --> Completed : 確認・判定、またはバンドル 0 件
    Downloading --> Completed
    Downloading --> Failed : リトライを使い切った
    Downloading --> Canceled : ct のキャンセル、Dispose
    Completed --> CheckingCatalog : 次の処理の TryBegin
```

- `Completed` / `Failed` / `Canceled` は、**次の処理が始まるまで残る**（`None` に戻らない）。次の `TryBegin` で、最初の段階になり、バイト数とリトライ回数が 0 に戻る。`Failed` / `Canceled` からも同じ（download-spec 2.4）
- `Failed` と `Canceled` へは、処理中の4つの段階（`CheckingCatalog` / `UpdatingCatalog` / `CalculatingSize` / `Downloading`）のどこからでも遷移する（図は `Downloading` だけを描いている）。初期化の待機での失敗・キャンセルも同じ
- `None` は、一度も処理していない間だけ。処理中かどうかは `AccIsBusy`、ダウンロード中かどうかは `AccIsDownloading` で判定する
