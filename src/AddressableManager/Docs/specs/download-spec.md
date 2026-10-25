# 機能仕様書：ダウンロード（Download）

> 最終更新日：2026-10-08 ／ 対象 Unity バージョン：6000.3.26f1
> **この機能（カタログの更新確認・更新分のアセットのダウンロード）の仕様はこのファイルを正とする**。全体の概要・他機能との関係は [`addressable-manager-design.md`](../addressable-manager-design.md) 3.3 を参照。命名パターンは [`conventions.md`](../conventions.md)、用語は `Docs/glossary.md`（リポジトリルート）が正。
> 図は [`diagrams/download-class.md`](../diagrams/download-class.md)・[`diagrams/download-sequence.md`](../diagrams/download-sequence.md) を参照。
>
> 本書は、ダウンロード機能が**どう動き、どう作られているか**、そして**改修するときにどこを触ればよいか**を、後から読む開発者が把握するための資料である。初期化の仕組みは [`initialize-spec.md`](initialize-spec.md)、読み込み・解放（Loading）の仕組みは [`loading-spec.md`](loading-spec.md) を前提とする。

---

## 1. 概要

`AddressableManager` が生成する `DownloadController` が、リモートのカタログの更新確認と、更新分のアセットのダウンロードを行う機能。起動シーンでも、ゲーム内の追加ダウンロード（イベント配信等）でも、同じ仕組みで使える。

利用者は `IAddressableHandler` の次のメンバーだけで、「更新があるかの判定 → 件数と容量の確認 → ダウンロードと進捗の表示」を行える。Addressables の API（handle の解放・進捗の取得・リトライ）は意識しなくてよい。

| メンバー | できること |
|---|---|
| `HasUpdateAsync` | 更新データがあるかどうかだけを `bool` で返す（カタログは更新しない） |
| `CheckUpdateAsync` | カタログを確認・更新し、更新されたカタログ数・ダウンロードが必要なバンドル数・合計バイト数（`DownloadCheckResult`）を返す |
| `DownloadAsync` | 対象 Label のアセットをダウンロードする。段階・バイト数・割合・リトライ回数（`DownloadStatus`）を `IProgress<DownloadStatus>` で通知する |
| `AccDownloadStatus` | 現在の `DownloadStatus`（取得した時点の値のコピー）を取得する |

- 対象は **Label 単位**。対象の Label は Runtime SO（`AddressableDownloadSettings`）で設定し、メソッドの引数で上書きもできる（3章）
- **`DownloadAsync` は、バンドルをダウンロードしてキャッシュに保存するだけ**で、完了したらすぐ handle を解放する（7.5）。アセットをメモリに読み込まず、Loading の Registry（RefCount）にも登録しない。アセットを使うときは、別に `LoadAsync` / `LoadByLabelAsync` を呼ぶ（キャッシュから読むため、通信は起きない）
- **`DownloadAsync` はカタログを更新しない**（今のカタログで、ダウンロードが必要なバンドルを取得するだけ）。カタログの更新は `CheckUpdateAsync` で行う。典型的な流れは「`CheckUpdateAsync`（`CheckAndUpdate`）→ 件数と容量を確認ダイアログで表示 → 同意されたら `DownloadAsync`」
- 公開 API はすべて `IAddressableHandler` に置く（利用者が触れる入口を1つに絞るため。別の Handler は作らない。設計仕様書 3.3・3.7）

**この機能が持たないもの**：起動時の自動ダウンロード（容量の確認ダイアログを挟めなくなるため）、`AssetKey` 単位のダウンロード（Label 単位のみ）、ダウンロード済みバンドルのキャッシュ削除、一時停止・再開（Addressables に該当する API が無い）、ネットワーク接続の事前確認（通信の可否は利用者側の責務。失敗はリトライの後に例外で返す）、通信そのものの中断（10章）。Editor 拡張による状態の表示は、Editor 拡張の実装時に扱う。

### 1.1 全体の中での位置づけ

```
AddressableManager（合成ルート。機能内クラスの生成・寿命管理・ライフサイクルの中継。公開は AccHandler のみ）
├─ AddressableHandler    … 利用者の唯一の入口（IAddressableHandler）。Download の API を DownloadController へ転送する
├─ InitializeController  … 初期化（initialize-spec.md）。IInitializationGate を実装する
├─ DownloadController    … 本書。確認・更新・ダウンロードの実行と状態遷移
│    ├─ DownloadStatusModel … 現在の状態（段階・バイト数・リトライ回数）と進捗の通知先
│    └─ Service            … Addressables のカタログ確認・更新・サイズ計算・ダウンロードの呼び出し
└─ LoadingController     … 読み込み・解放（loading-spec.md）
```

- DownloadController は **InitializeController・LoadingController を参照しない**（設計の絶対ルール）。初期化の成功を待つ仕組みは、`Core/` 直下の共通インターフェース `IInitializationGate` を介す（5章・7.4）
- Download は Loading の Registry・RefCount を触らない。ダウンロード未完了のアセットを Load した場合の扱いは、LoadingController／Service 側のエラーハンドリングに委ねる

---

## 2. 動作の全体像

### 2.1 更新の確認（`CheckUpdateAsync`）

1. 初期化の成功を待つ（5章）
2. カタログの更新確認・適用を、設定の方針（`CatalogUpdatePolicy`）に従って行う
3. 対象 Label について、ダウンロードが必要なバンドルの数と合計バイト数を数える
4. `DownloadCheckResult` を返す

| 方針 | カタログの確認（リモートサーバーへの問い合わせ） | カタログの更新 | 件数・容量を数える対象 |
|---|---|---|---|
| `None` | **しない**（通信しない） | しない | 今のカタログ |
| `CheckOnly` | する | **しない**（`UpdateCatalogs` を呼ばない） | 今のカタログ（保留中の更新は含まない） |
| `CheckAndUpdate`（既定） | する | 更新のあるカタログがあればする | 更新後のカタログ |

`DownloadCheckResult`（`readonly struct`）：

| メンバー | 意味 |
|---|---|
| `AccCatalogUpdateCount` | 更新があると分かったカタログの数。`CheckAndUpdate` では適用した数、`CheckOnly` では適用していない数。`None` では 0 |
| `AccBundleCount` | ダウンロードが必要な AssetBundle の数（サイズが 0 より大きいものを重複なく数える） |
| `AccTotalBytes` | ダウンロードが必要な合計バイト数 |
| `AccIsCatalogUpdatePending` | `CheckOnly` で、更新のあるカタログがあるのに適用していない（`AccCatalogUpdateCount > 0` かつ `CheckOnly`）とき `true` |
| `AccHasDownload` | `AccBundleCount > 0` |

- 件数の数え方は `Addressables.GetDownloadSizeAsync` と同じ判定（7.7）。違いは、合計バイトだけでなくバンドルの数も得られること
- **`CheckOnly` の件数・容量は、今のカタログに対する値**で、保留中の更新で増えるアセットは含まない。更新後の件数が必要なときは `CheckAndUpdate` にする

段階：`CheckingCatalog`（方針が `None` なら飛ばす）→ `UpdatingCatalog`（`CheckAndUpdate` で更新のあるカタログがあるときのみ）→ `CalculatingSize` → `Completed`。

### 2.2 更新の有無の判定（`HasUpdateAsync`）

カタログを更新せずに、更新データがあるかどうかだけを返す。

1. 初期化の成功を待つ
2. 方針が `None` でなければ、カタログの更新確認を行う。**更新のあるカタログが1件以上なら、サイズを計算せずに `true`**（通信が `.hash` の取得だけで済む）
3. 更新のあるカタログが無ければ、今のカタログで対象 Label のバンドルを数え、1件以上なら `true`、0件なら `false`

- **SO の方針に関係なく `UpdateCatalogs` を呼ばない**
- カタログを更新したが、まだダウンロードしていない状態（前回の `CheckUpdateAsync` の後でアプリを閉じた等）でも、手順3で `true` になる
- `CheckUpdateAsync` との違いは、件数や容量を返さず、判定だけを返すこと。画面の表示を切り替えるだけの用途に向く
- 確認したことによる Addressables の状態の変化は6章を参照

段階：`CheckingCatalog`（方針が `None` なら飛ばす）→ `CalculatingSize`（手順3のときのみ）→ `Completed`。

### 2.3 ダウンロード（`DownloadAsync`）

1. 初期化の成功を待つ
2. 段階 `CalculatingSize`：対象 Label のダウンロードが必要なバンドルを数え、合計バイト数を確定する。**0件なら、ダウンロードを始めずに `Completed` で終わる**
3. 段階 `Downloading`：`Addressables.DownloadDependenciesAsync` を始める。Manager の `LateUpdate`（`Tick`）のたびに、ダウンロード済みバイト数と合計バイト数を読んで状態を更新し、`IProgress<DownloadStatus>` に通知する
4. 完了したら handle を解放し、`Completed` を通知して終わる

### 2.4 段階と状態

状態は `DownloadStatus`（外部依存型。`readonly struct`。`MAH.AddressableManager`）で表し、`DownloadStatusModel`（7.2）が持つ値から、取得・通知のたびに作る。

| 段階（`DownloadPhase`） | 意味 |
|---|---|
| `None` | まだ何も処理していない（ダウンロードなし） |
| `CheckingCatalog` | カタログの更新確認中 |
| `UpdatingCatalog` | カタログの更新中 |
| `CalculatingSize` | ダウンロードが必要なバンドルと容量を計算中 |
| `Downloading` | ダウンロード中。バイト数と割合が毎フレーム更新される |
| `Completed` | 直前の処理が成功した |
| `Failed` | 直前の処理が失敗した（リトライを使い切った、初期化の待機で失敗した） |
| `Canceled` | 直前の処理がキャンセルされた（`ct`、または Manager の破棄） |

- `Completed` / `Failed` / `Canceled` は、**次の処理が始まるまで残る**（UI が結果を表示できるように）。次の処理を始めた時点で、バイト数・リトライ回数を 0 に戻す
- 一度も処理していない間は `None`。取得は常に有効な値を返し、`null` は返さない
- 確認・判定の間は、バイト数は 0 のまま。段階が変わったときに1回通知する（`IProgress` を受け取る `DownloadAsync` のみ）。バイト数と割合が動くのは `Downloading` の間だけ

`DownloadStatus` のメンバー：

| メンバー | 意味 |
|---|---|
| `AccPhase` | 段階 |
| `AccIsDownloading` | `AccPhase == Downloading` |
| `AccIsBusy` | 処理の途中（`CheckingCatalog` / `UpdatingCatalog` / `CalculatingSize` / `Downloading`） |
| `AccDownloadedBytes` / `AccTotalBytes` | ダウンロード済み／合計のバイト数 |
| `AccPercent` | 0〜1。`Completed` なら 1、そうでなく `AccTotalBytes > 0` なら `AccDownloadedBytes / AccTotalBytes`（0〜1 に収める）、それ以外は 0。構造体の生成時に求める（アクセサにロジックを書かない規則のため） |
| `AccRetryCount` | 現在の段階でのリトライ回数（2.5） |

状態遷移図は [`diagrams/download-sequence.md`](../diagrams/download-sequence.md) を参照。

### 2.5 失敗・リトライ・キャンセル・同時実行

**リトライ**
- カタログの確認・更新・サイズ計算・ダウンロードのどの段階で失敗しても、その段階を `_maxRetry` 回まで、`_retryDelayMs` の待機（`AsyncCompat.Delay`。実時間）を挟んでやり直す。リトライ回数は**段階ごとに数え**、段階が進むと `AccRetryCount` は 0 に戻る
- リトライのたびに `AccRetryCount` を 1 増やし、通知する。待機の間も段階は変わらない
- リトライを使い切ったら `AddressableDownloadException`（失敗した段階 `AccPhase`・リトライ回数・元の例外を `InnerException` に持つ）を呼び出し元の `await` に返し、段階は `Failed` になる。**失敗の例外は `DownloadStatus` には持たせない**（受け取る経路を呼び出し元の `await` の1つにするため）。進捗の受け取り側は `AccPhase == Failed` で失敗を判定し、原因に応じた処理は呼び出し元の `catch` で行う
- 初期化の待機で失敗した場合は、リトライせず、`AddressableDownloadException` に包まずに初期化の例外をそのまま返す（5章）。段階は `Failed`

**キャンセル**
- `ct` のキャンセルは、呼び出し元の待機を `OperationCanceledException` で終わらせ、段階を `Canceled` にし、handle を解放する。次の呼び出しができる
- **開始済みの通信は止まらない場合がある**（Addressables にダウンロードの中断 API が無いため。10章）
- **ただし `UpdatingCatalog`（カタログの更新中）は、`ct` で待機を打ち切らない**。Addressables の更新は途中で止められず、待機だけをやめて `Canceled` を返すと、返した後に裏でカタログが更新され、利用者の判断（キャンセルしたので更新されていない）と実際の状態がずれるため。`ct` のキャンセルは、更新が終わった後に反映される（`OperationCanceledException`・`Canceled`。**カタログは更新済み**で、次の段階〔サイズ計算〕は始めない）。更新を始める前のキャンセルは、更新を始めずに終える
- `UpdatingCatalog` でも、**Manager の破棄（`Dispose`）では、更新の完了を待たずに終わる**。更新が失敗してリトライを待っている間は、Addressables の操作が走っていないため、`ct` ですぐ打ち切れる

**同時実行**
- 確認・判定・ダウンロードは**同時に1つしか実行できない**。実行中に呼ぶと `InvalidOperationException`（実行中の処理は影響を受けない）。UI から連打されたときの扱い（ボタンの無効化等）は、利用者側の責務

**Manager の破棄**
- `OnDestroy` で DownloadController は `Dispose` され、実行中なら `Canceled` を通知してから通知先を外し、待機中の呼び出し元は `OperationCanceledException` で終わる。破棄後の呼び出しは `ObjectDisposedException`
- 順序は、**イベントの購読解除 → LoadingController の `Dispose` → DownloadController の `Dispose` → InitializeController の `Dispose` → URL 変換関数の復元**。Download は初期化の待機を持つため、Initialize より先に止める

**呼び出しで起きる例外**

| 状況 | 例外 |
|---|---|
| Manager の破棄後 | `ObjectDisposedException` |
| 引数なしの API で SO の Label が空 | `InvalidOperationException`（何もしないで成功させると、設定漏れに気づけないため） |
| 引数の Label が `null`／空 | `ArgumentNullException`／`ArgumentException` |
| 別の処理の実行中 | `InvalidOperationException` |
| 初期化が未開始（手動モード）／失敗済み | `InvalidOperationException`／`AddressableInitializeException` |
| `ct` のキャンセル | `OperationCanceledException` |
| リトライを使い切った | `AddressableDownloadException` |

引数・設定の検証は、処理を始める前（状態を変える前）に行う。

---

## 3. 設定項目（`AddressableDownloadSettings`）

`Runtime/Core/Download/Data/AddressableDownloadSettings.cs`（`[Serializable]`。namespace `MAH.AddressableManager.Core.Download`）。Runtime SO が `_download` として保持し、Inspector では「ダウンロード」の見出しの下に表示される。`AddressableLoadingSettings` と同じ作り（Unity のシリアライズ用の引数なしコンストラクタ、テスト用の値指定コンストラクタ、`ClampValues()`）にしている。

| 項目 | 型 | 既定値 | 意味 |
|---|---|---|---|
| `_catalogPolicy` | `CatalogUpdatePolicy` | `CheckAndUpdate` | カタログ更新の方針（2.1） |
| `_labels` | `string[]`（`[AddressableLabel]`） | 空 | 引数なしの API で対象にする Label。Inspector のプルダウンで選べる |
| `_maxRetry` | `int` | 3 | 失敗時のリトライ回数。`ClampValues()` で 0〜10 に補正する |
| `_retryDelayMs` | `int` | 1000 | リトライ前の待機（ミリ秒）。`ClampValues()` で 0〜60000 に補正する |
| `_autoCleanBundleCache` | `bool` | `false` | カタログ更新時に、使われなくなった古いバンドルのキャッシュを消すか（`UpdateCatalogs` の `autoCleanBundleCache`） |

- `CatalogUpdatePolicy`（`None` / `CheckOnly` / `CheckAndUpdate`）は Definition 層で、同じ `Data/` フォルダに置く。SO の設定でしか使わないため内部実装型（`MAH.AddressableManager.Core.Download`）
- `_autoCleanBundleCache` の既定が `false` なのは、読み込み中のバンドルとの兼ね合いを実機で確認できていないため（10章）
- 機能の設定は Runtime SO が束ね、DownloadController には `AddressableDownloadSettings` だけを渡す（設計仕様書 3.1.1）

---

## 4. 公開 API と使い方

`IAddressableHandler`（`MAH.AddressableManager`。`Runtime/Core/Interfaces/`）に以下を公開する。Manager には公開しない。利用者は `AddressableManager.AccInstance.AccHandler` から取得する。

```csharp
public interface IAddressableHandler
{
    // 初期化・読み込み・解放のメンバーは initialize-spec.md 4章・loading-spec.md 8章。ここではダウンロードのメンバーだけを示す
    /// <summary>
    /// 取得した時点の、最新の処理（確認・判定・ダウンロード）の状態のコピー。値は後から変わらないため、必要なたびに取得し直す。
    /// 一度も処理していなければ AccPhase == None
    /// </summary>
    DownloadStatus AccDownloadStatus { get; }

    /// <summary> 更新データがあるかどうかだけを判定する。カタログは更新しない </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
    UniTask<bool> HasUpdateAsync(CancellationToken ct = default);
    UniTask<bool> HasUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct = default);
#else
    Task<bool> HasUpdateAsync(CancellationToken ct = default);
    Task<bool> HasUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct = default);
#endif

    /// <summary> カタログを確認・更新し、ダウンロードが必要な件数と容量を返す </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
    UniTask<DownloadCheckResult> CheckUpdateAsync(CancellationToken ct = default);
    UniTask<DownloadCheckResult> CheckUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct = default);
#else
    Task<DownloadCheckResult> CheckUpdateAsync(CancellationToken ct = default);
    Task<DownloadCheckResult> CheckUpdateAsync(IReadOnlyList<string> labels, CancellationToken ct = default);
#endif

    /// <summary> 対象 Label のアセットをダウンロードする。progress には段階・バイト数・割合・リトライ回数が届く </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
    UniTask DownloadAsync(IProgress<DownloadStatus> progress = null, CancellationToken ct = default);
    UniTask DownloadAsync(IReadOnlyList<string> labels, IProgress<DownloadStatus> progress = null, CancellationToken ct = default);
#else
    Task DownloadAsync(IProgress<DownloadStatus> progress = null, CancellationToken ct = default);
    Task DownloadAsync(IReadOnlyList<string> labels, IProgress<DownloadStatus> progress = null, CancellationToken ct = default);
#endif
}
```

- 引数なしの版は SO の `_labels` を対象にする。引数に `labels` を渡す版は、SO の設定を上書きする（ゲーム内の追加ダウンロード用）
- **`ct` は呼び出し元の待機を打ち切る**。キャンセルすると段階は `Canceled` になる（2.5）。**カタログの更新中（`UpdatingCatalog`）だけは、更新が終わってから反映される**（`Canceled` でもカタログは更新済みになり得る）
- Label を渡す版に `null` を渡すと `ArgumentNullException`。**呼び出し時には投げず、`await` の時点で届く**（Controller の検証エラーと同じ。Handler が失敗した Task / UniTask を返す）
- **`DownloadAsync(null)` は、`IProgress` 版と `IReadOnlyList` 版のどちらにも当てはまり、コンパイルエラー（CS0121）になる**。進捗だけを渡すときは `progress:`、Label だけを渡すときは `labels:` の名前付き引数を付ける（`DownloadAsync(progress: _receiver, ct: ct)` のように）
- `HasUpdateAsync` / `CheckUpdateAsync` は `IProgress` を受け取らない。実行中の状態は `AccDownloadStatus` で読める

### 4.1 進捗の受け取り方（`IProgress<DownloadStatus>` を推奨）

**進捗は `IProgress<DownloadStatus>` で受け取ることを推奨する**。

- 段階・バイト数・割合・リトライ回数を、1つの型で受け取れる
- `InitializeAsync` と同じ「`IProgress<T>` を引数で渡す」形で、イベントのように購読と解除を対で書く必要が無い。解除漏れが起きない
- 呼んだ人にだけ、その回の処理の通知が届く（通知先は処理の終わりに必ず外れる。7.3）
- 届く値は通知した時点の値のコピーなので、`System.Progress<T>` のように後のフレームで処理されても、通知した時点の値のまま読める

**最後の通知で、成功・失敗・キャンセルを区別できる**。処理の終わりには、通知先を外す前に1回だけ、`AccPhase` が `Completed` / `Failed` / `Canceled` のどれかになった `DownloadStatus` が届く（7.3）。

`AccDownloadStatus` で取得する方法は、**`IProgress` を渡していない場所**（呼び出し元とは別の UI など）から、処理中の状態を参照したいときの補助である。取得するのは取得した時点の値のコピーなので、**必要なたびに取得し直す**（UI の `Update` で毎回読むなど）。プロパティを数段たどって構造体をコピーするだけで、GC アロケーションは起きず、毎フレーム呼んでも負荷は無視できる。

### 4.2 利用例

```csharp
var handler = AddressableManager.AccInstance.AccHandler;

// 更新があるかどうかだけで、ボタンの表示を切り替える
bool has_update = await handler.HasUpdateAsync(ct);
_updateButton.SetActive(has_update);

// 確認 → 件数と容量を表示 → 同意されたらダウンロード
var result = await handler.CheckUpdateAsync(ct);
if (result.AccHasDownload && await ShowConfirmDialogAsync(result.AccBundleCount, result.AccTotalBytes))
{
    try
    {
        // UniTask 導入時は、Progress.Create<DownloadStatus>(...) でラムダ1つで書ける。
        // 導入していない場合は、IProgress<DownloadStatus> を実装したクラス（Action を包むもの）を渡す
        await handler.DownloadAsync(_progressReceiver, ct);
    }
    catch (AddressableDownloadException ex)
    {
        ShowRetryDialog(ex); // ex.AccPhase に失敗した段階、InnerException に元の例外
    }
}

// 別の UI から、処理中の状態を毎フレーム読む
private void Update()
{
    var status = AddressableManager.AccInstance.AccHandler.AccDownloadStatus; // 毎回取得する
    _bar.gameObject.SetActive(status.AccIsDownloading);
    _bar.value = status.AccPercent;
}
```

**`AccDownloadStatus` をループで読むときの注意**：
1. ループの中で必ず `await`（`UniTask.Yield` 等）を挟む。挟まないとメインスレッドが止まり、`Tick` が走らず値も更新されない
2. 値の更新は `LateUpdate` で1フレームに1回のため、`Update` のタイミングで読むと1フレーム前の値になる（実害は無い）
3. **終わる条件は `AccPhase` ではなく、ダウンロードのタスクの完了にする**。始める前に読んだ値は、前回の処理の `Completed` のままで、即座にループを抜けてしまう

```csharp
var task = handler.DownloadAsync(ct: ct);
while (!task.Status.IsCompleted())
{
    _bar.value = handler.AccDownloadStatus.AccPercent; // 毎回取得する
    await UniTask.Yield(ct);                             // 必ず await を挟む
}
await task;                                              // 例外はここで受け取る
```

---

## 5. 初期化との関係

確認・判定・ダウンロードは、**初期化の成功を待ってから**始まる。`LoadBeforeInitializePolicy`（Loading の設定）には関係なく待つ。Addressables のカタログ確認は初期化済みであることが前提で、初期化が失敗した後は、Addressables 全体が使えないため。

| 初期化の状態 | 動作 |
|---|---|
| `NotStarted`（手動モード） | **`InvalidOperationException`**（`InitializeAsync` の呼び忘れにすぐ気づけるようにする。初期化を始めることはしない） |
| `Running` | 初期化の完了を待ってから始める |
| `Succeeded` | そのまま始める |
| `Failed` | `AddressableInitializeException`（Addressables を呼ばない） |

- 待機中に `ct` がキャンセルされたら `OperationCanceledException`
- **初期化の待機中の段階は、その処理の最初の段階**になる（`CheckUpdateAsync` は `CheckingCatalog`、`DownloadAsync` は `CalculatingSize`、`HasUpdateAsync` は方針が `None` なら `CalculatingSize`、それ以外は `CheckingCatalog`）。処理を始めた時点で、前の処理の結果（`Completed` 等）を上書きし、`AccIsBusy` が `true` になる
- 初期化の待機で例外になった場合、段階は `Failed`（キャンセルなら `Canceled`）になり、最後の通知が届く。返す例外は `AddressableDownloadException` に包まず、初期化の例外をそのまま返す
- 自動モードでは `Awake` の時点で `Running` になるため、`NotStarted` の行は手動モードでのみ起きる
- 待つ仕組みは `IInitializationGate.WaitUntilInitializedAsync`（7.4）。DownloadController は InitializeController を知らない

---

## 6. 確認だけで更新しない場合の Addressables の挙動

`HasUpdateAsync`・`CheckUpdateAsync`（`CheckOnly`）は、カタログの更新確認だけを行い、更新しない。このとき Addressables 側で何が起きるかを、Addressables 2.9.1 のソースで確認した結果を記す（10.1）。

- `CheckForCatalogUpdates` は、リモートの `.hash` を取得して手元のハッシュと比べ、違っていれば**メモリ上の「更新あり」フラグ**（`ResourceLocatorInfo.ContentUpdateAvailable`）を立てるだけ。**読み込みに使うカタログ・キャッシュ済みのカタログとハッシュのファイルは書き換えない**（書き換えるのは `UpdateCatalogs`）。そのため、確認だけして更新しなくても、Load は古いカタログのまま動く
- `CheckForCatalogUpdates` の `autoReleaseHandle` は、結果（更新のあるカタログ ID の一覧）を入れた操作の handle を解放する指定。**本モジュールは `false` で呼び、結果を読んだ直後に Service の `finally` で自分で解放する**（`true` にしても結果は同じで、他の handle と解放の経路をそろえるため）。カタログの取得・保持はしておらず、**解放してもフラグは消えない**。「取得したカタログを破棄すれば更新されない」わけではなく、**`UpdateCatalogs` を呼ばないから更新されない**
- **Addressables は、次の `CheckForCatalogUpdates` が呼ばれると、実行中の前回の操作の handle を解放する**（`AddressablesImpl.cs`）。利用者のコードや他のモジュールが、確認の途中で `Addressables.CheckForCatalogUpdates` を直接呼ぶと、本モジュールの確認の待機が終わらない場合がある（10章）
- **`UpdateCatalogs` を引数なしで呼ぶと、フラグが立っているカタログを更新する**。フラグが1つも立っていなければ、内部で確認してから更新する。本モジュールは `UpdateCatalogs` に、確認で得たカタログ ID を**明示して渡す**（`null` を渡さない）。**利用者が `Addressables.UpdateCatalogs()` を直接、引数なしで呼ぶと、`HasUpdateAsync` が立てたフラグの分が更新される**
- 副作用は1つで、`ContentCatalogProvider.DisableCatalogUpdateOnStart` が `false` になる。効くのは `LoadContentCatalogAsync` で後からカタログを読むときだけで、本モジュールは呼ばない

**Load が実際に失敗するのは、サーバー側で古いバンドルを消した・上書きした場合**。古いカタログが指す、まだキャッシュしていないバンドルを取得できなくなる。確認したかどうかには関係なく起きる。次の運用を推奨する。
- コンテンツ更新後も、古いバンドルをサーバーに残す
- 更新があれば、Load の前に `CheckUpdateAsync`（`CheckAndUpdate`）と `DownloadAsync` を行う
- カタログの更新中（`UpdatingCatalog`）に `LoadAsync` を走らせない（古い・新しいカタログのどちらで解決されるかが不定）。起動時（Load の前）に呼ぶ想定

---

## 7. 内部構成

クラス図は [`diagrams/download-class.md`](../diagrams/download-class.md) を参照。

### 7.1 クラスと責務

| クラス | 置き場所 | namespace | 責務 |
|---|---|---|---|
| `AddressableManager` | `Runtime/Core/` | `MAH.AddressableManager` | 合成ルート。`DownloadStatusModel` の生成と注入、DownloadController の生成・`Dispose`、`LateUpdate` の中継（`OnLateUpdate += / -=` を対で書く）。ロジックは持たない |
| `IAddressableHandler` | `Runtime/Core/Interfaces/` | `MAH.AddressableManager` | 利用者の唯一の入口（外部依存型）。ダウンロードの API（4章）を含む |
| `AddressableHandler` | `Runtime/Core/Handler/` | `MAH.AddressableManager.Core` | `IAddressableHandler` の実装。状態を持たず、`DownloadController` へ転送する。引数なし版は Label に `null` を渡し（Controller が SO の Label に解決する。Handler は設定を持たない）、引数あり版は `labels` が `null` なら、`ArgumentNullException` で失敗した Task / UniTask を返す（呼び出し時には投げず、`await` の時点で届く。Controller では `null` が「SO の Label を使う」を意味するため） |
| `DownloadController` | `Runtime/Core/Download/Controller/` | `MAH.AddressableManager.Core.Download` | 確認・判定・ダウンロードの実行、`DownloadStatusModel` を使った状態遷移、リトライ、進捗の通知、`Tick`、`Dispose`。Label の解決（`labels` が `null` なら SO の Label、空なら例外。2.5）と検証もここで行う。ピュア C# |
| `DownloadStatusModel` | `Runtime/Core/Download/Model/` | `MAH.AddressableManager.Core.Download` | 状態（段階・バイト数・リトライ回数）・実行中フラグ・進捗の通知先を持つ Model（7.2）。Addressables・設定は知らない。ピュア C# |
| `IAddressablesDownloadService` / `AddressablesDownloadService` | `Runtime/Core/Download/Service/` | `MAH.AddressableManager.Core.Download` | Addressables の呼び出し（`CheckForCatalogUpdatesAsync`・`UpdateCatalogsAsync`・`CalculateDownloadSizeAsync`・`StartDownloadAsync`・`GetDownloadBytes`・`ReleaseDownloadHandle`）。EditMode テストで偽物に差し替えるためにインターフェースを挟む。非同期の戻り値は、UniTask が導入されていれば UniTask、無ければ Task（共有タスクではないため）。handle の完了待ちは、どちらでも `TaskCompletionSource` で作った Task を await する |
| `DownloadSizeInfo` | `Runtime/Core/Download/Service/` | `MAH.AddressableManager.Core.Download` | Service → Controller の受け渡し用の struct（`BundleCount`・`TotalBytes`） |
| `AddressableDownloadSettings` と `CatalogUpdatePolicy` | `Runtime/Core/Download/Data/` | `MAH.AddressableManager.Core.Download` | 設定値（3章） |
| `DownloadPhase` / `DownloadStatus` / `DownloadCheckResult` | `Runtime/Core/Download/Data/` | `MAH.AddressableManager` | 段階・状態・確認結果。利用者が戻り値・引数として触るため外部依存型 |
| `AddressableDownloadException` | `Runtime/Core/` | `MAH.AddressableManager` | ダウンロードの失敗。利用者が `catch` するため外部依存型（`AddressableInitializeException` と同じ扱い） |
| `IInitializationGate` | `Runtime/Core/Interfaces/` | `MAH.AddressableManager.Core` | Download が「初期化の成功まで待つ」窓口（7.4）。どの機能にも属さない共通物 |

DownloadController に渡す設定は `AddressableDownloadSettings` だけ（Runtime SO を丸ごと渡さない。設計仕様書 3.1.1）。

### 7.2 状態の Model（`DownloadStatusModel`）

状態データを Controller から分けて持つ Model。**Controller の中で `new` せず、Manager の `Awake` で生成して DownloadController のコンストラクタに注入する**（Model は複数の Controller から参照され得る前提で配線するため。EditMode テストもテスト側で `new` して渡す）。

| 持つもの | 内容 |
|---|---|
| 状態 | 段階・バイト数（ダウンロード済み／合計）・リトライ回数のフィールド |
| 実行中フラグ | 同時実行を防ぐ（2.5） |
| 進捗の通知先 | `IProgress<DownloadStatus>`（1つ）。処理の終わりに必ず外す（7.3） |

- `AccStatus` は、**遷移のたびに作り直して保持する** `DownloadStatus`（`readonly struct`）を返す。アクセサの中で関数を呼ばない規則（`conventions.md` 2章）のため、取得のたびに作らず、遷移メソッドの中で作る。作るのは構造体で、スタック上のコピーのため、GC は起きない。利用者は構造体のコピーを受け取るため、後から値が変わらない。「前の処理の値」を誤って読む問題は構造上起きず、「無効」の段階も要らない
- 遷移はメソッドで行う：`TryBegin(first_phase, progress)`（実行中なら何もせず `false`。成功すると、値を 0 に戻し、段階を最初の段階にして、通知先を置き換え、開始を通知する。最初の段階は、処理中の段階のみ）・`MarkPhase`（段階が変わったときだけ通知し、リトライ回数を 0 に戻す）・`SetBytes`・`IncrementRetry`・`MarkCompleted`・`MarkFailed`・`MarkCanceled`・`End`。遷移メソッドは、フィールドを書き換えて `DownloadStatus` を作り直してから、通知先へ届ける
- **結果（`Completed` / `Failed` / `Canceled`）が記録された後と、実行中でないときの遷移は、何も変えず `false` を返す**（最後の通知を1回だけにするため。たとえば、失敗と `Dispose` が重なっても、最後の通知は1回）
- `End` は、**通知先を外し、実行中を解除する**。結果は残る。最後の通知を届けた後に呼ぶ。外す前に実行中を解除しないのは、最後の通知の中で次の処理が始められると、`End` がその通知先まで外してしまうため（終えるまでは、次の `TryBegin` は `false`）
- 通知先は利用者のコードのため、`Report` が例外を投げても `ReportProgress` が受け止めて `AddressableLogger.LogException` に流す（最後の通知で止まると、呼び出し元に完了・例外が返らなくなるため。`InitializeStateModel` と同じ理由）
- Model を直接触るのは DownloadController だけ。Handler の `AccDownloadStatus` は DownloadController を通して読む（隣の層とだけ話す。設計仕様書 2章）

### 7.3 最後の通知と通知先の解除

処理の終わりの順序は次のとおり。

1. 結果の段階を記録する（`MarkCompleted` / `MarkFailed` / `MarkCanceled`）
2. `progress.Report`（`AccPhase` が `Completed` / `Failed` / `Canceled` のどれかになった `DownloadStatus` を届ける）
3. `finally` で通知先を外し、実行中を解除する（`End`）。完了・失敗・キャンセル・`Dispose` のすべての経路で行う
4. 呼び出し元へ、完了または例外を返す

- **通知先を外す前に最後の通知を届ける**。外した後では通知が届かず、成功・失敗・キャンセルを区別できなくなるため。呼び出し元に例外が返るより前に届く
- 通知先は常に1つだけで、次の処理を始める `TryBegin` でも置き換える。前の処理の `IProgress` が、次の処理の通知を受けることはない
- `Dispose` のときは、実行中なら `Canceled` を通知してから通知先を外す。Controller が内部に持つ `CancellationTokenSource` を、呼び出しごとの `ct` と連結して待機に使い、`Dispose` でキャンセルする（呼び出しごとの確保であって、毎フレームではない）
- `HasUpdateAsync` / `CheckUpdateAsync` は `IProgress` を受け取らず、通知先を持たない。状態（段階・`AccDownloadStatus`）は同じように更新される

### 7.4 初期化との橋渡し（`IInitializationGate`）

```csharp
// Runtime/Core/Interfaces/IInitializationGate.cs（namespace MAH.AddressableManager.Core。Download に関するメンバーのみ）
internal interface IInitializationGate
{
    /// <summary> 初期化の成功まで待つ。LoadBeforeInitializePolicy に関係なく待つ。未開始は InvalidOperationException、失敗は AddressableInitializeException </summary>
    Task WaitUntilInitializedAsync(CancellationToken ct);
}
```

- InitializeController が実装し、Manager が DownloadController のコンストラクタに注入する。DownloadController が知っているのはこのインターフェースだけで、`MAH.AddressableManager.Core.Initialize` を `using` しない
- 戻り値は、Task / UniTask のシンボルに関係なく常に `Task`（共有タスクを待つため。設計仕様書 3.5.1）
- 状態ごとの動作は5章の表のとおり（成功済み→すぐ完了／実行中→共有タスクの完了を待つ／未開始→`InvalidOperationException`／失敗→Model が持つ例外）。`WaitUntilOpenAsync`（Loading 用）とは別で、**`Failed` 以外では PassThrough でも待つ**点が異なる
- EditMode テストでは、偽のゲート（`FakeInitializationGate`）に同じメソッドを持たせる

### 7.5 handle の扱いと Service

- `DownloadDependenciesAsync` は **`autoReleaseHandle: false`** で呼び、`Tick` でバイト数を読めるようにする（Service の `GetDownloadBytes`。内部で handle の `GetDownloadStatus()` を読み、ダウンロード中でなければ最後に読んだ値を返す）。**完了・失敗の経路は Service の `try/finally`、キャンセル・`Dispose` の経路は `ReleaseDownloadHandle()` で、`Addressables.Release` する**。待機をやめた古い呼び出しが後から再開しても、新しいダウンロードの handle を解放しないよう、渡された handle と保持している handle が一致するときだけ保持を外す。同じ理由で、**古い呼び出しは、完了後の `GetDownloadStatus()` の読み取りと、最後に読んだバイト数の書き込みもしない**（解放済みの handle を読む例外と、新しいダウンロードの値の上書きを避けるため。handle が有効で、保持しているものと一致するときだけ行う）。利用者から handle は見えない
- **完了したら、すぐ解放する**。`DownloadDependenciesAsync` の操作は、ダウンロードしたバンドルを handle を解放するまで読み込んだまま持ち、解放するまではそのバンドルからアセットを Load できない（Addressables 2.9.1 の `DownloadDependenciesAsync` のドキュメントコメント「The returned handle must be released before Assets can be loaded from the downloaded bundles.」）。解放しても、ダウンロードしたファイルはキャッシュに残る
- 確認（`CheckForCatalogUpdates`）・更新（`UpdateCatalogs`）・サイズ計算（`LoadResourceLocationsAsync`）の handle も、結果を読んだらすぐ解放する
- 結果（成功・失敗・例外）は、**`Completed` コールバックの中で読み**、`TaskCompletionSource` に渡す（`AddressablesInitializeService` と同じ形）。`AsyncCompat.ToTask` は UniTask 版では定義されないため使わない
- 呼んだ時点で操作が完了済み（`IsDone`）なら、`Completed` を待たずにその場で結果を読む
- Addressables が同期で投げた例外も、Service が捕まえて失敗した Task として Controller に渡す
- Service に渡すコールバックはフィールドにキャッシュした delegate を使い、Label の一覧を毎回 `ToList()` しない

### 7.6 進捗（`Tick`）

- Manager は `LateUpdate` から `OnLateUpdate`（`Time.unscaledDeltaTime`）を中継しており、DownloadController の `Tick(deltaTime)` もこれに購読する（`Awake` で `+=`、`OnDestroy` で `-=`。必ず対で書く）
- `Tick` は **`Downloading` のときだけ働き**、それ以外は先頭で戻る（ダウンロードしていないときは実質何も走らない）。Service から `GetDownloadBytes`（ダウンロード済み／合計のバイト数）を読み、Model を更新して、通知先へ通知する。**Service の合計が 0 以下のときは、サイズ計算で確定した合計を維持する**（`DownloadDependenciesAsync` は、ロケーションの取得が終わるまで合計が 0 のため。維持しないと、開始直後に表示が一瞬 `0 B / 0%` に戻る）
- 毎フレームの `await` ループは書かない（`InitializeController.Tick` と同じ形）

### 7.7 件数の数え方

`Addressables.GetDownloadSizeAsync` は合計バイトしか返さず、バンドル数が分からない。そのため Service が、同じ判定を自前で行う。

1. `Addressables.LoadResourceLocationsAsync(labels, MergeMode.Union)` で、Label に該当するロケーションを取得する
2. 各ロケーションの依存（`Dependencies`）をたどり、`ILocationSizeData` を持つバンドルのロケーションを、重複なく集める
3. `ComputeSize(location, ResourceManager)` が 0 より大きいもの（まだキャッシュに無いもの）を数え、合計を求める

- Addressables の内部実装に近い API を使うため、Addressables を更新したときの確認項目になる（9.5）
- Editor の「Use Asset Database」モードではバンドルが無く、常に 0 件

---

## 8. 設計の判断と理由

| 判断 | 理由 |
|---|---|
| 利用者の入口を `IAddressableHandler` に集約する（別の Handler は作らない） | 利用者が触れる入口を1つに絞るため（設計仕様書 3.3・3.7） |
| 更新の確認（`CheckUpdateAsync`）とダウンロード（`DownloadAsync`）を分ける | 間に、件数と容量を見せる確認ダイアログを挟めるようにするため。ダウンロードの前に、カタログを更新するかどうかを設定で選べる |
| `DownloadAsync` はカタログを更新しない | 確認と更新が別の処理になり、ダウンロードの途中でカタログが変わることを避けられる。更新は `CheckUpdateAsync` の役目 |
| `HasUpdateAsync` を別に用意する | 画面の表示を切り替えるだけの用途では、件数と容量が要らない。カタログに更新があれば、サイズの計算を飛ばして通信を減らせる |
| 進捗の型を `IProgress<float>` から `IProgress<DownloadStatus>` に変える | 段階（確認中・更新中・サイズ計算中・ダウンロード中）・バイト数・リトライ回数も通知するため。割合だけでは、リトライ中・サイズ計算中の表示ができない。`InitializeAsync` と同じ「引数で渡す」形のため、利用者の書き方は変わらない（設計仕様書 6章） |
| 進捗の通知をイベントにしない | `+=` と `-=` を対で書く必要があり、解除漏れが起きる。どの呼び出しの進捗か区別もできない |
| `DownloadStatus` を `readonly struct` にして、取得・通知のたびにコピーを渡す | 取得した値が後から変わらないため、前の処理の結果を表示している側が、次の処理で 0 に戻った値を誤って読むことが構造上起きない。「無効」の段階を持たずに済み、GC も起きない。失うのは「1回取得して持てば更新され続ける」手軽さだけで、`Update` で毎回 `AccDownloadStatus` を読む形で足りる |
| 状態を `DownloadStatusModel` に分ける | 状態データ（段階・バイト数・実行中・通知先）を Controller の処理と分け、遷移の条件を Model の遷移メソッドで守るため。Initialize・Loading と同じく Manager から注入する |
| ダウンロードの実行は同時に1つだけにする | 状態を1つに保つため。複数の呼び出し元が待つ共有タスクを作らずに済み、`Task` と UniTask の差も生まれない |
| 最後の通知を、通知先を外す前に届ける | 外した後では通知が届かず、成功・失敗・キャンセルを区別できなくなるため（7.3） |
| 失敗の例外を `DownloadStatus` に持たせない | 受け取る経路を呼び出し元の `await` の1つにするため。控えた値に例外とスタックトレースが残るのも避ける |
| リトライを段階ごとに数える | カタログの確認・更新で使った回数が、ダウンロードのリトライ回数を減らさないようにするため。UI には、現在の段階での再試行の回数が見える |
| 件数を自前で数える | `GetDownloadSizeAsync` は合計バイトしか返さず、「N 件」を表示できないため（7.7） |
| 初期化の成功を待つ（初期化を始めはしない） | 手動モードの意味（利用者が初期化のタイミングを決める）を保つため。未開始なら例外にして、`InitializeAsync` の呼び忘れにすぐ気づけるようにする |
| 引数なしで SO の Label が空なら例外にする | 何もしないで成功させると、設定漏れに気づけないため |
| `_autoCleanBundleCache` の既定を `false` にする | 読み込み中のバンドルを消してしまう恐れがあり、実機で確認できるまでは安全側にするため |
| カタログの更新中（`UpdatingCatalog`）だけ、`ct` で待機を打ち切らない | Addressables の更新は途中で止められない。待機だけをやめて `Canceled` を返すと、返した後に裏でカタログが更新され、利用者の判断と実際の状態がずれる。更新の完了を待ってから `Canceled` にすれば、返った時点で状態が確定する。`Dispose` では打ち切る（破棄を更新の完了待ちで止めないため）。リトライの待機中は Addressables の操作が走っていないため、`ct` で打ち切れる |
| 起動時の自動ダウンロードを持たない | 容量の確認ダイアログを挟めなくなるため。必要になったら SO の設定として追加する（9.3） |

---

## 9. 改修ガイド

### 9.1 設定項目を追加したい

1. `AddressableDownloadSettings` にフィールドとアクセサを足す（命名は `conventions.md` 1章）。範囲のある値は、`ClampValues()` で補正する（Runtime SO の `OnValidate` とテスト用コンストラクタから呼ぶ）
2. テスト用コンストラクタの引数に足す
3. DownloadController で使う。**Runtime SO や他の機能の設定を参照しない**
4. 本書3章の表を更新する

### 9.2 段階（`DownloadPhase`）を追加したい

1. `DownloadPhase` に値を足す。`AccIsBusy` の判定に含めるか決める
2. `DownloadStatusModel` の遷移と、DownloadController の流れに、段階の変更（`MarkPhase`）を足す
3. 本書2.4の表・図・テストを更新する。**`Completed` / `Failed` / `Canceled` が最後の通知で届く順序（7.3）は崩さない**

### 9.3 起動時の自動ダウンロードを加えたい

Manager が、初期化の完了後に DownloadController を呼ぶ形にする。**DownloadController に処理を足さず、Manager 側で順序を組む**（Controller 同士は互いを参照しないため、順序の保証は Manager の役目）。容量の確認ダイアログを挟めなくなるため、SO の設定（既定は OFF）として追加し、確認が不要なプロジェクトだけが選べるようにする。設定を足すときは9.1に従う。

### 9.4 `AssetKey` 単位のダウンロードを加えたい

`IAddressablesDownloadService` の `CalculateDownloadSizeAsync`・`StartDownloadAsync` が受け取る対象を、Label の一覧から、`AssetKey` も受け取れる形に広げる（`DownloadDependenciesAsync` は `IEnumerable<object>` のキーを受け取れる）。件数の数え方（7.7）はロケーションから数えるため、キーからロケーションを取得する部分だけを変えればよい。`AssetKey` は Path 入口と Reference 入口で別キーになる仕様（設計仕様書 3.6）に注意する。

### 9.5 Addressables を更新したとき

次の挙動が変わっていないか確かめる。
- `CheckForCatalogUpdates` が、カタログとハッシュのファイルを書き換えず、「更新あり」のフラグを立てるだけであること（6章）
- `UpdateCatalogs` を引数なしで呼ぶと、フラグが立っているカタログを更新すること（6章）
- `ILocationSizeData.ComputeSize` と、`GetDownloadSizeAsync` の判定が同じであること（7.7）
- `DownloadDependenciesAsync` の `GetDownloadStatus()` が、ダウンロード中のバイト数を返すこと

### 9.6 改修時に守ること

- DownloadController・Model・Service はピュア C# に保つ（`MonoBehaviour` を継承しない。Unity のライフサイクルは Manager から中継する）
- `DownloadStatusModel` は Controller の中で `new` しない（Manager から注入する）
- 非同期は UniTask が正で、UniTask が無い場合だけ Task を使う。違いは、公開 API のシグネチャの `#if ADDRESSABLE_UNITASK_SUPPORT` と `AsyncCompat` に閉じ込め、UniTask を `#if` の外で `using` しない。`IInitializationGate`・Service の共有的な戻り値は、UniTask 導入時も `Task` のままにする（設計仕様書 3.5.1）
- 時間待ちは `AsyncCompat.Delay`、キャンセル付き待機は `AsyncCompat.WithCancellation` を使う（`UniTask.Delay` を直接呼ぶと、ポーズ中〔`timeScale = 0`〕に待機が終わらない）
- `Core/Download/` から `Core/Initialize/`・`Core/Loading/` を `using` しない。共有が必要なものは `Core/` 直下に置く
- 最後の通知は、通知先を外す前に届ける（7.3）。すべての経路で handle を解放する（7.5）
- `UpdateCatalogs` に `null` を渡さない（6章）
- イベントの購読（`OnLateUpdate`）は、`Awake`／`OnDestroy` で必ず対にする

---

## 10. 注意点・既知の制約

- **キャンセルしても通信は止まらない場合がある**：Addressables にはダウンロードの中断 API が無い。`ct` で止まるのは呼び出し元の待機で、handle は解放するが、開始済みの通信は裏で続く場合がある
- **確認だけで更新しなくても、Load は古いカタログのまま動く**。ただしサーバー側で古いバンドルを消すと、Load が失敗する（6章）
- **`Addressables.UpdateCatalogs()` を利用者が引数なしで直接呼ぶと、確認で立ったフラグの分が更新される**（6章）
- **同時実行の禁止**：確認・判定・ダウンロードを並行に呼ぶと `InvalidOperationException`。UI の連打への対応は利用者側の責務
- **`DownloadAsync` の実行中に、対象のアセットを Load しない**：ダウンロードの操作がバンドルを読み込んだまま持っている間は、そのバンドルからアセットを Load できない（7.5）。`DownloadAsync` の完了を待ってから Load する
- **カタログの更新中（`UpdatingCatalog`）は、`ct` のキャンセルが更新の完了後に反映される**。`Canceled` でも、カタログは更新済みになり得る（2.5）
- **確認の途中で、他のコードが `Addressables.CheckForCatalogUpdates` を直接呼ぶと、本モジュールの確認の待機が終わらない場合がある**（Addressables が前回の操作を解放するため。6章）。確認は、本モジュールの API だけで行う
- **`CheckOnly` の件数・容量は、保留中の更新を含まない**（2.1）
- **Editor の「Use Asset Database」モードでは、バンドルが無いため、常に 0 件**。実際のダウンロードを確かめるには、ローカルの HTTP サーバー等でリモート配信を再現する（11.2）。Local のグループは、Use Existing Build でも、ビルド済みの手元のファイルから読むため、ダウンロードの対象にならない
- **ダウンロード中のリトライ後は、残りだけを数え直す可能性がある**：割合が小さくなることがある（10.1で未確認）
- **`_autoCleanBundleCache` は既定で `false`**。`true` にすると、使われなくなった古いバンドルのキャッシュが消える。読み込み中のバンドルとの兼ね合いは実機で確認していない

### 10.1 Addressables の挙動の確認結果

実装時に確認し、結果をここに記録する（未確認の間は「未確認」と書く）。

| 確認する点 | 結果 | 本機能への影響 |
|---|---|---|
| `CheckForCatalogUpdates` が、カタログ・ハッシュのファイルを書き換えるか | **書き換えない**（2026-10-08 Addressables 2.9.1 のソース `CheckCatalogsOperation.cs` で確認）。リモートの `.hash` を取得し、メモリ上の `ContentUpdateAvailable` を立てるだけ | 確認だけで更新しなくても Load が古いカタログのまま動く（6章） |
| `UpdateCatalogs` を引数なしで呼ぶと、フラグが立っているカタログを更新するか | **更新する**（同 `AddressablesImpl.cs` で確認）。フラグが無ければ内部で確認してから更新する | `UpdateCatalogs` に確認で得た ID を明示して渡す（6章） |
| `CheckForCatalogUpdates` の `autoReleaseHandle` が解放するもの | 結果を入れた操作の handle だけ。フラグは消えない（同上）。本モジュールは `false` で呼び、自分で解放する | 6章 |
| 確認の途中で、もう一度 `CheckForCatalogUpdates` を呼ぶとどうなるか | **実行中の前回の操作の handle が解放される**（2026-10-08 Addressables 2.9.1 の `AddressablesImpl.cs` で確認） | 外部から確認が呼ばれると、本モジュールの確認の待機が終わらない場合がある（10章） |
| `ComputeSize` を使った件数が、`GetDownloadSizeAsync` と一致するか | 未確認（実機・リモート配信環境が必要） | 一致しない場合、7.7 の数え方を見直す |
| 失敗後に `DownloadDependenciesAsync` を再び呼ぶと、ダウンロード済みの分をスキップするか | 未確認 | リトライの設計（2.5）は、ダウンロード済みのバンドルがキャッシュに残る前提。割合の変化は10章 |
| ダウンロード中に handle を解放すると、通信が止まるか | 未確認（Addressables に中断 API は無い） | 10章の注意点 |
| `DownloadDependenciesAsync` の handle を解放するまで、バンドルを読み込んだまま持つか | **持つ**（2026-10-08 Addressables 2.9.1 の `Addressables.cs` のドキュメントコメントで確認。解放するまで、そのバンドルからアセットを Load できない） | 完了したらすぐ解放する（7.5）。実行中に対象のアセットを Load しない（10章） |

---

## 11. テスト

| 種類 | 場所 | 確認すること |
|---|---|---|
| EditMode | `Tests/Editor/` | 偽の `IAddressablesDownloadService` と偽のゲートで `DownloadController` を動かす：方針ごとの Service の呼ばれ方と `DownloadCheckResult`、`HasUpdateAsync`（カタログに更新あり→サイズ計算なしで `true`／なし・バンドルあり→`true`／どちらも無し→`false`、`UpdateCatalogs` を呼ばない）、引数の Label の優先と空の Label の例外、0件でダウンロードを始めないこと、段階の順序と進捗、リトライ（段階ごと）と `AddressableDownloadException`、すべての経路での handle の解放、同時実行の例外、`ct` のキャンセルと `Canceled`、カタログの更新中の `ct` のキャンセル（更新の完了まで待ち、完了後に `Canceled`・サイズ計算は始めない／更新の前のキャンセルでは更新を始めない／`Dispose` では完了を待たない／リトライの待機中はすぐ終わる）、ダウンロードの開始直後に Service の合計が 0 でも確定した合計を維持すること、`DownloadStatus` が値のコピーであること、最後の通知が通知先を外す前に1回だけ届くこと（成功・失敗・キャンセル・`Dispose`）、前の `IProgress` が次の処理で呼ばれないこと、初期化の待機（未開始・失敗・待機中のキャンセル）、`Dispose`、通知先の例外の受け止め |
| EditMode | `Tests/Editor/` | `DownloadStatusModel`：遷移の条件、`TryBegin` の二重呼び出し、通知先の置き換えと解除、通知先の例外の受け止め、取得した値のコピー |
| EditMode | `Tests/Editor/` | `IInitializationGate.WaitUntilInitializedAsync`：成功済み・実行中・未開始・失敗の4状態（`PassThrough` 設定でも初期化の成功まで待つこと） |
| EditMode | `Tests/Editor/AddressableHandlerDownloadTests.cs` | Handler の転送：`AccDownloadStatus` が Controller の状態と一致すること、引数なし版が SO の Label を使うこと、Label を渡す版が優先されること、Label に `null` を渡すと、呼び出し時には投げず、`await` で `ArgumentNullException`（状態は変えない）、結果と進捗がそのまま届くこと |
| PlayMode | `Tests/Runtime/DownloadPlayModeTests.cs` | 実際の Addressables（Use Asset Database）で、`HasUpdateAsync` が `false`、`CheckUpdateAsync` が0件・0バイト、`DownloadAsync` が例外なく `Completed` で終わること。handle の解放漏れが無いこと（完了後に `Addressables.ResourceManager` の操作数が増えていないこと） |

実際のダウンロード（リモート配信）は、Use Asset Database では起きないため、ローカルの HTTP サーバーを使って手動で確かめる（11.2。確認項目は `.steering/20261008-addressable-download/design.md` 6章の表）。

既存の Initialize・Loading のテストは、偽のゲートに `WaitUntilInitializedAsync` を足すだけで、これまでどおり動く。

### 11.1 動作確認用サンプル

`Samples/AddressableDownload/`（`DownloadScene`）で、更新の有無の判定（Has Update）・確認（Check Update）・ダウンロードと進捗（Download）・キャンセル（Cancel）を、画面の操作で確かめられる。`Clear Cache` ボタンは、ダウンロードしたバンドルのキャッシュを消す、検証用の操作（モジュールの API ではない）。`DownloadSampleView` は、毎フレーム `AccDownloadStatus` を取得し直して表示し、`IProgress<DownloadStatus>` に最後に届いた値（Completed / Failed / Canceled）を別の欄に表示する。ボタンは、初期化が成功するまで無効にしている（`OnInitialized` の購読と、購読時の `AccIsInitialized` の確認）。実行中に別の操作を押すと、`InvalidOperationException` がログに出る。使い方の例としては `OnDownloadClicked`（呼び方と例外の扱い）と `Report`（進捗の受け取り）を参照する。

対象 Label は、Runtime SO（`DownloadSettings`。既定は `Manual`）の設定を使う。`DownloadSampleView` の `_labelsOverride` に Label を入れると、引数で渡す版に切り替わる。Use Asset Database では、ダウンロードが必要なものは常に 0 件になるため、実際のダウンロードは、対象 Label のグループを Remote にし、ローカルの HTTP サーバーで配信する環境で確かめる（11.2）。

### 11.2 リモート配信の再現手順（ローカルの HTTP サーバー）

Addressables 2.x（2.9.1 で確認）には、1.x の Hosting のウィンドウが無い。配信は、自前の HTTP サーバーで行う。サンプルの対象 Label `Manual` のエントリは、グループ `AddressableLoadTest` にある。

1. **グループを Remote にする**：Addressables Groups で、対象のグループを選び、Inspector の `Content Packing & Loading` で、Build Path を `Remote.BuildPath`、Load Path を `Remote.LoadPath` にする。Local のままだと、Use Existing Build でも、ダウンロードが必要なバンドルは 0 件になる
2. **リモートカタログを Remote にする**：Addressables Settings の `Catalog` で、`Build Remote Catalog` を ON にし、Build & Load Paths を `Remote` にする。カタログの更新（`Check Update`）を確かめるために必要
3. **プロファイルの `Remote.LoadPath` を設定する**：`Window > Asset Management > Addressables > Profiles` で、`Remote.LoadPath` を `http://localhost:8000/[BuildTarget]` にする（既定は `<undefined>`）
4. **ビルドする**：Groups の `Build > New Build > Default Build Script`。Remote のバンドルとカタログが、`Remote.BuildPath`（既定は `ServerData/[BuildTarget]`）に出力される
5. **HTTP サーバーを起動する**：`ServerData` のフォルダで、`python3 -m http.server 8000` を実行する。ブラウザで `http://localhost:8000/<BuildTarget>/`（macOS は `StandaloneOSX`）を開いて、バンドルとカタログのファイルが見えれば、配信できている
6. **Play Mode Script を Use Existing Build にして再生する**：`Check Update` でバンドル数と容量が 0 より大きくなり、`Download` で進捗が動く

- 一度ダウンロードしたバンドルはキャッシュに残るため、2回目以降は 0 件になる。繰り返し確かめるときは、サンプルの `Clear Cache` ボタン（`Caching.ClearCache()` を呼ぶだけ）でキャッシュを消す。ダウンロード中は受け付けない。読み込み中のバンドルがあると、消せずに失敗する
- `localhost` の通信は速く、数 MB のバンドルは、数十ミリ秒（1〜数フレーム）で終わる。進捗の途中経過・キャンセル・リトライを確かめるときは、通信を遅くした HTTP サーバーを使う。リポジトリルートの `Tools/slow_server.py` が、その HTTP サーバー（64KB ごとに 0.1 秒待つ。約 640KB/s。ポートは 8000）。サーバーは、**`cd` したフォルダを配信する**ため、スクリプトの場所ではなく、`ServerData` のフォルダで実行する

  実行方法（macOS）：① 先に起動していたサーバー（`python3 -m http.server 8000`）を `Ctrl + C` で止める（同じポートは使えない）。② ターミナルで、`ServerData` のフォルダに `cd` して、`python3 ../../../Tools/slow_server.py` を実行する（`ServerData` は `src/AddressableManager/` の下にあり、`Tools` はリポジトリルートにある。絶対パスで指定してもよい）。何も表示されずに待機状態になれば、起動している（止めるときは `Ctrl + C`）。③ ブラウザで `http://localhost:8000/<BuildTarget>/` を開いて、ファイルが見えることを確かめる。`python3: command not found` と出るときは、Python が入っていない。`Address already in use` と出るときは、①のサーバーが動いたままになっている。`can't open file` と出るときは、スクリプトのパスが違う

  この状態で、ダウンロード中に `Cancel` を押すと `Canceled` に、サーバーを止めるとリトライ回数が増えて、使い切ると `Failed` になる
- Unity が HTTP の通信を拒否したときは、Player Settings の `Other Settings > Allow downloads over HTTP` を `Always allowed` にする
- 実機で確かめるときは、`localhost` を、PC の LAN の IP アドレスにする
- 更新（`Check Update` の件数）を確かめるときは、アセットを変更して `Build > Update a Previous Build` を行い、HTTP サーバーのフォルダを入れ替える。変更分のバンドルだけが、ダウンロードの対象になる
