# ユビキタス言語定義（用語集）

> 最終更新日：2026-10-24 ／ 対象 Unity バージョン：6000.3.26f1
> 本書は `AddressableManager` で使用する用語を定義する。コード・図・ドキュメント（利用者向け／開発者向けの双方）の表記を本書に揃えることで、表記ゆれによる誤解を防ぐ。
> 運用ルール：①新しい用語（クラス名・enum・独自概念）を導入したら本表に追記する ②コード・図・ドキュメントの表記は本表に合わせる ③説明は1〜2文まで。設計の詳細・採用理由は `src/AddressableManager/Docs/addressable-manager-design.md` の該当章を参照する ④命名の**パターン**（書式・構造）は `src/AddressableManager/Docs/conventions.md` が正。本書は**語彙**を定める

**「公開」列の意味**：`外部` は利用者プロジェクトのコードが直接触れる語、`内部` はモジュール実装者のみが触れる語。区分基準は C# のアクセス修飾子ではなく「モジュール外部のコードが直接その型に依存するか」であり、`src/AddressableManager/Docs/conventions.md` 7.1 の外部依存型／内部実装型の区分と一致する。

> **For English readers:** This glossary is written in Japanese. The "英語" (English) column gives the official English term for each entry, and the "コード上の命名" column gives the name used in code. In the "公開" column, `外部` means External (touched directly by user code) and `内部` means Internal.

---

## 1. ドメイン用語（アセット管理の中核概念）

### 1.1 ロードと解放

| 日本語 | 英語 | コード上の命名 | 公開 | 説明 |
|--------|------|---------------|------|------|
| ロード／読み込み | Load | `LoadAsync<T>` | 外部 | アセットを読み込む操作。読み込んだアセットそのもの（`AssetTicket<T>.AccAsset`）をチケットと一緒に返し、型による挙動差を持たない（3.4.1） |
| 解放 | Release | `Release`, `ReleaseInternal` | 外部/内部 | アセットへの参照を手放す操作。「破棄」「回収」とは別概念であり混用しない（7章参照） |
| 明示解放 | Explicit Release | `Release(ticket)` | 外部 | 実装者が `Release` を呼んで解放すること。本モジュールの**主経路**であり、自動回収に依存させない |
| チケット | Ticket | `AssetTicket<T>` | 外部 | Load 1回ごとに発行される解放用の引換券。読み込んだアセット・`AssetKey`・`LifetimeScopeId`・通し番号を持ち、通し番号で一意になる（3.6） |
| まとめチケット | Group Ticket | `AssetGroupTicket` | 外部 | Label 一括ロード1回ごとに発行される解放用のチケット。中身のアセットを `TryGetAsset<T>` / `GetAssets<T>` で型ごとに取り出せ、`Release` 1回でまとめて解放される（3.4.2） |
| Label 一括ロード | Label Load | `LoadByLabelAsync` | 外部 | Label が付いたアセットをまとめて読み込む操作。Load のときに型は指定せず、型は取り出すときに指定する。Label にどの型が入っているかを利用者が把握しなくてよい（3.4.2） |
| アセットキー | Asset Key | `AssetKey` | 外部 | 「入口の種別＋値」からなる複合キー。ロードの同一判定に使う |
| 入口の種別 | Key Kind | `AssetKey.Kind`, `AssetKey.KeyKind`（`Path` / `Reference`） | 外部 | キーの由来。Path 入口と AssetReference 入口を区別する |
| Path 入口 | Path | `AssetKey.FromPath` | 外部 | Addressable のアドレス文字列でアセットを指定する経路 |
| AssetReference 入口 | Reference | `AssetKey.FromReference` | 外部 | `AssetReference`（GUID）でアセットを指定する経路。同一アセットでも Path 入口とは別キー扱いになる（3.6・仕様） |
| 参照カウント | Reference Count | `RefCount` | 内部 | 1つのキーが何回確保されているかの数。0 になった時点で実解放する |
| 重複ロード判定 | Dedup | — | 内部 | 同一の判定キー（`AssetKey` と型の組）の2回目以降のロードで実ロードを起こさず、既存エントリを使い回す判定 |
| 判定キー | Entry Key | `AssetEntryKey` | 内部 | Registry がエントリを引くキー。`(AssetKey, 型)` の組で、同じ `AssetKey` でも型が違えば別のエントリになる（3.4.2） |
| 進行中のロード | In-flight | `AssetEntryModel.LoadTask` | 内部 | 完了前のロード。重複呼び出しは handle 共有ではなく **LoadTask の共有**で吸収する（3.4） |
| 二重解放 | Double Release | — | 内部 | 同一の確保枠を2回解放してしまうこと。発行済みチケットの管理（通し番号）と `CountByScope` の減算済み判定で防ぐ（3.4） |
| 型違いのロード | Different-type Load | — | 内部 | ロード済みのキーを別の型で要求すること。判定キーに型を含めるため別のエントリとして読まれ、例外にはならない（以前は例外にしていたが、Label 一括ロードのために取り消した。5.3・6章） |
| 読み込み失敗 | Load Failure | `AssetLoadException` | 外部 | リトライを使い切ってもロードできなかったことを示す例外。`AssetKey` を持ち、最後の例外を `InnerException` に入れる（3.5） |
| 過剰解放 | Over-release | — | 内部 | 未登録のキーに対する解放呼び出し。警告ログで検出する |
| 解放漏れ／リーク | Leak | — | 外部 | `Release` を呼び忘れ、自動回収で回収された状態。エラーではないが是正対象とする（3.8） |
| リーク検出 | Leak Detection | — | 外部 | 明示解放されずに回収されたチケットを、確保時のスタックトレース付きで記録する機能。エディタ限定（3.8・3.9(3)） |
| 確保元 | Allocation Source | `LeakKey`（`LeakRegistryModel` の内部） | 内部 | リークを1行に集約する単位。単体は `AssetKey`・型・寿命の種類・確保時のスタックトレース、Label 一括は Label・寿命の種類・確保時のスタックトレースが同じもの。Owner の ID・Scene の handle は含めない（`editor-debug-window-spec.md` 8.2.3） |
| インスタンス化 | Instantiate | `Object.Instantiate` | 外部 | Prefab アセットからシーン上の実体を生成すること。**Manager の管理対象外であり実装者の責務**（3.4.1） |
| Prefab アセット | Prefab Asset | — | 外部 | `LoadAsync<GameObject>` が返すアセット本体。シーン上のインスタンスではない |
| プリロード | Preload | — | 外部 | 事前に一括ロードしておく運用。Label 一括ロード（`LoadByLabelAsync`）で行える。低レベル経路（`LoadingController`）の利用例のひとつ |
| ダウンロードサイズ | Download Size | `DownloadCheckResult.AccTotalBytes` | 外部 | 対象 Label の依存ダウンロードに必要な合計バイト数。`Addressables.GetDownloadSizeAsync` と同じ判定で、バンドル数もあわせて数える（3.3） |
| 依存ダウンロード | Dependency Download | `DownloadDependenciesAsync` | 内部 | アセットの依存バンドルを事前にダウンロードする処理 |
| カタログ更新 | Catalog Update | `CheckUpdateAsync`, `CatalogUpdatePolicy` | 外部 | カタログの更新有無の確認と適用。起動時とゲーム内の双方から同一の仕組みで呼べる（3.3） |

### 1.2 寿命と自動回収

| 日本語 | 英語 | コード上の命名 | 公開 | 説明 |
|--------|------|---------------|------|------|
| 寿命 | Lifetime | `AssetLifetime` | 外部 | Load 時に指定する「いつまで保持するか」の指定。終了時点で未解放のものは自動回収される |
| 寿命種別 | Lifetime Kind | `LifetimeKind` | 外部 | 寿命の種類。`Manual` / `Scene` / `Owner` の3種（3.8） |
| Manual 寿命 | Manual | `LifetimeKind.Manual` | 外部 | 自動回収なし。永続アセットやフェーズ単位の管理に使い、`Manager.Dispose` 時のみ強制解放される |
| Scene 寿命 | Scene | `LifetimeKind.Scene` | 外部 | Load 時のシーンが Unload された時点で回収される |
| Owner 寿命 | Owner | `LifetimeKind.Owner` | 外部 | 指定した GameObject が破棄された時点で回収される |
| オーナー | Owner | `GameObject` / `Component` | 外部 | Owner 寿命で指定する破棄検知の対象。ピュア C# クラスは指定できない（3.8） |
| 寿命スコープ | Lifetime Scope | `LifetimeScopeId` | 外部 | 寿命の実体を一意に識別する ID。各 `AssetLifetime` は内部でこれに解決される |
| 確保内訳 | Count by Scope | `AssetEntryModel.CountByScope` | 内部 | 寿命スコープごとの確保数。回収時に「解放し忘れた分」だけを回収するために必要 |
| 自動回収 | Auto Release / Reclaim | `LoadingController.ReleaseLifetime` | 外部 | 寿命スコープ終了時に未解放分を一括解放すること。**セーフティネットであり常用させない** |
| セーフティネット | Safety Net | — | 外部 | 解放忘れを必ず回収するための安全網。Watcher・ポーリング・`Manager.Dispose` の3段構え（3.8「多重の安全網」） |
| 破棄検知 | Destroy Detection | `AssetLifetimeWatcher.OnDestroy` | 内部 | owner の破棄を検知する仕組み。Unity が `OnDestroy` の呼び出しを保証することが安全網の根拠 |
| Watcher | Watcher | `AssetLifetimeWatcher` | 内部 | owner に `AddComponent` する内部専用の監視コンポーネント。実装者は何も継承・実装しない |
| ポーリング | Polling | `LoadingController.Tick` | 内部 | 登録済み owner を定期走査して破棄済みを回収する保険。既定3秒間隔（3.8） |
| 回収経路 | Reclaim Route | `OwnerReclaimRoute` | 内部 | Owner 寿命の回収が起きた入口。`OnDestroy`（Watcher の通知。主経路）と `Polling`（取りこぼしの回収）。ポーリング経由が多いほど Watcher が取りこぼしていることを示す（`editor-debug-window-spec.md` 8.3） |
| 偽 null | Fake Null | — | 内部 | 破棄済み `UnityEngine.Object` が `== null` で真を返す状態。ポーリングはこの判定で破棄を検出する |
| 強制解放 | Forced Release | `Manager.Dispose` 経路 | 内部 | アプリ終了・全体リセット時に全エントリを解放する最終防衛線 |
| フェーズ単位の管理 | Phase | — | 外部 | フェーズ境界での一括解放。回収タイミングを Unity が保証できないため独立した寿命種別とはせず `Manual` に統合した（3.8） |

### 1.3 状態管理

| 日本語 | 英語 | コード上の命名 | 公開 | 説明 |
|--------|------|---------------|------|------|
| エントリ | Entry | `AssetEntryModel` | 内部 | 1キー分の状態（Worker / LoadTask / RefCount / Status / CountByScope） |
| レジストリ | Registry | `AssetRegistryModel` | 内部 | エントリの集合。Editor 拡張が読み取り専用で可視化する対象 |
| ワーカー | Worker | `IAssetLoaderWorker` | 内部 | 実ロードを担当する単位。handle を内部に隠蔽し、dedup や RefCount には関知しない。生成は `IAssetLoaderServiceFactory` 経由（3.5） |
| ロードステータス | Load Status | `AsyncOperationStatus` | 内部 | ロードの進行状態。Addressables の型をそのまま使う |
| 再利用バッファ | Reusable Buffer | `_releaseTargetBuffer` | 内部 | 一括回収時の一時退避用 `List`。`ToArray()` の都度確保を避けるためフィールドに保持して `Clear()` で使い回す（5.4） |
| リトライ | Retry | `_maxRetry`, `_retryDelayMs` | 内部 | 失敗時の再試行。回数・待機時間は SO で設定する |
| 進捗通知 | Progress | `IProgress<float>`（初期化）, `IProgress<DownloadStatus>`（ダウンロード） | 外部 | 進捗の通知手段。UI に直接依存しないため `IProgress<T>` のみを公開する。ダウンロードは、割合だけでなく段階・バイト数・リトライ回数も届ける型（`DownloadStatus`）で通知する |

### 1.4 初期化

詳細は `src/AddressableManager/Docs/specs/initialize-spec.md` を参照。

| 日本語 | 英語 | コード上の命名 | 公開 | 説明 |
|--------|------|---------------|------|------|
| 初期化 | Initialize | `IAddressableHandler.InitializeAsync` | 外部 | `Addressables.InitializeAsync` の呼び出しと状態の管理。何度呼んでも同じ処理を共有する |
| 初期化モード | Initialize Mode | `InitializeMode`（`Auto` / `Manual`） | 内部 | 初期化を Manager の `Awake` で自動で始めるか、`InitializeAsync` の呼び出しで始めるかの設定 |
| 自動初期化 | Auto Initialize | `InitializeMode.Auto` | 外部 | Manager の `Awake` で初期化を始めること。利用者は Handler の `InitializeAsync` で実行中の初期化を待てる |
| 初期化の状態 | Initialize State | `InitializeState`（`NotStarted` / `Running` / `Succeeded` / `Failed`） | 外部 | 初期化の進行状態。Handler の `AccInitializeState` で参照する。`Succeeded` / `Failed` は終端（以降は変化しない） |
| 初期化の状態モデル | Initialize State Model | `InitializeStateModel` | 内部 | 初期化の状態・失敗の例外・共有完了・進捗の通知先を保持する Model。Manager が生成して InitializeController に注入する |
| 初期化失敗 | Initialize Failure | `AddressableInitializeException` | 外部 | 初期化に失敗したことを示す例外。Addressables の例外を `InnerException` に入れる。失敗した後はアプリを再起動するまで初期化をやり直せず、以降の `InitializeAsync` と Load もこの例外になる |
| 初期化前の Load の扱い | Load Before Initialize | `LoadBeforeInitializePolicy`（`PassThrough` / `WaitForInitialize`） | 内部 | 初期化が成功する前の Load を、そのまま通すか、初期化の完了まで待たせるかの設定。既定は `PassThrough`。初期化に失敗した後は、どちらでも Load を止める |
| 初期化ゲート | Initialization Gate | `IInitializationGate` | 内部 | Loading が「読み込んでよいか（待つ必要があるか）／待つ」を、Download が「初期化の成功まで待つ」を問い合わせる共通インターフェース。InitializeController が実装し、Manager が LoadingController・DownloadController に注入する。Controller 同士を参照させないために `Core/` 直下に置く |
| URL キャッシュ対策 | Catalog Cache Busting | `CatalogCacheBustingService` | 内部 | リモートカタログの `.hash` の URL に要求ごとに異なるクエリを付け、OS の URL キャッシュで古いカタログが返るのを防ぐ（カタログ本体の URL は `.hash` の変換結果から作られるため、本体にも同じクエリが付く）。既定は OFF |

### 1.5 ダウンロード

詳細は `src/AddressableManager/Docs/specs/download-spec.md` を参照。

| 日本語 | 英語 | コード上の命名 | 公開 | 説明 |
|--------|------|---------------|------|------|
| 更新の有無の判定 | Has Update | `IAddressableHandler.HasUpdateAsync` | 外部 | 更新データがあるかどうかだけを `bool` で返す。カタログは更新しない |
| 更新の確認 | Check Update | `IAddressableHandler.CheckUpdateAsync` | 外部 | カタログを確認・更新し、更新されたカタログ数・ダウンロードが必要なバンドル数・合計バイト数を返す。カタログを更新するかは `CatalogUpdatePolicy` で決まる |
| ダウンロード | Download | `IAddressableHandler.DownloadAsync` | 外部 | 対象 Label のアセットをダウンロードする。カタログは更新しない（更新は `CheckUpdateAsync`）。進捗は `IProgress<DownloadStatus>` で受け取ることを推奨する |
| 確認結果 | Download Check Result | `DownloadCheckResult` | 外部 | `CheckUpdateAsync` の戻り値。`readonly struct` で、`AccCatalogUpdateCount`・`AccBundleCount`・`AccTotalBytes`・`AccIsCatalogUpdatePending`・`AccHasDownload` を持つ |
| ダウンロード状態 | Download Status | `DownloadStatus`, `IAddressableHandler.AccDownloadStatus` | 外部 | 段階・バイト数・割合・リトライ回数。`readonly struct` で、取得・通知した時点の値のコピー。必要なたびに取得し直す |
| ダウンロード段階 | Download Phase | `DownloadPhase`（`None` / `CheckingCatalog` / `UpdatingCatalog` / `CalculatingSize` / `Downloading` / `Completed` / `Failed` / `Canceled`） | 外部 | 処理の進行状態。`None` は何も処理していない（ダウンロードなし）。`Completed` / `Failed` / `Canceled` は、次の処理が始まるまで残る |
| 最後の通知 | Final Notification | — | 外部 | 処理の終わりに、通知先を外す前に1回だけ届く `Completed` / `Failed` / `Canceled` の `DownloadStatus`。受け取る側は、これで成功・失敗・キャンセルを区別する |
| カタログ更新の方針 | Catalog Update Policy | `CatalogUpdatePolicy`（`None` / `CheckOnly` / `CheckAndUpdate`） | 内部 | `CheckUpdateAsync` がカタログを確認・更新するかを選ぶ設定。既定は `CheckAndUpdate` |
| ダウンロード失敗 | Download Failure | `AddressableDownloadException` | 外部 | リトライを使い切ってもダウンロードできなかったことを示す例外。失敗した段階とリトライ回数を持ち、元の例外を `InnerException` に入れる。呼び出し元の `await` に返り、`DownloadStatus` には持たせない |
| ダウンロード状態モデル | Download Status Model | `DownloadStatusModel` | 内部 | 現在の段階・バイト数・リトライ回数・実行中フラグ・進捗の通知先を持つ Model。Manager が生成して DownloadController に注入する |
| 更新ありフラグ | Content Update Available | `ResourceLocatorInfo.ContentUpdateAvailable`（Addressables 内部） | 内部 | `CheckForCatalogUpdates` が立てる、メモリ上の「更新あり」の印。立てただけではカタログは書き換わらず、`UpdateCatalogs` を引数なしで呼ぶと、この印が立ったカタログが更新される |

---

## 2. システム用語（モジュール構造・配布）

### 2.1 レイヤーと責務

| 日本語 | 英語 | コード上の命名 | 公開 | 説明 |
|--------|------|---------------|------|------|
| マネージャー本体 | Manager | `AddressableManager` | 外部 | GameObject にアタッチする本体。生成・寿命管理と Unity ライフサイクルの中継のみを行い、ロジックを持たない（3.1） |
| 初期化コントローラー | Initialize Controller | `InitializeController` | 内部 | 起動時に1回だけ走る初期化シーケンス。ダウンロード関連の処理は持たない（3.2） |
| ダウンロードコントローラー | Download Controller | `DownloadController` | 内部 | カタログ更新・依存ダウンロードを担う常駐コントローラー。起動時とゲーム内の双方から呼べる。初期化の成功は `IInitializationGate` を介して待つ（3.3） |
| ローディングコントローラー | Loading Controller | `LoadingController` | 内部 | 実行時ロード／解放のロジック本体。dedup・RefCount・寿命回収を担う（3.4） |
| ハンドラー | Handler | `IAddressableHandler` | 外部 | 利用者が触れる唯一の入口。初期化・ダウンロード・読み込み・解放を、状態を持たず Controller へ転送する薄い窓口。`AddressableManager.AccInstance.AccHandler` から取得する（3.7） |
| サービス | Service | `AddressableLoaderBase` / `AssetLoaderService<T>` | 内部 | 実ロード担当。string キーだけを受け取り、リトライ・ロギング・キャンセル・handle 解放を担当する（3.5） |
| 内部ロガー | Logger | `AddressableLogger` | 内部 | `Debug.Log` を包むだけの内部用ロガー。ログレベルは `AddressableRuntimeSettingsSO` で設定する（3.1.1） |
| Label ロケーション解決サービス | Label Location Service | `ILabelLocationService`, `LabelLocationService` | 内部 | Label のロケーションを型を絞らずに取得し、`UnityEngine.Object` ではない型と重複を除いて返す Service。Controller が1件ずつの Load を組み立てる前段（3.4.2） |
| Watcher バインダー | Watcher Binder | `IAssetLifetimeWatcherBinder` | 内部 | Watcher の貼り付けと解除を担う Lifetime 層の窓口。Controller が `AddComponent` を直接呼ばないために挟む（3.8） |
| 責務の一方向性 | One-way Dependency | — | 内部 | Controller だけが Registry と Service の両方を知り、他の層は隣の層としか話さないという原則（2章） |
| ピュア C# クラス | Pure C# Class | — | 内部 | `MonoBehaviour` を継承しない Unity 非依存のクラス。Controller・Service・Model はすべてこれに保つ |
| 外部依存型 | Public-facing Type | — | 外部 | 利用者プロジェクトのコードが直接触れる型。namespace に `Core` を含めない |
| 内部実装型 | Internal Type | — | 内部 | Handler 等を経由して間接的にのみ使われる型。namespace は `MAH.AddressableManager.Core.<機能>` |
| 機能単位の縦割り | Feature Slice | `Initialize` / `Download` / `Loading` | 内部 | `Runtime/Core/` 配下の分割軸。機能フォルダ同士の相互参照は設計違反（4章） |
| 汎用モジュール | General-purpose Module | — | 外部 | 特定ライブラリ（DI コンテナ・R3）に依存しない方針。UniTask は正として使うが必須の依存にはしない。本モジュールの前提（1章） |
| 低レベル経路 | Low-level Access | `LoadingController` への直接経路 | 外部 | 一括プリロード・メモリ計測・障害調査のために残す「逃げ道」（3.8） |
| ライフサイクル中継 | Lifecycle Relay | `LateUpdate` → `OnLateUpdate` → `Tick`、`sceneUnloaded` → `OnSceneUnloaded` → `HandleSceneUnloaded` | 内部 | Manager が Unity のフレーム更新とシーンのアンロードをイベント経由で Controller に渡す仕組み（3.1） |

### 2.2 非同期実装方針

| 日本語 | 英語 | コード上の命名 | 公開 | 説明 |
|--------|------|---------------|------|------|
| UniTask | UniTask | `UniTask`, `UniTask<T>` | 外部 | **正とする非同期型**。Unity のライフサイクルに組み込まれ、GC アロケーションが発生しにくい。必須の依存にはせず、`#if` の外で `using` は書かない（3.5.1） |
| Awaitable | Awaitable | `Awaitable`, `Awaitable<T>` | 外部 | UniTask が導入されていない場合の代替の非同期型（Unity 6.0 以上の標準機能）。UniTask と同じく1回しか `await` できない。`Task` は使わない（3.5.1） |
| 共有完了 | Shared Completion | `SharedCompletion`（暫定）、`AssetEntryModel.AccLoadTask`、`InitializeStateModel` の共有完了 | 内部 | 複数の呼び出し元が待つ処理の完了を表すもの。`await` できる値を共有せず、待ち手ごとに値を作って結果を配る（3.5.1） |
| シグネチャの出し分け | Conditional Signature | `#if ADDRESSABLE_UNITASK_SUPPORT` | 内部 | UniTask 版（`#if` 側）と代替の Awaitable 版（`#else` 側）の API を切り替える方式。呼び出し側は `await` で使う限り違いを意識しない |
| コンパイルシンボル（UniTask） | Compile Symbol | `ADDRESSABLE_UNITASK_SUPPORT` | 内部 | UniTask 2.5.0 以上が UPM で導入されている場合に自動定義される |
| Version Defines | Version Defines | — | 内部 | asmdef の機能で、パッケージの存在に応じてシンボルを自動定義する。**UPM 経由のパッケージにのみ反応する**（3.5.1・罠） |
| キャンセル付き待機 | With Cancellation | `AsyncCompat.WithCancellation` | 内部 | UniTask / Awaitable の差を吸収する薄いヘルパー。キャンセル待機の実装はここに集約する |
| 実時間での待機 | Realtime Delay | `AsyncCompat.Delay` | 内部 | リトライ等の時間待ち。ポーズ中（`timeScale = 0`）も進むよう実時間で待つ。`UniTask.Delay` を直接呼ばない（3.5.1） |
| ブリッジ | Bridge | `handle.ToUniTask()`（Awaitable 版は実装時に確定） | 内部 | `AsyncOperationHandle` を `await` できる形に変換する処理。Awaitable 版は `AwaitableCompletionSource` で完了イベントを待ち受ける |
| キャンセルトークン | Cancellation Token | `CancellationToken ct` | 外部 | 実行時にしか決まらないため SO ではなく引数で渡す |
| メインスレッド前提 | Main Thread Only | — | 内部 | Addressables の継続がメインスレッド前提であるため、ロックを取らない設計にしている（3.4） |

### 2.3 配布・ビルド構成

| 日本語 | 英語 | コード上の命名 | 公開 | 説明 |
|--------|------|---------------|------|------|
| UPM | Unity Package Manager | — | 外部 | 本モジュールの配布手段。git URL の `?path=MAH-AddressableManager` 指定で、リポジトリルートの配布フォルダを配布する。`#v1.0.0` のようにタグを付けるとバージョンを固定できる |
| パッケージ名 | Package Name | `com.github.mah-lib.addressable-manager` | 外部 | `package.json` の `name`。`manifest.json` の `dependencies` に書くキー |
| 配布フォルダ | Package Folder | `MAH-AddressableManager/`（リポジトリルート） | 内部 | UPM で配布するフォルダ。開発は `src/AddressableManager/Assets/@MAH/MAH-AddressableManager/` で行い、リリース時にここへ複製する（`CONTRIBUTING.md` 4章） |
| Unity パッケージ構成 | Package Layout | `Runtime/` `Editor/` `Tests/` `Samples/` | 内部 | パッケージ最上位の構成。asmdef の分割単位でもある（4章）。配布フォルダでは `Samples/` を `Samples~/` にする（Unity にインポートされず、利用者が Package Manager の Samples から選んで Import する） |
| asmdef | Assembly Definition | `*.asmdef` | 内部 | アセンブリ定義ファイル。`Editor/` は役割別に4区分（`Window/` `Attribute/` `SettingsSync/` `Registration/`）するが asmdef は1つに集約する |

### 2.4 設定アセット（SO）

プロジェクト固定・環境で決まる設定は SO で管理し、実行時にしか決まらないもの（`CancellationToken` / `IProgress<float>`）は引数で渡す（3.2）。設定 SO は機能ごとに分けず、実行時用とエディタ用の2つにまとめる（設計仕様書3.1.1）。`CreateAssetMenu` パスは `src/AddressableManager/Docs/conventions.md` 8章が正。

| 日本語 | 英語 | コード上の命名 | 公開 | 説明 |
|--------|------|---------------|------|------|
| 実行時設定 | Runtime Settings | `AddressableRuntimeSettingsSO` | 内部 | Manager に割り当てる唯一の設定アセット。ログレベル等の共通設定と、機能ごとの設定クラスを束ねる。ビルドに含まれる |
| エディタ設定 | Editor Settings | `AddressableEditorSettingsSO` | 内部 | エディタでのみ使う設定（デバッグウィンドウの更新間隔・リークのログ出力・Dependency Check の出力先・アセットの登録 等）。ランタイムから参照しないためビルドに含まれない |
| 機能ごとの設定 | Feature Settings | `Addressable<機能>Settings`（`AddressableLoadingSettings` 等） | 内部 | Runtime SO が保持する `[Serializable]` クラス（SO ではない）。Controller には自分の機能の設定だけを渡す |
| ローディング設定 | Loading Settings | `AddressableLoadingSettings` | 内部 | owner ポーリングの有効/無効と間隔（既定3秒）、リトライ回数・待機時間 |
| 初期化設定 | Initialize Settings | `AddressableInitializeSettings` | 内部 | 初期化モード、初期化前の Load の扱い、`autoReleaseHandle`、URL キャッシュ対策の ON/OFF |
| ダウンロード設定 | Download Settings | `AddressableDownloadSettings` | 内部 | カタログ更新の方針、対象 Label（引数で上書き可）、リトライ回数・待機時間、古いバンドルのキャッシュを消すか |
| デバッグウィンドウ設定 | Debug Window Settings | `AddressableDebugWindowSettings` | 内部 | Editor SO が保持する `[Serializable]` クラス（SO ではない）。デバッグウィンドウの更新間隔（既定 0.5 秒・下限 0.1 秒）を持つ。ビルドに含まれない（3.9） |
| 登録設定 | Registration Settings | `AddressableRegistrationSettings` | 内部 | Editor SO が保持する `[Serializable]` クラス。自動登録のスイッチ・ログの細かさ・ルールの作成時の初期値・既定の Group Template を持つ（3.9(7)） |

---

## 3. 技術用語

### 3.1 アーキテクチャ

レイヤー名の定義。命名パターン（`<機能>Controller` 等）は `src/AddressableManager/Docs/conventions.md` 10章が正。

| 日本語 | 英語 | コード上の命名 | 説明 |
|--------|------|---------------|------|
| コントローラー | Controller | `<機能>Controller` | ロジックの本体。Registry と Service の両方を知る唯一の層 |
| サービス | Service | `<対象>Service`（基底は `Base`） | 実処理の担当。本モジュールでは実ロードを担う |
| モデル | Model | `<対象>Model` | データと状態を表現するクラス |
| ビュー | View | `<対象>View` | `MonoBehaviour` を継承する表示担当 |
| データ | Data | `Addressable<機能>Settings` | SO による設定クラス |
| ディフィニション | Definition | `AssetKey`, `LifetimeKind` 等 | enum やデータの箱。ロジックを持たない |
| ロジック | Logic | `AsyncCompat` | 状態を持たない純粋ロジック |
| ハンドラー | Handler | `<対象>Handler` | 状態を持たず Controller への転送に徹する薄い窓口。本モジュール固有のレイヤー |
| デバッグリーダー | Debug Reader | `<対象>DebugReader`（インターフェースは `I<対象>DebugReader`） | Editor が実行中の状態を読み取り専用で見るための窓口。本モジュール固有のレイヤー。操作できる Handler と区別するため `Handler` と付けない |
| 属性 | Attribute | `<対象>Attribute` | `PropertyAttribute` 派生。ランタイム側に置く |
| ドロワー | Drawer | `<対象>Drawer` | `PropertyDrawer` 派生。Editor 側に置く |

### 3.2 Unity 固有

| 日本語 | 英語 | コード上の命名 | 説明 |
|--------|------|---------------|------|
| MonoBehaviour | MonoBehaviour | `MonoBehaviour` | 継承するのは Manager / Editor / View のみ（3.1） |
| ゲームオブジェクト | GameObject | `GameObject` | owner 指定の対象 |
| コンポーネント | Component | `Component` | owner 指定時は内部で `gameObject` に解決される |
| プレハブ | Prefab | — | 再利用可能な GameObject のアセット。`LoadAsync<GameObject>` が返すのはこれ |
| シーン | Scene | `Scene` | Scene 寿命の単位。`Unload` が回収トリガーになる |
| ScriptableObject | ScriptableObject | `SO`（本書での略記） | 設定アセット。プロジェクト固定の設定はこちらで管理する |
| インスペクター | Inspector | — | SO 設定とキー選択の操作面。実装者はここだけを触れば済む状態を目指す |
| LateUpdate | LateUpdate | `LateUpdate` | ライフサイクル中継に使う。`Update` だと同一フレーム内で後から実行された `Destroy` を取りこぼす（3.1） |
| OnDestroy | OnDestroy | `OnDestroy` | Unity が呼び出しを保証するため、破棄検知の主経路として使える |
| PlayerLoop | PlayerLoop | `PlayerLoopSystem` | フレーム処理の挿入機構。本モジュールでは採用しない（5.3） |
| ドメインリロード | Domain Reload | — | エディタのスクリプト再読み込み。static 状態の残留問題の原因（3.7） |
| EditMode / PlayMode | EditMode / PlayMode | — | テストの実行環境の区分。`Tests/Editor` / `Tests/Runtime` に対応する |
| IMGUI | Immediate Mode GUI | `OnGUI` | 外部依存ゼロの描画手段。Editor 拡張のウィンドウの描画に使う |
| AssetDatabase | AssetDatabase | `AssetDatabase.GetDependencies` | エディタ専用のアセット参照 API。未登録の依存の確認（Dependency Check）に使う |
| 弱参照 | Weak Reference | — | Editor 拡張が実行中インスタンスへ到達するための手段のひとつ（3.9） |

### 3.3 Addressables

本モジュールの設計説明に必要な語のみを収録する。グループ・ラベル等の**運用規則**は利用者側プロジェクトの規則に従うため対象外（`src/AddressableManager/Docs/conventions.md` 9章）。Label 一括ロードで扱う語（Label・リソースロケーション）のみ収録する。

| 日本語 | 英語 | コード上の命名 | 説明 |
|--------|------|---------------|------|
| Addressables | Addressables | `Addressables` | 本モジュールが管理対象とするアセット管理システム |
| アドレス | Address | `AddressablePath` | アセットを指す文字列。Path 入口の値 |
| アセットリファレンス | Asset Reference | `AssetReference` | アセットを GUID で指す参照型。Reference 入口の値 |
| ランタイムキー | Runtime Key | `AssetReference.RuntimeKey` | `AssetReference` が持つ GUID 文字列。アドレス文字列とは値が一致しない。`AssetKey.FromReference` の時点で文字列に変換し、Service へは string で渡す（3.5・3.6） |
| ハンドル | Handle | `AsyncOperationHandle` | Addressables の操作ハンドル。Service が内部に隠蔽し、利用者には見せない |
| 操作ステータス | Operation Status | `AsyncOperationStatus` | ハンドルの完了状態 |
| 解放 API | Release API | `Addressables.Release(handle)` | 本モジュールが使う唯一の解放 API |
| インスタンス生成 API | Instantiate API | `Addressables.InstantiateAsync`, `ReleaseInstance` | **本モジュールでは使用せず、相当 API も提供しない**（3.4.1） |
| カタログ | Catalog | `CheckForCatalogUpdates`, `UpdateCatalogs` | 配信アセットの所在情報。更新確認と適用を DownloadController が担う |
| InternalId の変換関数 | Internal Id Transform | `Addressables.InternalIdTransformFunc` | ロケーションの InternalId（URL 等）を読み込み前に書き換える、Addressables 全体で1つの delegate。URL キャッシュ対策で使い、既存の関数は上書きせず連結する |
| Addressable 設定 | Addressable Settings | `AddressableAssetSettings` | アドレス一覧の取得元。エディタ専用アセンブリからしか参照できない（3.9(1)） |
| Play Mode Script | Play Mode Script | — | エディタ再生時のアセット供給方式（Use Asset Database / Use Existing Build）。測定値の意味が変わるため EditMode 実ロードを採用しない理由（3.9(6)） |
| ロケーション解決 | Location Resolution | — | アドレスから実体を突合する処理。完全な同一判定はコストが高く採用しない（3.6・5.3） |
| Label | Label | `Addressables.LoadResourceLocationsAsync(label)` | Addressables のエントリに付ける識別子。Label 一括ロードでは、この Label が付いたアセットをまとめて読み込む（3.4.2） |
| リソースロケーション | Resource Location | `IResourceLocation`（`PrimaryKey` / `ResourceType`） | アセット1件分の所在情報。Label から取得でき、画像は Texture2D と Sprite の2件として返る。取得順序は保証されない（loading-spec 13.12） |

### 3.4 パフォーマンス

| 日本語 | 英語 | コード上の命名 | 説明 |
|--------|------|---------------|------|
| GC アロケーション | GC Allocation | — | ヒープ確保。フレーム時間より GC スパイクが体感品質に影響するため優先して抑える（5.4） |
| ボックス化 | Boxing | — | 値型の参照型化。`Dictionary` を `IEnumerable` にアップキャストすると発生する |
| 一時コレクションの都度確保 | Temporary Allocation | `ToArray()` / `ToList()` | 一括回収で連続呼び出しされるため、再利用バッファに置き換える |
| クロージャ生成 | Closure Allocation | — | ラムダの毎回生成。Watcher へのコールバックはフィールドにキャッシュした delegate を使う |
| スタックトレース記録 | Stack Trace Capture | — | 取得コストと文字列生成が大きいため `#if UNITY_EDITOR` で本番から除外する |
| 早期リターン | Early Return | `Tick` 冒頭の判定 | owner 登録数が0件なら何も走らせない方針（5.4） |

---

## 4. Editor 拡張の用語

### 4.1 デバッグウィンドウ

| 日本語 | 英語 | コード上の命名 | 公開 | 説明 |
|--------|------|---------------|------|------|
| デバッグウィンドウ | Debug Window | `AddressableDebugWindow` | 外部 | 実行時状態・リーク・Watcher をタブで表示する単一ウィンドウ（3.9） |
| 読み取り専用原則 | Read-only Principle | — | 外部 | Editor 拡張から Registry や寿命情報を書き換えないという原則。手動 Release／強制回収ボタンは実装しない。対象は実行時の状態を見る機能と静的な走査の機能で、アセットの登録（4.3）だけは、ルールか手動一括登録の指示に従って `AddressableAssetSettings` に書き込む（2026-10-24） |
| 読み取り専用の窓口 | Debug Reader | `IAddressableDebugReader`, `AddressableDebugReader` | 内部 | Window が Model を読むための窓口。Manager が Model をコンストラクタ注入して生成し、`AddressableManager.AccInstance.AccDebugReader` で渡す。`#if UNITY_EDITOR` で囲み、本番ビルドから除外する（3.9） |
| スナップショット | Snapshot | `<対象>Snapshot` | 内部 | Model の状態を取得した時点でコピーした値（`readonly struct`）。取得後に Model が変わっても値は変わらない。Window 側の `List` へコピーして渡す |
| 更新間隔（pull） | Refresh Interval | `AddressableDebugWindowSettings` | 内部 | Window が窓口から状態を取り直す間隔。Runtime 側からは通知せず、Window 側が一定間隔で取りに行く |
| 実行時状態確認 | Runtime State View | — | 外部 | Registry の内容（キー・型・RefCount・確保内訳・ステータス）の一覧表示 |
| リーク検出一覧 | Leak List | `LeakListTab`, `LeakRegistryModel`, `LeakSnapshot` | 外部 | 明示解放されずに回収されたチケットを、確保元ごとに集約して蓄積・表示する（「リーク」タブ）。ログは流れて見逃されるため一覧化する。呼び出し元へのジャンプと、ログ出力もできる |
| 表示のクリア | Clear View | `LeakListTab` | 外部 | 「リーク」タブの「表示をクリア」。Runtime の記録は消さず、タブがクリア時点の基準を持って、以降の発生分だけを表示する（既読扱い） |
| ログ出力 | Leak Log Export | `LeakLogFormatter`, `LeakLogWriter`, `LeakLogAutoExporter`, `AddressableLeakLogSettings` | 外部 | 「リーク」タブの内容をテキストファイルに出力すること。手動（日時付きで溜める）と、Play 終了時の自動（固定名 `AddressableLeaks_LastPlay.txt` に上書きし、前回分を `-prev` に1世代残す）がある |
| 呼び出し元（スタックトレース） | Caller Frame | `StackTraceParser.FindFirstUserFrame` | 外部 | スタックトレースのうち、モジュール内（`Runtime/`・`Library/PackageCache/`）を飛ばした、ジャンプできる最初の利用者のフレーム。ジャンプボタンとログに使う |
| Watcher 可視化 | Watcher View | `OwnerWatcherTab`, `OwnerReclaimRegistryModel`, `OwnerWatchSnapshot`, `OwnerReclaimSnapshot` | 外部 | 監視中の owner と Watcher の貼り付け状態の一覧と、`OnDestroy` 経由／ポーリング経由のどちらで回収されたかの区別表示（「Owner」タブ。3.9(5)） |
| アドレスプルダウン | Address Dropdown | `AddressablePathAttribute`, `AddressablePathDrawer` | 外部 | アドレスを Inspector のプルダウンから選択させる属性拡張。件数が多い場合は `AdvancedDropdown` を用いる |

### 4.2 未登録の依存の確認（Dependency Check）

| 日本語 | 英語 | コード上の命名 | 公開 | 説明 |
|--------|------|---------------|------|------|
| 未登録の依存の確認 | Dependency Check | `AddressableDependencyWindow` | 外部 | Addressables に登録したアセットが使っている未登録のアセットが、どこでどのくらい使われているかを、Play もビルドもせずに一覧で確認するウィンドウ。Group の作成・設定を見直す材料にする。読み取り専用（3.9(4)） |
| 未登録の依存 | Unregistered Dependency | `DependencyCheckRow` | 外部 | 登録済みのアセット（親）が使っているが、Addressables に登録されていないアセット（子）。使っている親のバンドルに暗黙的に同梱される |
| 暗黙の依存 | Implicit Dependency | — | 外部 | 未登録のアセットが、使っている親のバンドルに暗黙的に同梱されること（Addressables の挙動） |
| 複数グループ | Multi-Group | `DependencyCheckRow.AccIsMultiGroup` | 外部 | 未登録のアセットを、2つ以上のグループの親が使っていること。登録済みのアセットを経由した使用も数える。Group の分け方を見直す手がかりで、**ビルドで重複するかどうかの判定ではない**（正確な確認は Analyze） |
| 参照元 | Referrer | `DependencyCheckReferrer` | 外部 | 未登録のアセットを使っている登録済みのアセット（親）と、そのグループ。直接の使用か、経由の使用かを持つ |
| 経由 | Via | `DependencyCheckReferrer.AccIsVia`・`AccViaPath` | 外部 | 親が未登録のアセットを直接使うのではなく、間にある登録済みのアセット（経由先）を通して使っていること。例：Title.prefab → Btn.mat（登録済み）→ Tex.png のとき、Tex.png の参照元 Title.prefab は「Btn.mat を経由」。経由先が複数のときは、パス順で最初の1つと数を持つ |
| 走査（Scan） | Scan | `DependencyCheckScanner` | 内部 | 全グループの登録済みアセットの依存を `AssetDatabase.GetDependencies` で辿ること。「Scan」ボタンでだけ行う |
| 読み込み計測（不採用） | Load Profiling | — | — | ロード所要時間等の計測。読み込み時間は端末・通信環境に大きく左右されるため作らないことにした（2026-10-15。設計仕様書 5.3・6章）。計測用の型・デファインシンボルは存在しない |

### 4.3 アセットの登録

詳細は `src/AddressableManager/Docs/specs/asset-registration-spec.md` を参照。

| 日本語 | 英語 | コード上の命名 | 公開 | 説明 |
|--------|------|---------------|------|------|
| アセットの登録 | Asset Registration | `MAH.AddressableManager.Editor.Registration` | 外部 | アセットを Addressables のエントリにすること（グループ・アドレス・Label を決める）。自動登録と手動一括登録の2つのきっかけがある（3.9(7)） |
| 自動登録 | Auto Registration | `AutoRegistrationPostprocessor` | 外部 | アセットのインポートのたびに、ルールに従って登録・更新すること。既定で有効 |
| 自動登録のスイッチ | Auto Registration Switch | `AddressableRegistrationSettings` | 外部 | 自動登録だけを完全にオフにする Editor SO の設定。他の登録ツールを使うプロジェクトのためのもの。オフのときはルールをすべて無視する。手動一括登録には影響しない |
| ルール（自動登録のルール） | Auto Registration Rule | `AddressableAutoRegistrationRuleSO` | 外部 | 対象フォルダ1つ・対象の拡張子・アドレス／グループ名／Label のテンプレートを書いた SO。利用者が何個でも作る。自動では作らない |
| All | All | — | 外部 | ルールの対象の指定方法のひとつ。対象フォルダの配下で、Addressables に登録できるものをすべて対象にする。除外する拡張子を指定できる |
| 競合（ルールの競合） | Rule Conflict | `RegistrationRuleMatcher` | 外部 | 1つのアセットに、2つ以上のルールが当たること（パスの範囲と拡張子の両方が重なる）。競合したアセットは登録せず、エラーにする。優先順位では決めない |
| 当て直す | Reapply | `AddressableRegistrationRuleWindow` | 外部 | ルールを作ったり変えたりしたあと、すでにあるアセットにルールを適用すること。差分を見てから適用する。ルールの変更では自動で当て直さない |
| ルールの一覧のウィンドウ | Rule List Window | `AddressableRegistrationRuleWindow` | 外部 | すべてのルール・競合・アドレスの重複の一覧と、当て直すを持つウィンドウ。ルールの編集はしない |
| 手動一括登録 | Manual Registration | `AddressableManualRegistrationWindow` | 外部 | Project ウィンドウでフォルダを右クリックし、その場で決めたグループ・Label・アドレスで一括で登録すること。ルールは使わず、自動登録のルールに当たるアセットは変更しない |
| ルールの作成時の初期値 | Rule Creation Defaults | `AddressableRegistrationSettings` | 外部 | ルールを作るときにコピーする Label・アドレスのテンプレート・除外パターン。コピーしたあとはルールと無関係で、評価のときには参照しない（「既定値」とは呼ばない） |
| テンプレート | Template | — | 外部 | アドレス・グループ名・Label を、マクロを使って組み立てる文字列。例：`{Folder.Top}/{Asset.Name}` |
| マクロ | Macro | `RegistrationMacroExpander` | 外部 | テンプレートの中の `{Asset.Name}` などの置き換え部分。独自の一覧と、よく使う Unity の値（`{UnityEngine.Application.version}` 等）だけを展開し、Addressables の `EvaluateString` には任せない。値は登録したときに固定される |
| アドレスの追従／固定 | Address Follow / Fixed | — | 外部 | アセットの移動・名前の変更のときに、アドレスを展開し直す（追従。既定）か、変えない（固定）かのルールの設定 |
| アドレスの重複 | Duplicate Address | `RegistrationPlanBuilder` | 外部 | 別々のアセットに同じアドレスが付くこと。登録はしてエラーにする（手動一括登録では確認のダイアログで知らせる） |
| 登録の計画 | Registration Plan | `RegistrationPlan` | 内部 | 今のエントリとルールから作る差分（登録・グループの移動・アドレスの変更・Label の変更）。差分がなければ書き込まない |

---

## 5. 英語・日本語対応表

### 5.1 頻出単語（動詞の使い分け）

| 英語 | 日本語 | 備考 |
|------|--------|------|
| Load | 読み込む | アセットの読み込み |
| Release | 解放する | **アセット参照を手放す**。Destroy / Dispose と混用しない |
| Reclaim / Collect | 回収する | 寿命終了時の**自動**解放にのみ使う |
| Destroy | 破棄する | GameObject・インスタンスの破棄（Unity 側） |
| Dispose | 破棄する | Controller・Worker の後始末（C# 側） |
| Instantiate | 生成する | Prefab からの実体生成。**Load と明確に区別する** |
| Initialize | 初期化する | |
| Download | ダウンロードする | 「DL」は文中の略記のみ可。API 名・クラス名には使わない |
| Resolve | 解決する | `AssetLifetime` → `LifetimeScopeId` の変換など |
| Register / Unregister | 登録する／解除する | ①Watcher への登録（必ず対で書く）。②アセットの Addressables への登録（エントリにすること。4.3）。②の「解除」は本モジュールでは行わない（ルールに当たらなくなっても登録を外さない）。文脈で紛れるときは「Watcher に登録する」「Addressables に登録する」と対象を書く |
| Reapply | 当て直す | 自動登録のルールを、すでにあるアセットに適用すること（4.3）。「再適用」と混在させない |
| Poll | 走査する | ポーリングによる破棄検知 |
| Tick | 駆動する | 経過時間を渡してフレーム処理を進める |
| Watch | 監視する | Watcher による破棄検知 |
| Retry | リトライする | 「再試行」と混在させない |
| Record | 記録する | リーク情報・Owner の回収経路の蓄積 |
| Create | 生成する | オブジェクトの生成（Instantiate とは区別） |
| Update | 更新する | カタログ更新など |
| Validate | 検証する | 設定値の妥当性確認 |
| Execute | 実行する | シーケンスの実行 |
| Complete | 完了する | |

### 5.2 略語

| 略語 | 正式名称 | 説明 |
|------|---------|------|
| SO | ScriptableObject | 設定アセット。本書・設計仕様書での略記 |
| UPM | Unity Package Manager | 本モジュールの配布手段 |
| DL | Download | 文中の略記のみ可 |
| GC | Garbage Collection | |
| DI | Dependency Injection | 本モジュールは DI コンテナに依存しない |
| IMGUI | Immediate Mode GUI | `OnGUI` による描画方式 |
| SBP | Scriptable Build Pipeline | Addressables の依存パッケージ |
| URP | Universal Render Pipeline | 検証用サンドボックスのレンダリング |
| UI / UX | User Interface / Experience | |

---

## 6. コード上の命名規則

命名の**パターン**（クラス・フィールド・アクセサ・定数・関数の書式、レイヤー別の接尾辞、namespace 構成）は `src/AddressableManager/Docs/conventions.md` 1章・7章・10章が正とする。本書では重複して定義せず、**語彙**（どの語を使うか／使わないか）のみを 5章・7章で定める。

---

## 7. 禁止・非推奨用語

| 使用しない | 代わりに使う | 理由 |
|-----------|-------------|------|
| ~~「Load したインスタンス」~~ | 「Prefab アセット」 | `LoadAsync<GameObject>` が返すのはアセット本体。最も事故に直結する誤用（3.4.1） |
| ~~「Instantiate で読み込む」~~ | 「Load する」 | Load と Instantiate は別系統であり、後者は Manager の管理対象外 |
| ~~「破棄する」を解放の意味で使う~~ | 「解放する」 | 解放／破棄／回収は本モジュールでは別概念（5.1参照） |
| ~~「回収する」を明示解放の意味で使う~~ | 「解放する」 | 「回収」は自動回収に限定する。混用すると「自動回収はセーフティネット」という方針が伝わらない |
| ~~「ハンドル」を利用者向け文書で使う~~ | 「チケット」 | 利用者に handle を意識させないことが本モジュールの目的（1章） |
| ~~`handle` を汎用の変数名にする~~ | `ticket` / 具体名 | 曖昧な変数名としての `handle` は禁止。ただし **Addressables の API 名としての `AsyncOperationHandle`・`handle` は正式名称であり使用可** |
| ~~「Label で解放する」（`ReleaseByLabel`）~~ | 「まとめチケットで解放する」 | 同じ Label を複数の呼び出し元が読んだ場合に、どの分を解放するか決められないため。Label 文字列による解放は提供しない（5.3） |
| ~~`Scope` 単体~~ | `LifetimeScope` / `LifetimeScopeId` | VContainer の `LifetimeScope` と紛らわしいため、修飾なしの `Scope` は使わない |
| ~~`Phase` を寿命種別として使う~~ | `Manual` | 寿命種別は `Manual` / `Scene` / `Owner` の3種。`Phase` は `Manual` に統合済み（3.8） |
| ~~`_user_data`~~（フィールド） | `_userData` | フィールドにアンダースコア区切りは使わない（ローカル変数は `src/AddressableManager/Docs/conventions.md` 1章に従いスネークケース） |
| ~~「重複」「重複同梱」を Dependency Check の画面・出力・コードで使う~~ | 「複数グループ」（`AccIsMultiGroup`） | Dependency Check の「グループ数」は、使っている親のグループの数で、ビルドで実際に重複するかどうかの判定ではない。「重複」と書くと、ビルドで重複すると誤解される（4.2）。なお、Pack Separately の注記など、ビルド時の参考として実際の重複を述べる文脈では使ってよい |
| ~~「ルールの既定値」「Editor SO の既定値」（自動登録の文脈）~~ | 「ルールの作成時の初期値」 | ルールを作るときにコピーするだけの値で、評価のときに参照する既定値ではない。「既定値」と書くと、Editor SO を変えればすべてのルールに反映されると誤解される（4.3） |
| ~~`btn`~~ | `button` | 略語は避ける |
| ~~`temp`~~ | 具体的な名前 | 曖昧さを避ける |
| ~~`data`~~ / ~~`info`~~ | 具体的な名前 | 曖昧さを避ける |
