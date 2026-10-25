# 機能仕様書：初期化（Initialize）

> 最終更新日：2026-10-08 ／ 対象 Unity バージョン：6000.3.26f1
> **この機能（Addressables の初期化）の仕様はこのファイルを正とする**。全体の概要・他機能との関係は [`addressable-manager-design.md`](../addressable-manager-design.md) 3.2 を参照。命名パターンは [`conventions.md`](../conventions.md)、用語は `Docs/glossary.md`（リポジトリルート）が正。
> 図は [`diagrams/initialize-class.md`](../diagrams/initialize-class.md)・[`diagrams/initialize-sequence.md`](../diagrams/initialize-sequence.md) を参照。
>
> 本書は、初期化機能が**どう動き、どう作られているか**、そして**改修するときにどこを触ればよいか**を、後から読む開発者が把握するための資料である。読み込み・解放（Loading）の仕組みは [`loading-spec.md`](loading-spec.md) を前提とする。

---

## 1. 概要

`AddressableManager` が生成する初期化機能が `Addressables.InitializeAsync` を呼び、その進行状態（未開始・実行中・成功・失敗）を管理する機能。

- 初期化を**自動**で行うか**手動**で行うかを、Runtime SO（`AddressableRuntimeSettingsSO`）で選べる。自動なら Manager の `Awake` で初期化が始まる
- 初期化前の Load の扱い・`autoReleaseHandle`・カタログの URL キャッシュ対策を、同じ SO で設定する
- 完了・失敗は、`await` できる `InitializeAsync` と、イベント（`OnInitialized` / `OnInitializeFailed`）の両方で知らせる。**これらの公開 API はすべて `IAddressableHandler` に置く**（利用者が触れる入口を1つに絞るため。Manager は公開しない）
- **初期化の失敗は致命的なエラーとして扱う**。Addressables は失敗した初期化をアプリの再起動までやり直せず（10.1）、失敗の後は Addressables 全体が使えない。そのためリトライ・やり直しは持たず、失敗した後は `IAddressableHandler` の読み込みも止めて `AddressableInitializeException` を返す（5章）。利用者は失敗を受けて、再起動を促す等の対応をする

**この機能が持たないもの**：カタログ更新・依存ダウンロードは DownloadController の担当（設計仕様書 3.3・[`download-spec.md`](download-spec.md)）。初期化の後にカタログ更新を続けて行う「起動シーケンス」は、Manager に持たせない（2026-10-08 に決定。容量の確認ダイアログを挟めなくなるため。9.3）。DownloadController は、初期化の成功を `IInitializationGate` を介して待ってから動く（7.4）。Handler は各 Controller へ転送するだけで、順序を決めるロジックは持たない。

### 1.1 全体の中での位置づけ

```
AddressableManager（合成ルート。機能内クラスの生成・寿命管理・ライフサイクルの中継。公開は AccHandler のみ）
├─ AddressableHandler    … 利用者の唯一の入口（IAddressableHandler）。初期化の API を InitializeController へ転送する
├─ InitializeController  … 本書。初期化の実行と状態遷移
│    ├─ InitializeStateModel … 初期化の状態・失敗の例外・共有タスク（Manager が生成して注入する）
│    └─ Service           … Addressables.InitializeAsync の呼び出し／URL キャッシュ対策
└─ LoadingController     … 読み込み・解放（loading-spec.md）
     └─ IInitializationGate を通して「読み込んでよいか」だけを問い合わせる（InitializeController は知らない）
```

- InitializeController と LoadingController は**互いを参照しない**（設計の絶対ルール）。Load が初期化を待つ仕組みは、`Core/` 直下の共通インターフェース `IInitializationGate` を介して作る（7.4）
- 既定（`PassThrough`）では Loading は初期化を待たない。Addressables は最初のロード時に自動で初期化されるため、これまでどおり `InitializeAsync` を呼ばなくても読み込みは動く。ただし初期化が失敗した後は、設定に関係なく読み込みを止める（5章）

---

## 2. 動作の全体像

### 2.1 自動モード（`InitializeMode.Auto`）

1. Manager の `Awake` で、URL キャッシュ対策を設定し（ON の場合。6章）、InitializeController と Handler を生成して、`Tick`（7.6）を購読する
2. 続けて Manager が InitializeController の `StartAutoInitialize` を呼び、初期化を始める。結果を待つ利用者はいないため、例外は Controller の中で記録・ログ化し、未観測の例外にはしない（7.3）。**`Tick` の購読は、この自動開始より前に行う**。また、Addressables が初期化済みだと、開始と同時に（`Awake` の中で）完了してイベントが発火するため、利用者が Handler で購読する時点では完了済みのことがある（取りこぼしは `AccIsInitialized` で確認する。4.1・10.1）
3. 完了すると状態が `Succeeded`（または `Failed`）になり、InitializeController がイベントを発火する（Handler の `OnInitialized` / `OnInitializeFailed` の購読者に届く。7.7）
4. 利用者は、任意のタイミングで `await AddressableManager.AccInstance.AccHandler.InitializeAsync()` を呼べば、**実行中の初期化をそのまま待てる**（新しく初期化を始めることはない）

### 2.2 手動モード（`InitializeMode.Manual`）

1. Manager の `Awake` で、URL キャッシュ対策を設定し（ON の場合）、InitializeController と Handler を生成して、`Tick` を購読する。**初期化は始めない**（状態は `NotStarted`）
2. 利用者が Handler の `InitializeAsync` を呼んだ時点で初期化が始まる。2回目以降の呼び出しは、実行中または完了済み（成功・失敗とも）の同じ処理を共有する

シーケンス図は [`diagrams/initialize-sequence.md`](../diagrams/initialize-sequence.md) 1〜2章を参照。

### 2.3 状態と遷移

状態は `InitializeState`（外部依存型。`MAH.AddressableManager`）で表し、`InitializeStateModel`（7.2）が保持する。

| 状態 | 意味 |
|---|---|
| `NotStarted` | まだ初期化を始めていない（手動モードで `InitializeAsync` が呼ばれる前） |
| `Running` | 初期化中 |
| `Succeeded` | 初期化に成功した。以降は変化しない |
| `Failed` | 初期化に失敗した。**以降は変化しない**（Addressables はアプリを再起動するまで初期化をやり直せないため。10.1） |

| 遷移 | 条件 |
|---|---|
| `NotStarted` → `Running` | 自動モードの `Awake`、または `InitializeAsync` の最初の呼び出し |
| `Running` → `Succeeded` | `Addressables.InitializeAsync` の操作が成功した |
| `Running` → `Failed` | `Addressables.InitializeAsync` の操作が失敗した、または呼び出しが同期で例外を投げた |

- `Succeeded` / `Failed` からは遷移しない。この制約は `InitializeStateModel` の遷移メソッドが守る（Controller が誤って遷移させようとしても状態は変わらない）
- `Failed` になった後、InitializeController は **`Addressables.InitializeAsync` を二度と呼ばない**。呼ぶと Addressables が `ArgumentOutOfRangeException` を同期で投げるため（10.1）。以降の `InitializeAsync` は、失敗した共有タスクをそのまま返す

状態遷移図は [`diagrams/initialize-sequence.md`](../diagrams/initialize-sequence.md) 4章を参照。

- Manager の破棄（`OnDestroy`）で InitializeController は `Dispose` され、`InitializeAsync` の実行中の待機は `OperationCanceledException` で終わる。破棄後の状態は変化しない
- `OnDestroy` での順序は、**イベントの購読解除 → LoadingController の `Dispose` → InitializeController の `Dispose` → URL 変換関数の復元（6.4）**。Loading を先に止めることで、初期化のキャンセルで再開した Load が、破棄済みの判定に確実に引っかかる（5章）

---

## 3. 設定項目（`AddressableInitializeSettings`）

`Runtime/Core/Initialize/Data/AddressableInitializeSettings.cs`（`[Serializable]`。namespace `MAH.AddressableManager.Core.Initialize`）。Runtime SO が `_initialize` として保持し、Inspector では「初期化」の見出しの下に表示される。`AddressableLoadingSettings` と同じ作り（Unity のシリアライズ用の引数なしコンストラクタ、テスト用の値指定コンストラクタ）にしている。範囲のある値を持たないため `ClampValues()` は無い（9.1）。

| 項目 | 型 | 既定値 | 意味 |
|---|---|---|---|
| `_mode` | `InitializeMode` | `Auto` | `Auto`：Manager の `Awake` で初期化を始める／`Manual`：`InitializeAsync` が呼ばれるまで始めない |
| `_loadBeforeInitialize` | `LoadBeforeInitializePolicy` | `PassThrough` | 初期化が成功する前に Load が呼ばれたときの扱い（5章） |
| `_autoReleaseHandle` | `bool` | `true` | `Addressables.InitializeAsync(autoReleaseHandle)` に渡す値。`false` のときは Controller が handle を保持し、`Dispose` で解放する（7.5） |
| `_enableCatalogCacheBusting` | `bool` | `false` | リモートカタログの URL キャッシュ対策を有効にする（6章） |

- 2つの enum（`InitializeMode` / `LoadBeforeInitializePolicy`）は Definition 層で、同じ `Data/` フォルダに置く。SO の設定でしか使わないため内部実装型（`MAH.AddressableManager.Core.Initialize`）
- **リトライ・タイムアウト・失敗後のやり直しの設定は持たない**（8章）。カタログ取得の Web リクエストのタイムアウトは、Addressables 側の設定（AddressableAssetSettings の Catalog のリクエストタイムアウト。実行時は `Addressables.CatalogRequestsTimeout`）で行う。ネットワーク起因の再試行は DownloadController が持つ（設計仕様書 3.3）

---

## 4. 公開 API と使い方

`IAddressableHandler`（`MAH.AddressableManager`。`Runtime/Core/Interfaces/`）に以下を公開する。Manager には公開しない。利用者は `AddressableManager.AccInstance.AccHandler` から取得する。

```csharp
public interface IAddressableHandler
{
    // 読み込み・解放のメンバー（LoadAsync / Release / LoadByLabelAsync）は loading-spec.md 8章。ここでは初期化のメンバーだけを示す
    /// <summary> 初期化の状態 </summary>
    InitializeState AccInitializeState { get; }
    /// <summary> 初期化に成功しているか </summary>
    bool AccIsInitialized { get; }

    /// <summary> 初期化に成功したときに呼ばれる </summary>
    event Action OnInitialized;
    /// <summary> 初期化に失敗したときに呼ばれる（1回だけ。失敗した後は初期化をやり直せない） </summary>
    event Action<AddressableInitializeException> OnInitializeFailed;

    /// <summary> 初期化を行う。実行中・成功済みなら、その処理を共有して待つ </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
    UniTask InitializeAsync(IProgress<float> progress = null, CancellationToken ct = default);
#else
    Task InitializeAsync(IProgress<float> progress = null, CancellationToken ct = default);
#endif
}
```

| 呼んだときの状態 | `InitializeAsync` の動作 |
|---|---|
| `NotStarted` | 初期化を始め、完了まで待つ |
| `Running` | 実行中の初期化を待つ（新しく始めない） |
| `Succeeded` | すぐに完了する |
| `Failed` | すぐに `AddressableInitializeException` を投げる（初期化をやり直さない。毎回同じ例外） |

- **失敗は `await` とイベントの両方で知らせる**。`Failed` になったとき、待っている `InitializeAsync` すべてに `AddressableInitializeException` が届き、同時に `OnInitializeFailed` が1回発火する。自動モードで待っている利用者がいなくても、イベントは発火する
- 例外は `AddressableInitializeException`（外部依存型。`Runtime/Core/AddressableInitializeException.cs`）。Addressables の例外（操作の `OperationException`、または `Addressables.InitializeAsync` が同期で投げた例外）を `InnerException` に入れる
- **`ct` は呼び出し元の待機だけを打ち切る**。キャンセルしても初期化そのものは止まらず、他の呼び出し元や自動モードの初期化には影響しない（7.3）
- **`progress`** には、待っている間の進捗（0〜1。Addressables の `PercentComplete`）が届く。後から合流した呼び出し元にも、合流した時点以降の進捗が届く。完了時には必ず 1 が届く（成功済みのときに呼んだ場合も、すぐに 1 が届く）。失敗・キャンセルのときは 1 は届かない
- イベントは Handler への購読を通して InitializeController から発火され、Addressables の完了通知と同じメインスレッドで呼ばれる

### 4.1 利用例

```csharp
// 自動モード：起動シーンの処理で、初期化の完了を待ってから次へ進む
private async Task RunSetupAsync(CancellationToken ct)
{
    try
    {
        await AddressableManager.AccInstance.AccHandler.InitializeAsync(_progressView, ct); // 実行中の自動初期化を待つだけ
    }
    catch (AddressableInitializeException ex)
    {
        // 初期化はアプリを再起動するまでやり直せず、以降の Load もすべて失敗する。再起動を促す
        ShowFatalErrorDialog(ex);
        return;
    }
    LoadTitleScene();
}

// 手動モード：利用規約の同意後など、任意のタイミングで初期化を始める
await AddressableManager.AccInstance.AccHandler.InitializeAsync(ct: ct);

// イベントで受け取る：購読より前に完了している場合があるため、状態も確認する
private void Start()
{
    var handler = AddressableManager.AccInstance.AccHandler;
    if (handler.AccIsInitialized) { HandleInitialized(); return; }
    handler.OnInitialized += HandleInitialized;
}

private void OnDestroy()
{
    if (AddressableManager.AccInstance != null)
        AddressableManager.AccInstance.AccHandler.OnInitialized -= HandleInitialized; // 購読は必ず対で解除する
}
```

**イベントの取りこぼしに注意**：自動モードでは `Awake` で初期化が始まり、利用者がイベントを購読する前に終わっている場合がある（Addressables が既に初期化済みなら、`Awake` の中で同期的に完了する。10.1）。イベントで受け取る場合は、上の例のように購読時に `AccIsInitialized`（失敗なら `AccInitializeState`）を確認する。取りこぼしを気にせず書きたい場合は `await InitializeAsync()` を使う。

---

## 5. 初期化前・失敗後の Load の扱い

`_loadBeforeInitialize` で、初期化が成功する前に `LoadAsync` / `LoadByLabelAsync` が呼ばれたときの動作を選ぶ。初期化が失敗した後は、設定に関係なく読み込みを止める。

| 初期化の状態 | `PassThrough`（既定） | `WaitForInitialize` |
|---|---|---|
| `NotStarted`（手動モード） | そのまま読み込む（Addressables が自動で初期化する） | **`InvalidOperationException`**（`InitializeAsync` の呼び忘れにすぐ気づけるようにする） |
| `Running` | そのまま読み込む | 初期化の完了を待ってから読み込む |
| `Succeeded` | そのまま読み込む | そのまま読み込む（待たない） |
| `Failed` | **`AddressableInitializeException`**（Addressables を呼ばない） | **`AddressableInitializeException`**（同左） |

- 自動モードでは `Awake` の時点で `Running` になるため、`NotStarted` の行は手動モードでのみ起きる
- **`Failed` は両方の設定で同じ扱い**。初期化に失敗した後は Addressables 全体が使えず（10.1）、読み込みを試させても Addressables のエラー（キーが無効）で失敗するだけで、原因が初期化の失敗だと分からない。特に `LoadByLabelAsync` は、エラーにならず「該当なし」の Warning と空のまとめチケットになり、失敗に気づけない恐れがある。そのため Addressables を呼ぶ前に止め、初期化の失敗をそのまま伝える。Load から初期化をやり直すことはしない
- 止まるのは読み込みだけで、`Release` は止めない（失敗の前に受け取ったチケットを解放できるようにするため）
- `WaitForInitialize` で待っている間に `ct` がキャンセルされたら `OperationCanceledException`、Manager が破棄されたら `ObjectDisposedException`
  - 破棄されると、ゲートの待機には共有タスクの `OperationCanceledException` が届く（2.3）。LoadingController が `catch` で破棄済み（`_isDisposed`）を確認し、`ObjectDisposedException` に変換して投げる。Load の呼び出し元から見て、破棄後の Load と同じ例外にそろえるため
- 待っている間に寿命が終わった（owner が破棄された・シーンがアンロードされた）ら `OperationCanceledException`（「ロード中に寿命が終了しました」。loading-spec の、ロード中に寿命が終わった場合と同じ扱い）

### 5.1 Load のどの時点で待つか

`LoadAsync` の手順（loading-spec 3.1）のうち、**入力の検証（手順1〜2）の後、寿命の解決（手順3）と RefCount の加算（手順5）より前**で待つ。`LoadByLabelAsync`（loading-spec 13.2）では、手順1の検証の後、ロケーションの解決（手順2）より前で待つ。

- RefCount の加算・チケットの発行・Owner の Watcher 登録は、すべて待機の後に行う。待っている間にキャンセル・破棄されても、巻き戻すものが何もない
- 待機の後で、もう一度破棄済みを確認する（`await` を挟むため）
- 待機の後の寿命の解決では、owner の破棄・シーンのアンロードを**引数の誤り（`ArgumentException`）として扱わない**。利用者は呼び出しの時点では有効な寿命を渡しており、引数の誤りとして知らせると原因を取り違えるため。待機の前に寿命が有効か（owner が破棄されていないか・シーンがロード済みか）を確認しておき、待機の後に無効になっていれば `OperationCanceledException` を投げる。待機の前から無効なら、これまでどおり `ArgumentException`
- ゲートが開いているとき（`PassThrough` で `Failed` 以外、または `Succeeded`）は `await` を挟まず、確保もしない。初期化後の Load のコストは変わらない（bool を1つ見るだけ）
- `Failed` でゲートが閉じているときも同じ位置で止まるため、RefCount・チケット・Watcher には何も残らない

---

## 6. カタログの URL キャッシュ対策

### 6.1 解決する問題

iOS / macOS では、OS の URL キャッシュによって、リモートのカタログ（`.hash`）を取りに行っても古い内容が返ることがある。`.hash` が古いままだと、サーバー側でカタログを更新しても「更新なし」と判定され、新しいコンテンツを取得できない。

### 6.2 動作

`_enableCatalogCacheBusting` が ON のとき、`Addressables.InternalIdTransformFunc` に変換関数を設定し、リモートカタログの `.hash` の URL に**要求ごとに異なるクエリ**を付ける。URL が毎回異なるため、OS のキャッシュが使われない。

変換の規則：
- 対象は、InternalId が **`http` で始まり**（`https` を含む）、**`?` より前のパス部分（`?` が無ければ全体）が `.hash` で終わる**もの。それ以外（ローカルのカタログ・アセットバンドル・カタログ本体等）は変換しない。CDN の署名トークン等のクエリが付いた `.hash`（`…/catalog.hash?token=abc`）も対象になる。クエリの中に `.hash` があるだけのもの（`…/data.json?v=catalog.hash`）は対象外
- 付けるのは `?t=<起動時の値>-<連番>`。InternalId に既に `?` が含まれていれば `&t=<起動時の値>-<連番>`
- 起動時の値は Manager の起動時に1回だけ作る（`DateTime.UtcNow.Ticks`）。連番は変換するたびに1つ進める
- **対象を `.hash` に絞るのは**、カタログ本体（`.bin`、JSON 形式の設定なら `.json`）の URL が、変換後の `.hash` の URL を `.Replace(".hash", ".bin")` して作られるため（Addressables 2.9.1 の `ContentCatalogProvider`）。`.hash` にクエリを付ければ、本体にも同じクエリが自動で付く。`.bin` / `.json` まで対象にすると、利用者がリモートに置いた JSON 等のアセットまで毎回キャッシュを外してしまう
- **要求ごとに値を変えるのは**、カタログ更新の確認が起動時だけでなくゲーム内からも行われるため（DownloadController。設計仕様書 3.3）。セッションで同じ値を使うと、2回目以降の確認で OS のキャッシュが効き、古い `.hash` が返る。本体の URL は `.hash` の変換結果から作られるので、値が要求ごとに変わっても `.hash` と本体の組み合わせは崩れない

例：`https://cdn.example.com/Android/catalog_1.0.hash` → `https://cdn.example.com/Android/catalog_1.0.hash?t=638634528000000000-1`（このとき取得される本体は `…/catalog_1.0.bin?t=638634528000000000-1`）
署名付き URL の例：`https://cdn.example.com/Android/catalog_1.0.hash?token=abc` → `…/catalog_1.0.hash?token=abc&t=638634528000000000-1`（本体は `…/catalog_1.0.bin?token=abc&t=638634528000000000-1`）

### 6.3 設定するタイミング

**`Awake` で、モードに関わらず設定する**（重複した Manager の確認の後、InitializeController の生成より前）。重複した Manager（2つ目以降）は `Destroy` して戻るため、変換関数を設定しない。確認より前に設定すると、重複した Manager の分だけ変換関数が二重に連結される。手動モード＋`PassThrough` では、`InitializeAsync` より前に Load されると Addressables が自動で初期化される。初期化の時点で変換関数が設定されていないと、カタログが変換なしで取得されてしまうため、初期化の方法に関係なく先に設定しておく。

### 6.4 既存の変換関数との共存

`Addressables.InternalIdTransformFunc` は Addressables 全体で1つの delegate であり、利用者のプロジェクトが別の目的（URL の差し替え等）で設定していることがある。

- 既に設定されている関数は**上書きせず連結する**。先に既存の関数を通し、その結果に対して上記の規則でクエリを付ける
- Manager の破棄（`OnDestroy`）時に、設定前の関数へ戻す。ただし戻すのは、`InternalIdTransformFunc` が**自分の設定した関数のままのときだけ**。利用者が後から別の関数に上書きしていた場合は触らず、Warning を出す（そのまま戻すと利用者の関数が消えるため。Warning は、上書きした関数がこの関数を呼んでいない場合に、キャッシュ対策が途中から外れていたことを知らせる）
  - 上書きされていたときは、設定前の関数を**保持したまま**にする。利用者の関数がこの関数を内部で呼ぶ作り（`var prev = func; func = loc => Modify(prev(loc));`）だと、戻した後もこの関数は呼ばれ続ける。そのとき設定前の関数を捨てると、`Awake` より前に設定されていた元の関数が通らなくなるため。戻したとき（自分の関数のままだったとき）は、設定前の関数への参照を捨てる
- 利用者が Manager の `Awake` より後に `InternalIdTransformFunc` を上書きした場合、キャッシュ対策は効かなくなる（連結は Manager 側からしかできないため）。利用者が独自の変換関数を使うときは、`DefaultExecutionOrder(-1000)` の Manager より先に設定するか、キャッシュ対策を OFF にして自前で行う

### 6.5 コストへの配慮

変換関数は、カタログに限らず**すべてのロケーションの解決**で呼ばれる。

- 毎回のログ出力はしない。クエリを付けたときだけ `Log`（Verbose）を出す（`AddressableLogger.Log` は `[Conditional("UNITY_ASSERTIONS")]` のため、リリースビルドでは呼び出しごと消える）
- 拡張子・接頭辞の判定は `StringComparison.Ordinal` で行う（カルチャ依存の比較を避ける）
- 対象外の InternalId は、新しい文字列を作らず、受け取った文字列をそのまま返す。文字列を作るのは `.hash` のときだけで、起動時とカタログ更新の確認時にしか起きない

---

## 7. 内部構成

クラス図は [`diagrams/initialize-class.md`](../diagrams/initialize-class.md) を参照。

### 7.1 クラスと責務

| クラス | 置き場所 | namespace | 責務 |
|---|---|---|---|
| `AddressableManager` | `Runtime/Core/` | `MAH.AddressableManager` | 合成ルート。URL キャッシュ対策の設定と復元、`InitializeStateModel` の生成と注入、InitializeController と Handler の生成・`Dispose`、自動モードでの初期化の開始、`LateUpdate` の中継。公開メンバーは `AccInstance` / `AccHandler` のみで、初期化の API・イベントは持たない |
| `IAddressableHandler` | `Runtime/Core/Interfaces/` | `MAH.AddressableManager` | 利用者の唯一の入口（外部依存型）。初期化の API（4章）を含む |
| `AddressableHandler` | `Runtime/Core/Handler/` | `MAH.AddressableManager.Core` | `IAddressableHandler` の実装。状態を持たず、InitializeController・LoadingController へ転送する。イベントは `add` / `remove` で InitializeController のイベントへ直接つなぐ（7.7）。複数機能を知るため機能フォルダの外に置く。内部実装型 |
| `InitializeController` | `Runtime/Core/Initialize/Controller/` | `MAH.AddressableManager.Core.Initialize` | 初期化の実行、`InitializeStateModel` を使った状態遷移、進捗の通知、内部イベント（`OnSucceeded` / `OnFailed`）の発火（7.7）、`IInitializationGate` の実装。ピュア C# |
| `InitializeStateModel` | `Runtime/Core/Initialize/Model/` | `MAH.AddressableManager.Core.Initialize` | 初期化の状態・失敗の例外・共有タスク・進捗の通知先を保持する Model（7.2）。Addressables・設定は知らない。ピュア C# |
| `IAddressablesInitializeService` / `AddressablesInitializeService` | `Runtime/Core/Initialize/Service/` | `MAH.AddressableManager.Core.Initialize` | `Addressables.InitializeAsync` の1回の呼び出しと、その handle の進捗・完了の受け渡し。進捗は `GetPercentComplete()`（完了後は 1。handle の状態確認を伴うためアクセサにしない）。EditMode テストで偽物に差し替えるためにインターフェースを挟む |
| `CatalogCacheBustingService` | `Runtime/Core/Initialize/Service/` | `MAH.AddressableManager.Core.Initialize` | `InternalIdTransformFunc` の連結・復元と、URL の変換（6章）。起動時の値はコンストラクタで受け取る（テストで値を固定するため）。連番は自身で進める |
| `AddressableInitializeSettings` と enum 群 | `Runtime/Core/Initialize/Data/` | `MAH.AddressableManager.Core.Initialize` | 設定値（3章） |
| `IInitializationGate` | `Runtime/Core/Interfaces/` | `MAH.AddressableManager.Core` | Loading が「読み込んでよいか（待つ必要があるか）／待つ」を、Download が「初期化の成功まで待つ」を問い合わせる窓口（7.4）。どの機能にも属さない共通物 |
| `InitializeState` | `Runtime/Core/Initialize/Data/` | `MAH.AddressableManager` | 初期化の状態。利用者が `AccHandler.AccInitializeState` で参照するため外部依存型 |
| `AddressableInitializeException` | `Runtime/Core/` | `MAH.AddressableManager` | 初期化の失敗。利用者が `catch` するため外部依存型（`AssetLoadException` と同じ扱い） |

InitializeController に渡す設定は `AddressableInitializeSettings` だけ（Runtime SO を丸ごと渡さない。設計仕様書 3.1.1）。

### 7.2 状態の Model（`InitializeStateModel`）

初期化の状態データを Controller から分けて持つ Model。Loading の `AssetRegistryModel` 等と同じく、**Controller の中で `new` せず、Manager の `Awake` で生成して InitializeController のコンストラクタに注入する**（Model は複数の Controller から参照され得る前提で配線するため。EditMode テストもテスト側で `new` して渡す）。

| 持つもの | 内容 |
|---|---|
| 状態 | `InitializeState`（2.3） |
| 失敗の例外 | `Failed` になったときの `AddressableInitializeException`。`Failed` の後の `InitializeAsync`・ゲートは、毎回これを投げる |
| 共有タスク | 初期化1回分の処理を表す `Task`（7.3）。Loading の `AssetEntryModel` がロードタスクを持つのと同じ扱い |
| 進捗の通知先 | 待っている呼び出し元の `IProgress<float>`（7.6）。リストはフィールドで保持して使い回す |

- 遷移はメソッドで行う：`MarkRunning(task)`（`NotStarted` からのみ）・`MarkSucceeded()`・`MarkFailed(exception)`（どちらも `Running` からのみ）。条件に合わない呼び出しでは状態を変えない。**`Succeeded` / `Failed` が終端であることを Model の側で保証する**
- Model を直接触るのは InitializeController だけ。Handler の `AccInitializeState` / `AccIsInitialized` は InitializeController を通して読む（隣の層とだけ話す。設計仕様書 2章）
- LoadingController（将来の DownloadController も）は Model を参照せず、`IInitializationGate`（7.4）を通す。Model は `Core/Initialize/` にあり、他の機能フォルダから参照すると設計違反になるため

### 7.3 共有タスクとキャンセル

- 初期化1回分の処理を**1つの `Task`** にして `InitializeStateModel` に持たせ、`InitializeAsync` の呼び出し元すべてがその `Task` を待つ。UniTask 使用時も、共有するタスクは安全性を優先して `Task` のままにする（複数の呼び出し元が待ち、完了後にも後から呼ばれた `InitializeAsync` が待つため。`Task` は何度 `await` しても安全だが、UniTask は2回目の `await` で例外や別処理の状態の読み取りになり得る。Loading の LoadTask の共有と同じ理由。設計仕様書 3.5.1・loading-spec 6.2）
- 公開 API の `InitializeAsync` は、共有タスクに `AsyncCompat.WithCancellation(ct)` を付けて返す。呼び出し元の `ct` で打ち切られるのは**その呼び出し元の待機だけ**
- 初期化そのものを止めるのは、Controller が内部に持つ `CancellationTokenSource` だけで、Manager の破棄（`Dispose`）でキャンセルされる
- 自動モードの開始は、Manager が `InitializeController.StartAutoInitialize()` を呼ぶだけにする。Controller が `InitializeAsync` を待たずに始め、失敗の例外を観測済みにする（未観測の例外として `TaskScheduler.UnobservedTaskException` に流れないようにするため。失敗の記録・ログは `RunAsync` が行う）。Manager は Task の扱いを持たない
- `Failed` の後の `InitializeAsync` は、例外を投げ直さず、失敗済みの共有タスクを `await` して同じ例外を返す（投げ直すと、そのたびに同じインスタンスのスタックトレースが上書きされるため）
- 共有タスクは**1回だけ作る**。`Failed` の後も作り直さず、失敗した共有タスクを返し続ける（2.3）
- 共有タスクは `TaskCompletionSource` で作り、`MarkRunning` してから Service を呼ぶ。Addressables が初期化済みで同期的に完了しても、`NotStarted → Running → Succeeded` の順序を守るため。完了時は、状態の遷移とイベントの発火（7.7）を先に行い、その後で共有タスクを完了させる（待っていた呼び出し元が再開した時点で、状態とイベントが確定しているようにするため）
- `Dispose` は、待機中の呼び出し元を共有タスクの `TrySetCanceled` で直接終わらせる（継続の実行順に頼らず、確実に `OperationCanceledException` で終わらせるため）。状態は変えない。待つのをやめた Service のタスクは、後から失敗しても未観測の例外にならないよう観測しておく

### 7.4 Loading との橋渡し（`IInitializationGate`）

```csharp
// Runtime/Core/Interfaces/IInitializationGate.cs（namespace MAH.AddressableManager.Core）
internal interface IInitializationGate
{
    /// <summary> 待たずに Load してよいか（PassThrough で初期化に失敗していない、または初期化に成功済み） </summary>
    bool AccIsOpen { get; }
    /// <summary> Load してよい状態になるまで待つ。未開始（WaitForInitialize）・失敗時は例外（5章の表） </summary>
    Task WaitUntilOpenAsync(CancellationToken ct);
    /// <summary> 初期化の成功まで待つ（Download が使う。LoadBeforeInitializePolicy に関係なく待つ）。未開始は InvalidOperationException、失敗は AddressableInitializeException </summary>
    Task WaitUntilInitializedAsync(CancellationToken ct);
}
```

- **InitializeController が実装し、Manager が LoadingController・DownloadController のコンストラクタに注入する**。どちらも知っているのはこのインターフェースだけで、`MAH.AddressableManager.Core.Initialize` を `using` しない
- インターフェースを `Core/Interfaces/`（共通物）に置くのは、Initialize（実装する側）と Loading・Download（使う側）の両方から参照されるため。機能フォルダのどれかに置くと、他の機能がその機能フォルダを参照することになり、設計違反になる
- `WaitUntilInitializedAsync`（2026-10-08 追加。Download 用）は、**設定 `_loadBeforeInitialize` に関係なく、初期化の成功まで待つ**。Download はカタログの確認・更新・ダウンロードに Addressables の初期化が済んでいることを必要とし、「Load は待たないが Download は待つ」を `PassThrough` のままで両立させるため、`WaitUntilOpenAsync` とは別にしている。動作は、成功済み→すぐ完了／実行中→共有タスクの完了を待つ（`AsyncCompat.WithCancellation(ct)` 付き）／未開始→`InvalidOperationException`／失敗→Model が持つ例外。`AccIsOpen` のような「待たずに済むか」の問い合わせは、Download では要らない（Download は常に初期化の成功を待つため）。詳細は [`download-spec.md`](download-spec.md) 5章・7.4
- 判定は InitializeController の中で、`InitializeStateModel` の状態と `_loadBeforeInitialize` を組み合わせて行う。「初期化前・失敗後の Load の扱い」は初期化の設定と状態であり、Loading 側はどちらも知らずに済む

| 状態 | `AccIsOpen`（`PassThrough`） | `AccIsOpen`（`WaitForInitialize`） | `WaitUntilOpenAsync` |
|---|---|---|---|
| `NotStarted` | `true` | `false` | `InvalidOperationException` |
| `Running` | `true` | `false` | 共有タスクの完了を待つ（成功なら完了、失敗なら `AddressableInitializeException`） |
| `Succeeded` | `true` | `true` | すぐに完了する |
| `Failed` | **`false`** | `false` | すぐに `AddressableInitializeException`（Model が持つ例外） |

- LoadingController は `if (!_initializationGate.AccIsOpen) await _initializationGate.WaitUntilOpenAsync(ct);` の形で呼ぶ。開いているときは `await` も確保も起きない
- 待機中の破棄でゲートから届く `OperationCanceledException` は、LoadingController が `ObjectDisposedException` に変換する（5章）。ゲート（InitializeController）は Loading の破棄状態を知らないため、変換は Loading 側の役目
- EditMode テストでは、常に開いている偽のゲートを渡せば、これまでどおり Loading だけを単体で動かせる

### 7.5 handle の扱い（`autoReleaseHandle`）

- `true`（既定）：Addressables が完了時に handle を自動で解放する。結果（成功・失敗・例外）は Service が完了通知の中で読み取って Controller に渡す（解放後の handle は読めないため）
- `false`：Service が handle を解放せずに保持し、InitializeController の `Dispose` で `Addressables.Release` する
- 利用者から handle は見えない（チケット・状態・例外だけを公開する方針。glossary 7章）
- **`Addressables.InitializeAsync` が同期で投げる例外も、Service が捕まえて失敗として Controller に渡す**。手動＋`PassThrough` で、Load がきっかけの Addressables の自動初期化が先に失敗していると、後から呼んだ `InitializeAsync` は `ArgumentOutOfRangeException` を同期で投げる（10.1）。これも `Failed` にして、以降の Load をゲートで止める

**結果を読む時機と Task への変換**：
- 結果（`Status`・`OperationException`）は、**`Completed` コールバックの中で読む**。完了通知の間は Addressables が操作の参照を保っているため、`autoReleaseHandle=true` で先に解放の処理が登録されていても読める（Addressables 2.9.1 の `AsyncOperationBase.Complete`）。`await` の後（継続の中）では、解放が終わっている場合があり読めない
- Service は `Completed` から `TaskCompletionSource` を作り、**シンボルに関係なく `Task` を返す**（共有タスクは常に `Task`。7.3）。`AsyncCompat.ToTask` は UniTask 版では定義されないため使わない
- 呼んだ時点で操作が完了済み（`IsDone`）なら、`Completed` を待たずにその場で結果を読む（初期化済みの場合。10.1）

### 7.6 進捗（`Tick`）

- Manager は `LateUpdate` から `OnLateUpdate`（`Time.unscaledDeltaTime`）を中継しており、InitializeController の `Tick(deltaTime)` もこれに購読する（`Awake` で `+=`、`OnDestroy` で `-=`。必ず対で書く）
- `Tick` は `Running` のときだけ働き、それ以外は先頭で戻る（初期化後は実質何も走らない）。Service から handle の `PercentComplete` を読み、待っている呼び出し元の `IProgress<float>` に通知する
- 進捗の通知先は、待機を始めた呼び出し元ごとに `InitializeStateModel` に登録し、待機が終わったら（完了・キャンセルとも）外す
- 通知先は利用者のコードのため、`Report` が例外を投げても `InitializeStateModel.ReportProgress` が受け止めて `AddressableLogger.LogException` に流し、残りの通知先への通知を続ける（7.7 のイベントの購読側と同じ理由。完了時の 1 は共有タスクの完了より前に通知するため、ここで例外が出ると待機中の呼び出し元と `WaitForInitialize` の Load が終わらなくなる。`Tick` で例外が出ると、同じ `OnLateUpdate` の後ろの購読先〔`LoadingController.Tick`〕も止まる）
- 通知先は、**初期化を始める前に登録する**。Addressables が初期化済みだと開始と同時に完了し、完了時の 1 を通知するため、登録が後だと 1 を取りこぼす

### 7.7 イベントの発火

- InitializeController は `internal event OnSucceeded` / `OnFailed` を持ち、成功・失敗の遷移の直後に1回だけ発火する。Handler の公開イベント `OnInitialized` / `OnInitializeFailed` は、**フィールドを持たず、`add` / `remove` アクセサで Controller のイベントへ直接つなぐ**（`add => _controller.OnSucceeded += value;`、`remove => _controller.OnSucceeded -= value;`）。Manager は購読も中継もしない。Handler に購読の状態を作らないため、解除し忘れる経路は利用者の購読と解除の対だけになる
- 公開イベントを Controller に持たせないのは、利用者が触れる窓口を外部依存型（`IAddressableHandler`）に限るため（conventions 7章）
- 購読側（利用者のハンドラ）が投げた例外は、Controller の発火箇所で `try/catch` して `AddressableLogger.LogException` に流す。発火は Service の完了通知の中で同期的に行われるため、例外をそのまま流すと状態遷移と共有タスクの完了が止まり、待っている呼び出し元が終わらなくなるため。Handler を介さない EditMode テストでも保証できるよう、Controller 側に置く

---

## 8. 設計の判断と理由

| 判断 | 理由 |
|---|---|
| 自動初期化を Manager の `Awake` で始める（`RuntimeInitializeOnLoadMethod` は使わない） | 設定（Runtime SO）は Manager に割り当てられており、`Awake` で読める。シーンに Manager を置く今の構成のまま使え、SO を探す仕組み（Resources 等）を別に用意せずに済む |
| `await` とイベントの両方を用意する | `await` は取りこぼしがなく、起動フローを順に書くのに向く。イベントは、初期化を始めた側と結果を受け取る側が別のクラスである場合（UI の表示切り替え等）に向く |
| `InitializeAsync` は何度呼んでも同じ処理を共有する | 自動モードで始まった初期化を、利用者が後から待てるようにするため。呼び出しのたびに初期化をやり直すと、自動と手動で使い方が分かれてしまう |
| 呼び出し元の `ct` は待機だけを打ち切る | 複数の呼び出し元が同じ初期化を待つため、1人のキャンセルで全員の初期化が止まってはいけない |
| 初期化前の Load は既定で待たない（`PassThrough`） | Addressables は最初のロード時に自動で初期化されるため、待たなくても読み込みは動く。既定で待たせると、これまで `InitializeAsync` なしで動いていたコードが動かなくなる（設計仕様書 5.3） |
| `WaitForInitialize`＋手動＋未開始の Load は例外にする | 「呼ばれるまで待つ」にすると、`InitializeAsync` を呼び忘れたときに Load が永遠に終わらず、原因に気づきにくい |
| リトライ・タイムアウト・失敗後のやり直しを持たない（`Failed` は終端） | Addressables 2.9.1 は、失敗した初期化をアプリの再起動までやり直せない（10.1。呼び直すと同じ失敗の操作が返るか、同期で例外になる）。実行中に呼び直しても同じ操作が返るため、タイムアウト後の再試行も前回と同じ操作を待つだけになる。また、リモートの `.hash` は取れなければローカル・キャッシュのカタログで続行するため、初期化の失敗はネットワークではなくビルドの不備（`settings.json`・ローカルカタログが読めない）で起き、再試行しても直らない。効かない設定を置くと「効くように見えて効かない」状態になるため持たない。カタログ取得のタイムアウトは Addressables の `CatalogRequestsTimeout`、ネットワークの再試行は Download で扱う |
| 初期化に失敗した後は、設定に関係なく Load を止める | 失敗の後は Addressables 全体が使えず、読み込みを試させても原因の分からないエラー（キーが無効）になるだけ。`LoadByLabelAsync` は空のまとめチケットになり、失敗に気づけない恐れもある。ゲートで止めて、初期化の失敗をそのまま伝える（5章） |
| 初期化に失敗した後の Load からは初期化をやり直さない | Load は1件のアセットの読み込みであり、モジュール全体の初期化の判断をそこでさせない。そもそも Addressables 側でやり直せない（10.1） |
| 状態を `InitializeStateModel` に分ける | 状態データ（状態・失敗の例外・共有タスク・進捗の通知先）を Controller の処理と分け、終端の制約を Model の遷移メソッドで守るため。Loading の Registry 群と同じく Manager から注入し、Model を複数の Controller から参照する形にも配線し直さずに広げられる |
| Service を挟む | `Addressables.InitializeAsync` を直接呼ぶと、EditMode テストで失敗・同期の例外・進捗を再現できない（Loading の `IAssetLoaderServiceFactory` と同じ理由） |
| URL キャッシュ対策は既定で OFF | リモートカタログを使わないプロジェクトや、CDN 側でキャッシュを制御しているプロジェクトでは不要。全ロケーションの解決に変換関数が挟まるため、必要なときだけ有効にする |
| URL キャッシュ対策は既存の変換関数を上書きしない | `InternalIdTransformFunc` は1つしか持てず、上書きすると利用者の URL 差し替え等が黙って無効になる |
| URL キャッシュ対策の対象は `.hash` だけ | カタログ本体の URL は変換後の `.hash` の URL から作られるため、`.hash` だけで本体にも効く。対象を広げると、利用者のリモートの JSON 等まで巻き込む（6.2） |
| クエリの値は要求ごとに変える（セッションで固定しない） | ゲーム内のカタログ更新の確認でも OS のキャッシュを避けるため。本体は `.hash` の変換結果から作られるので、値が変わっても組み合わせは崩れない（6.2） |
| 破棄時の変換関数の復元は、自分の関数のままのときだけ | 利用者が後から設定した関数を、復元で消さないため（6.4） |

---

## 9. 改修ガイド

### 9.1 設定項目を追加したい

1. `AddressableInitializeSettings` にフィールドとアクセサを足す（命名は `conventions.md` 1章）。範囲のある値を足すときは、`AddressableLoadingSettings` と同じ形で `ClampValues()` を作って補正し、Runtime SO の `OnValidate` とテスト用コンストラクタから呼ぶ（今は範囲のある値が無いため、メソッドも無い）
2. テスト用コンストラクタの引数に足す
3. InitializeController で使う。**Runtime SO や他の機能の設定を参照しない**
4. 本書3章の表を更新する

### 9.2 URL キャッシュ対策の対象・付け方を変えたい

`CatalogCacheBustingService` の変換規則だけを変える。対象の拡張子（`.hash`）は定数で持っている。対象を広げる前に、6.2 の「`.hash` に絞る理由」を確認すること（本体のカタログは `.hash` から導かれるため、多くの場合は広げる必要がない）。プロジェクトごとに変えたい要望が出た場合は、SO の設定項目に昇格させる（9.1）。変換はすべてのロケーションで呼ばれるため、対象外のときに文字列を作らない形を保つ（6.5）。

### 9.3 初期化の後に処理を続けたい（カタログ更新など）

InitializeController に処理を足さず、**Manager 側で順序を組む**（Manager が持つ起動シーケンスの中で、InitializeController の完了後に DownloadController を呼ぶ等）。Controller 同士は互いを参照しないため、順序の保証は Manager の役目（設計仕様書 2章）。InitializeController は「`Addressables.InitializeAsync` を確実に終わらせる」ことだけに保つ。

**現状（2026-10-08）：Manager に起動シーケンスは持たせていない**。Download を実装した結果、利用者が Handler の `CheckUpdateAsync` / `DownloadAsync` を、好きなタイミングで呼ぶ形になった（容量の確認ダイアログを挟めなくなるため、自動では呼ばない）。DownloadController は、呼ばれたときに `IInitializationGate.WaitUntilInitializedAsync` で初期化の成功まで待つため、順序は DownloadController 側で保証される（初期化が未開始なら `InvalidOperationException`）。Handler の `InitializeAsync` は引き続き、初期化だけを行い、後続の処理を含めない。起動時の自動ダウンロードが要るときは、[`download-spec.md`](download-spec.md) 9.3 に従い、SO の設定として足す。

起動シーケンスに処理を足した場合、`AccIsInitialized`・イベント・`IInitializationGate` が「どの処理まで終わったら成功か」を表すのかを決め、本書2章・5章を更新すること。

### 9.4 初期化前の Load の扱いを変えたい

- 選択肢を増やす：`LoadBeforeInitializePolicy` に値を足し、InitializeController の `AccIsOpen` / `WaitUntilOpenAsync` の判定（7.4 の表）を変える。LoadingController 側は変更不要。**`Failed` でゲートを閉じる扱いは、どの選択肢でも崩さない**（5章）
- 待つ位置を変える：LoadingController の `LoadAsync` / `LoadByLabelAsync`。**寿命の解決と RefCount の加算より前**で待つ形は崩さない（5.1）。後ろに動かすと、待機中のキャンセル・破棄で巻き戻しが必要になる

### 9.5 Addressables を更新したとき

`Tests/Runtime/AddressablesInitializeBehaviorTests.cs` を実行し、10.1 の挙動が変わっていないか確かめる。失敗した初期化をやり直せるようになっていた場合は、`Failed` を終端にした判断（8章）を見直す。このテストは Addressables の internal（`Addressables.m_AddressablesInstance` 等）にリフレクションで触れるため、内部構成が変わると SetUp で失敗する。そのときはテストを直してから挙動を確かめる。

### 9.6 改修時に守ること

- InitializeController・Model・Service はピュア C# に保つ（`MonoBehaviour` を継承しない。Unity のライフサイクルは Manager から中継する）
- `InitializeStateModel` は Controller の中で `new` しない（Manager から注入する）。`Failed` の後に `Addressables.InitializeAsync` を呼ぶ経路を作らない（2.3）
- 非同期は UniTask が正で、UniTask が無い場合だけ Task を使う。違いは、公開 API のシグネチャの `#if ADDRESSABLE_UNITASK_SUPPORT` と `AsyncCompat` に閉じ込め、UniTask を `#if` の外で `using` しない。共有タスク（7.3）と `IAddressablesInitializeService`・`IInitializationGate` の戻り値は、UniTask 導入時も `Task` のままにする（設計仕様書 3.5.1）
- 時間待ちを足すときは `AsyncCompat.Delay` を使う（`UniTask.Delay` を直接呼ぶと、ポーズ中〔`timeScale = 0`〕に待機が終わらない）
- `Core/Initialize/` から `Core/Loading/`（`Core/Download/`）を `using` しない。共有が必要なものは `Core/` 直下に置く
- イベントの購読（`OnLateUpdate`・`OnInitialized` 等）と `InternalIdTransformFunc` の設定は、`Awake`／`OnDestroy` で必ず対にする

---

## 10. 注意点・既知の制約

- **イベントの取りこぼし**：購読前に初期化が終わっていると、イベントは届かない（4.1）
- **初期化の失敗は致命的**：失敗するとアプリを再起動するまで Addressables 全体が使えない（10.1）。本モジュールは失敗を `await`・イベントで知らせ、以降の Load をゲートで止める（5章）。再起動を促す等の対応は利用者側で行う
- **手動＋`PassThrough` で `InitializeAsync` の前に Load した場合**：Addressables が自動で初期化する。このとき初期化の状態は `NotStarted` のままで、イベントも発火しない（Manager・Handler を通らない初期化のため）。後から `InitializeAsync` を呼ぶと、Addressables 側の初期化が成功していればすぐ完了して `Succeeded` になる。失敗していれば同期の例外を受けて `Failed` になり（7.5）、以降の Load はゲートで止まる
  - ただし、`InitializeAsync` を呼ぶ前の Load は、Addressables の自動初期化が失敗してもゲートでは止まらず、Addressables のエラー（キーが無効）で失敗する。初期化機能が失敗を知らないため。失敗した時点で確実に止めたい場合は、自動モードか `WaitForInitialize` を使う
- Domain Reload を無効にした環境では、`InternalIdTransformFunc` が前回の Play の値を持ち越すことがある。Manager の `OnDestroy` で元の関数に戻すため、Play を正常に終えていれば残らない

### 10.1 Addressables の挙動の確認結果

実装時に PlayMode で確認し、結果をここに記録する（未確認の間は「未確認」と書く）。

PlayMode での確認は `Tests/Runtime/AddressablesInitializeBehaviorTests.cs`（P1〜P5）で行う。Addressables 本体の挙動を確かめるテストで、Addressables を更新したときの回帰テストを兼ねる。各テストの前後で Addressables の内部インスタンスをリフレクションで作り直し、未初期化の状態から始める（Editor が Play のたびに行う作り直しと同じ処理）。

| 確認する点 | 結果 | 本機能への影響 |
|---|---|---|
| 失敗した後に `Addressables.InitializeAsync` を再び呼ぶと、初期化をやり直すか（前回の失敗した操作が返らないか） | **やり直さない**（2026-10-02 PlayMode で確認。P1・P2・P5）。handle を保持している間は、同じ失敗済みの操作が返る。handle が解放された後（自動解放・自分で解放のどちらも）は、呼んだその場で `ArgumentOutOfRangeException` が出る。初期化を始めた印（`hasStartedInitialization`。internal）が戻らず、失敗時に空になったロケーター一覧の先頭を読むため。失敗の後は Load も失敗する。この状態から戻すには、アプリの再起動が必要（Editor では Play のたびに作り直されるため、再起動したのと同じになる） | リトライ・失敗後のやり直しは効かないため持たず、`Failed` を終端にした（2.3・8章）。失敗の後は Load をゲートで止める（5章）。`Failed` の後に `Addressables.InitializeAsync` を呼ばない |
| 実行中に `Addressables.InitializeAsync` を再び呼ぶと、同じ操作が返るか（タイムアウト後の再試行に関係する） | **同じ操作が返る**（2026-10-02 PlayMode で確認。P3） | タイムアウト後の再試行は前回と同じ操作を待つだけになるため、タイムアウトを持たない（8章）。共有タスクの仕組み（7.3）と食い違わない |
| 初期化済みのときに `Addressables.InitializeAsync` を呼ぶと、同期的に完了するか | **同期で完了する**（2026-10-02 PlayMode で確認。P4）。呼んだ直後に、成功で完了済みの操作が返る。なお、存在しないローカルのパスによる失敗も、呼んだその場で完了する | 自動モードでも `Awake` の中で `Succeeded` になり、イベントが購読前に発火する。4.1 の「購読時に `AccIsInitialized` を確認する」は必須 |
| `InternalIdTransformFunc` にリモートカタログの `.hash` の InternalId が渡るか（Addressables 2.9.1） | ソース上は渡る（`TextDataProvider` → `ResourceManager.TransformInternalId`）。本体（`.bin`）の URL は変換後の `.hash` の URL から作られる（`ContentCatalogProvider`）。実機では未確認 | 渡らない場合、6章の対策は効かない |

---

## 11. テスト

| 種類 | 場所 | 確認すること |
|---|---|---|
| EditMode | `Tests/Editor/` | 偽の `IAddressablesInitializeService` で InitializeController を動かす：共有タスク（2回呼んでも初期化は1回）、呼び出し元のキャンセルが他に影響しないこと、失敗が `await` と `OnInitializeFailed` の両方に届くこと、`Failed` の後の `InitializeAsync` が Service を呼ばずに同じ例外を返すこと、Service の同期の例外が `Failed` になること、`autoReleaseHandle=false` での `Dispose` 時の解放、進捗の通知 |
| EditMode | `Tests/Editor/` | `InitializeStateModel`：遷移（`NotStarted → Running → Succeeded / Failed`）と、条件に合わない遷移で状態が変わらないこと（`Succeeded` / `Failed` は終端）、進捗の通知先の登録・解除 |
| EditMode | `Tests/Editor/` | `CatalogCacheBustingService`：`.hash` 以外（`.bin` / `.json` を含む。クエリの中だけに `.hash` がある URL も）と `http` 以外は変換しないこと・呼ぶたびに連番が進むこと・クエリ付きの `.hash`（署名トークン等）に `&` で続くこと・既存の関数との連結・復元（自分の関数のままなら戻す／利用者が上書きしていたら戻さない） |
| EditMode | `Tests/Editor/` | `IInitializationGate`：7.4 の表の各マス（InitializeController 側）。5章の表の各マス（偽のゲートで LoadingController を動かし、待機中のキャンセル・破棄・寿命の終了で何も残らないこと。破棄は `ObjectDisposedException`、寿命の終了は `OperationCanceledException` になること。`PassThrough` でも `Failed` の後は `LoadAsync` / `LoadByLabelAsync` が `AddressableInitializeException` になり、何も残らないこと） |
| PlayMode | `Tests/Runtime/InitializePlayModeTests.cs` | 実際の Addressables で、自動モードの起動後に `Succeeded` になること、手動モードで `InitializeAsync` の後に `Succeeded` になること、イベントが発火すること、進捗の最後に 1 が届くこと、`WaitForInitialize` での Load の待機と未開始時の例外、URL キャッシュ対策の設定と復元。初期化中の Manager の破棄は、実際の Addressables では初期化が同期的に終わって再現できないため、EditMode（偽の Service）で確認する |
| PlayMode | `Tests/Runtime/AddressablesInitializeBehaviorTests.cs` | Addressables 本体の挙動（10.1 の P1〜P5）。Addressables の更新時の回帰テスト（9.5） |

既存の Loading のテストは、常に開いている偽のゲートを渡すことで、初期化の有無に関係なく動く。
