# AddressableManager 設計仕様書

> 最終更新日：2026-10-24
> 対象 Unity バージョン：6000.3.26f1
> 本書は**開発者向け**の設計資料（AddressableManager 自体を実装・改修する人が読む）。利用者向けの導入手順・使い方は、リポジトリルートの `README.md` と `Docs/usage.md` を参照する。

## 1. 目的・背景

Addressables の読み込み(Load)〜解放(Release)を一括管理する `AddressableManager` の設計。以下を狙いとする。

- 呼び出し側（実装担当者）が Addressables API（handle, 参照カウント等）を意識せずに利用できること
- `LoadAsync<T>` は読み込んだアセットそのもの（`AssetTicket<T>.AccAsset`）を解放用チケットと一緒に返し、型による挙動差を持たせないこと
- 読み込み・解放は**明示的に**行える（暗黙のDispose任せにはしない）
- Label を指定して**まとめて読み込み・まとめて解放**でき、Label にどの型のアセットが入っているかを利用者が把握していなくても使えること（3.4.2）
- ただし**解放忘れは必ず回収される**こと（Load時に指定した寿命で自動回収するセーフティネットを持つ）
- 多重ロード・解放漏れなどの事故が構造的に起きにくいこと
- 神クラス（グローバル Singleton が全 handle を抱え込む）化を避けること
- SO(ScriptableObject) 設定のみで実装者が即座に扱えるレベルまで抽象化すること
- **汎用モジュール**として、DIコンテナ（VContainer等）・R3のような特定ライブラリに依存しない設計とすること（DIコンテナ導入時もManager内部実装は変更しない）。非同期処理は UniTask を正とするが、必須の依存にはせず、UniTask が無いプロジェクトでは `Awaitable` で動作させる（3.5.1）

---

## 2. 全体構成

```
AddressableManager（機能内クラスの生成・寿命管理と、Handler を返すことのみ）
├─ InitializeController   … 起動時の初期化シーケンス（1回・使い捨て）
├─ DownloadController      … カタログ更新・依存ダウンロード（常駐、起動時／ゲーム内DL双方に対応）
├─ LoadingController       … 実行時ロード/解放のロジック本体（常駐）
├─ Handler(IAddressableHandler) … 利用者が触れる唯一の入口。初期化・ダウンロード・読み込み・解放を各Controllerへ転送する（状態を持たない）
├─ Service                 … 実ロード担当（AddressableLoaderBase を継承した AssetLoaderService）／Label のロケーション解決（LabelLocationService）
├─ Model                   … AssetKey / AssetTicket<T> / AssetGroupTicket / AssetEntryKey / AssetEntryModel / AssetRegistryModel / InitializeStateModel
├─ Lifetime                … 寿命指定と自動回収（AssetLifetime / AssetLifetimeWatcher）
└─ Editor拡張(EditorWindow) … Registry を読み取り専用で可視化／Addressables の未登録の依存を静的に走査／ルールまたは手動の指示によるアセットの登録（3.9(7)）
```

### 責務の一方向性（重要な原則）

- Controller だけが Registry（Model）と Service の両方を知る
- 他の層（Handler, Lifetime, Editor）は隣の層としか話さない
- InitializeController / DownloadController / LoadingController は互いを知らない（Manager が順序を保証する）
- **MonoBehaviour を継承するのは Manager / Editor / View のみ**。Controller・Service・Model はピュアC#とし、Unityライフサイクル（`LateUpdate`）が必要な処理は Manager からイベント経由で中継する

---

## 3. 各層の詳細

### 3.1 AddressableManager

- 責務：**機能内クラス（Controller・Service・Model・Handler）の生成と寿命管理、Unityライフサイクルの中継、および `IAddressableHandler` を返すこと**。ロジックを持たない
- **公開メンバーは `AccInstance`（Manager の取得）と `AccHandler`（`IAddressableHandler` の取得）の2つだけ**。初期化・ダウンロード・読み込み・解放の操作は、利用者が触れる入口を1つに絞るため、すべて Handler（3.7）に置く。Manager に操作用の公開 API（`InitializeAsync` 等）を足さない
- **MonoBehaviour を継承する**。本モジュール内で MonoBehaviour を継承するのは Manager / Editor / View のみとし、Controller・Service・Model はすべてピュアC#クラスとする
- static な `AccInstance` を持ち、`DontDestroyOnLoad` にする。各 Controller と Handler は `Awake` で生成し、`AccHandler` をすぐに公開する（読み込み・解放は `InitializeAsync` に依存しない）。自動初期化（`InitializeMode.Auto`）の開始は、生成の直後に起動するライフサイクルの一部として `Awake` に残す
- `LateUpdate` と `SceneManager.sceneUnloaded` の通知はイベント経由で Controller 側に中継する

詳細（クラス設計・`Awake`/`OnDestroy` の実装・購読の対応関係）は **`src/AddressableManager/Docs/specs/loading-spec.md` 2章**を参照。

### 3.1.1 設定アセットの構成（Runtime SO / Editor SO）

プロジェクト固定の設定は、機能ごとに SO を分けず、**使われる場面（実行時／エディタ）で2つの SO にまとめる**。

| SO | 置き場所 | 中身 | ビルド |
|---|---|---|---|
| `AddressableRuntimeSettingsSO` | `Runtime/Core/` | 実行時に使う設定。共通（ログレベル）＋機能ごとの設定クラス（`AddressableLoadingSettings` 等） | 含まれる（Manager が参照するため） |
| `AddressableEditorSettingsSO` | `Editor/SettingsSync/` | エディタでのみ使う設定（デバッグウィンドウの更新間隔・リークのログ出力・Dependency Check の出力先・アセットの登録〔自動登録のスイッチ・ルールの作成時の初期値・手動一括登録の既定の追加先グループ〕 等） | 含まれない（ランタイムから参照しないため） |

- **機能ごとの設定は SO ではなく `[Serializable]` クラス**（`Addressable<機能>Settings`）として各機能の `Data/` フォルダに置き、Runtime SO がそれを保持する。Inspector では機能ごとに折りたたんで表示される
- **Controller には自分の機能の設定クラスだけを渡す**（`LoadingController` には `AddressableLoadingSettings` のみ）。Runtime SO を丸ごと渡さないことで、機能同士が互いの設定を知らずに済む。機能の設定を束ねるのは合成ルートである Manager 層（`Runtime/Core/`）だけとする
- 設定クラスが SO ではないため、EditMode テストでは `new` するだけで Controller を駆動できる（`ScriptableObject.CreateInstance` が不要）
- 利用者が作成・割り当てるアセットは Runtime SO の1つだけになる（1章「SO設定のみで即座に扱える」）
- Editor SO は、エディタ側の設定項目が初めて必要になった時点で作成する（空のクラスを先に作らない）。最初の項目はデバッグウィンドウの更新間隔（`AddressableDebugWindowSettings`）で、機能1（実行時状態）の実装時に作る。SO は型（`AssetDatabase.FindAssets`）で探し、0件ならエディター起動時に `Assets/MAH.Data/AddressableManager/Editor/` へ自動作成する（詳細：`src/AddressableManager/Docs/specs/editor-debug-window-spec.md` 3章）

採用理由：機能ごとに SO を分けると、利用者が作って割り当てるアセットが機能の数だけ増えるうえ、ログレベルのような機能をまたぐ設定の置き場所が定まらない。「ビルドに含めるか」という境界と SO の境界を一致させれば、エディタ専用の設定が誤ってビルドに入る心配もない。

**自動登録のルールの SO（`AddressableAutoRegistrationRuleSO`。3.9(7)）は、この方針の対象外**とする。プロジェクトに1つずつの「設定」ではなく、利用者が対象フォルダごとに何個でも作る「データ」であるため。自動登録に共通の項目（スイッチ・ルールの作成時の初期値）と、手動一括登録の既定の追加先グループだけを、Editor SO の `AddressableRegistrationSettings` に持たせる（詳細：`src/AddressableManager/Docs/specs/asset-registration-spec.md` 3・8章）。

### 3.2 InitializeController

起動時に1回だけ走る、順序を持つシーケンス。終わったら基本用済み。**ダウンロード関連の処理は持たない**（3.3 DownloadController に分離）。

処理内容：
- `Addressables.InitializeAsync()` の呼び出しと、状態（未開始・実行中・成功・失敗）の遷移。状態は `InitializeStateModel`（3.6）が持つ
- 初期化を**自動**（Manager の `Awake` で開始）か**手動**（利用者が `InitializeAsync` を呼んだ時点で開始）かを Runtime SO で選べる
- `InitializeAsync` は何度呼んでも同じ処理を共有する。完了・失敗は `await` とイベント（`OnInitialized` / `OnInitializeFailed`）の両方で通知する。これらの公開 API は Manager ではなく `IAddressableHandler`（3.7）に置く
- 任意で、リモートカタログの URL キャッシュ対策（`InternalIdTransformFunc` でカタログの `.hash` に要求ごとに異なるクエリを付ける）を行う
- 初期化前の Load は既定で待たない。SO の設定で待たせることもでき、その場合は `Core/` 直下の共通インターフェース `IInitializationGate` を介して LoadingController が問い合わせる（Controller 同士は参照しない）
- **初期化の失敗は致命的なエラーとして扱う**。Addressables は失敗した初期化をアプリの再起動までやり直せないため、リトライ・やり直しは持たず `Failed` を終端にする。失敗した後は、設定に関係なく Load も `IInitializationGate` で止め、`AddressableInitializeException` を返す

設定の持ち方：
- **プロジェクト固定・環境で決まる設定**（モード、初期化前の Load の扱い、`autoReleaseHandle`、URL キャッシュ対策の ON/OFF）→ Runtime SO が保持する `AddressableInitializeSettings` で管理（3.1.1）
- **実行時にしか決まらないもの**（`CancellationToken`, `IProgress<float>`）→ 引数で渡す

詳細（状態遷移・設定項目・公開 API・内部構成・改修ガイド）は **`src/AddressableManager/Docs/specs/initialize-spec.md`** を参照。

### 3.3 DownloadController（独立レイヤー）

カタログ更新・依存ダウンロードを担当する、**起動時とゲーム内の両方から呼び出せる常駐コントローラー**。

背景：当初は InitializeController に内包する想定だったが、「起動時のカタログ更新DL」と「ゲーム内でのイベント配信等に伴う追加DL」は同じ仕組み（進捗管理・リトライ・失敗時リカバリ）を再利用したいため、独立したコントローラーとして切り出す。

責務：
- カタログ更新チェック・更新（`CheckForCatalogUpdates` → `UpdateCatalogs`）
- 更新データの有無の判定（カタログは更新しない）
- ダウンロードが必要なバンドルの数と合計バイト数の計算、依存ダウンロードの実行（`DownloadDependenciesAsync`）と、段階・バイト数・割合を含む進捗通知
- 失敗時のリトライ（段階ごと）
- 起動時もゲーム内も、`IAddressableHandler`（3.7）経由で任意のタイミングから呼び出される。**Manager が起動時に自動で呼ぶ「起動シーケンス」は持たない**（容量の確認ダイアログを挟めなくなるため。必要になったら SO の設定として追加する）

**ダウンロードの操作は、別の Handler を作らず `IAddressableHandler` にメソッドとして追加する**（利用者が触れる入口を1つに絞るため。Manager に `AccDownloadHandler` は置かない）。確定した API は次のとおり（非同期型 UniTask/Awaitable の切り替え方針は3.5.1参照。詳細は `src/AddressableManager/Docs/specs/download-spec.md` 4章）。

```csharp
// IAddressableHandler に追加するメンバー（UniTask 導入時は UniTask 版、無ければ Awaitable 版。ここでは UniTask 版を示す）。対象は Label 単位
DownloadStatus AccDownloadStatus { get; }   // 取得した時点の状態のコピー。必要なたびに取得し直す
UniTask<bool> HasUpdateAsync(CancellationToken ct = default);   // 更新データの有無だけを返す。カタログは更新しない
UniTask<DownloadCheckResult> CheckUpdateAsync(CancellationToken ct = default);  // カタログの確認・更新、件数（カタログ数・バンドル数）と容量
UniTask DownloadAsync(IProgress<DownloadStatus> progress = null, CancellationToken ct = default);
// 上の3つには、Label を引数で渡して SO の設定を上書きする版（IReadOnlyList<string> labels を先頭に取る）もある
```

**進捗は `IProgress<DownloadStatus>` で受け取ることを推奨する**。段階・バイト数・割合・リトライ回数を1つの型で受け取れ、`InitializeAsync` と同じ「引数で渡す」形のため、イベントのような購読解除が要らない。`AccDownloadStatus` で取得する方法は、`IProgress` を渡していない場所から状態を参照したいときの補助である（`src/AddressableManager/Docs/specs/download-spec.md` 4.1）。

初期化の前にダウンロードが呼ばれたときは、**初期化の成功を待つ**（`IInitializationGate.WaitUntilInitializedAsync`。`LoadBeforeInitializePolicy` に関係なく待つ。手動モードで未開始なら `InvalidOperationException`、失敗していれば `AddressableInitializeException`）。従来の案「成功後に初めて `AccDownloadHandler` を公開し、`null` で未初期化を判定する」は、Handler が1つになるため採らない。

設定の持ち方：
- カタログ更新ポリシー、対象 Label、リトライ回数、待機時間等は Runtime SO が保持する `AddressableDownloadSettings` で管理する（3.1.1）

```csharp
[Serializable]
internal sealed class AddressableDownloadSettings
{
    [SerializeField] private CatalogUpdatePolicy _catalogPolicy = CatalogUpdatePolicy.CheckAndUpdate;
    [AddressableLabel, SerializeField] private string[] _labels;     // 引数なしの API が対象にする Label。引数で上書きできる
    [SerializeField] private int _maxRetry = 3;                      // 0〜10
    [SerializeField] private int _retryDelayMs = 1000;               // 0〜60000
    [SerializeField] private bool _autoCleanBundleCache = false;     // カタログ更新時に古いバンドルのキャッシュを消すか
}

public enum CatalogUpdatePolicy
{
    None,            // カタログを確認も更新もしない（今のカタログで件数・容量だけを数える）
    CheckOnly,       // 更新有無を調べるだけ（UI表示用。更新はしない）
    CheckAndUpdate,  // 調べて自動更新
}
```

注意：
- enum は「互いに排他な意味のある選択肢」にのみ使う。連続値（リトライ回数等）は enum 化しない
- 進捗通知は段階（`DownloadPhase`：CheckingCatalog / UpdatingCatalog / CalculatingSize / Downloading / Completed / Failed / Canceled）付きの `DownloadStatus` にし、UI には直接依存しない（`IProgress<DownloadStatus>` で通知するのみ）。失敗の例外は `DownloadStatus` に持たせず、呼び出し元の `await` に `AddressableDownloadException` として返す
- 「ブラックボックス化」＝失敗フェーズや進捗などの**情報**まで隠すことではない。隠すのはあくまで**呼び出しの手間**
- LoadingController とは独立しており、互いを知らない（ダウンロード未完了のアセットをLoadした場合の扱いはLoadingController／Service側のエラーハンドリングに委ねる）。InitializeController とも互いを知らず、初期化の成功を待つときは `IInitializationGate` を通す

詳細（動作・段階・設定項目・公開 API・内部構成・改修ガイド・注意点）は **`src/AddressableManager/Docs/specs/download-spec.md`** を参照。

### 3.4 LoadingController

実行時ロードの中核。何度でも並行に呼ばれ続け、アプリ生存中ずっと状態を保持する。

責務：`(AssetKey, 型)` による重複ロード判定（dedup）、RefCount と寿命スコープごとの確保内訳（CountByScope）の管理、発行済みチケットの管理（通し番号による二重解放の防止）、in-flight 中の重複呼び出しを LoadTask の共有で吸収、Label 一括ロードの組み立てとまとめチケットの管理（3.4.2）、寿命スコープ終了時の一括回収（`ReleaseLifetime`）、Service の起動・破棄。

`LoadAsync<T>` の要点：
- RefCount は **await の前に**加算する（キャンセル・失敗時は catch で巻き戻す）
- 重複ロードの判定キーは `(AssetKey, 型)`（`AssetEntryKey`）。同じキーでも型が違えば別のエントリとして扱う（単体の `LoadAsync` で型を取り違えても例外にならない点に注意。3.4.2・5.3・6章）
- ロード失敗時・ロード中に RefCount が 0 になった場合は、エントリを即座に削除する（Worker の破棄はロード完了後）
- 明示解放と自動回収は同じ経路（`ReleaseInternal`）を通す。解放は、チケットの記録（`IssuedTicketRecord`）が持つエントリへの参照を起点にし、Registry にあるエントリが記録のものと同じ場合だけ Registry から取り除く

詳細（クラス設計・`LoadAsync<T>` の7手順・サンプルコード）は **`src/AddressableManager/Docs/specs/loading-spec.md` 3章**を参照。

### 3.4.1 戻り値のルール（Instantiate系を提供しない）

**`LoadAsync<T>` は常に「読み込んだアセットそのもの」を返す**。型によって戻り値の意味が変わることはない。`Addressables.InstantiateAsync` に相当する API は Manager では提供せず、インスタンス化・破棄は実装者の責務とする。

採用理由：戻り値のルールを一貫させ、Manager の責務を「アセットのロードと解放」だけに絞るため。Load 系（実体を共有）と Instantiate 系（呼ぶたびに別物）という性質の異なる2系統を抱え込まずに済む。

詳細（実装者が注意すべき点）は **`src/AddressableManager/Docs/specs/loading-spec.md` 4章**を参照。

### 3.4.2 Label 一括ロード

`LoadByLabelAsync(label, lifetime, ct)` で、Label が付いたアセットをまとめて読み込み、`AssetGroupTicket`（まとめチケット）を返す。`Release(group)` を1回呼べば、中身がすべて解放される。**Load のときに型は指定せず、Label にどの型が入っているかを利用者が把握していなくてよい**。型は、まとめチケットから `TryGetAsset<T>` / `GetAssets<T>` で取り出すときに指定する（読み込んだ型との完全一致）。

- 内部は「ロケーションの解決（`LabelLocationService`）→ (PrimaryKey, 型) の組ごとに1件ずつの Load」の組み合わせ。RefCount・重複ロードの共有・寿命の管理は、1件ずつの Load と同じ処理を通る（別の解放経路は作らない）
- そのため、重複ロードの判定キーは `(AssetKey, 型)` とする。画像は Texture2D と Sprite の2件として読まれるが、Label で読んだ Sprite と単体の `LoadAsync<Sprite>` は同じエントリを共有する
- 「確保（同期）」と「待機」の2段階に分ける。確保は `await` の前に全件ぶん行うため、全件の通し番号が先に決まる。失敗・キャンセル・寿命の終了が起きたら、残りを待たずに全件をすぐ巻き戻す

採用理由：中〜大規模のプロジェクトでは、Label ごとに入っている型を把握しきれない。Load 時に型を指定させる方式では運用できないため、型は取り出す側に寄せた。

詳細（手順・例外の優先順位・寿命による回収・制限事項・サンプルコード）は **`src/AddressableManager/Docs/specs/loading-spec.md` 13章**を参照。

### 3.5 Service（実ロード担当）

別プロジェクトで作成済みの `AddressableLoaderBase<T>` を移植・改修してベースクラスとし、それを継承した `AssetLoaderService<T>` が実ロードを担う。リトライ、ロギング、キャンセル処理、handle解放を担当。dedupやRefCountは関知しない。

- **Service は string キーだけを受け取る**。`AssetReference` は `AssetKey.FromReference` の時点で文字列に変換されるため、Service は入口（Path / Reference）の違いを知らない
- Worker の生成は `IAssetLoaderServiceFactory` 経由にする（EditMode テストで偽の Worker に差し替えるため）
- 複数アセットを扱う `AddressableMultiLoaderBase<T>` は移植しない（Controller の重複判定・RefCount と役割が重なるため）。Label 一括ロードは、Controller が1件ずつの Load を組み合わせて行い、Service 層にはロケーションの解決だけを担う `LabelLocationService` を置く（3.4.2）

詳細（改修内容の一覧・インターフェース定義）は **`src/AddressableManager/Docs/specs/loading-spec.md` 6章**を参照。

### 3.5.1 非同期実装方針（UniTask を正とし、無ければ Awaitable）

> **移行前の注意**：本節は 2026-10-31 に決めた方針である。コードの移行が済むまで、実装には `Task` が残っている（5.4）。

非同期処理は **UniTask を正**とする。Unity のライフサイクル（PlayerLoop）に組み込まれ、GC アロケーションが発生しにくいため。本モジュールの利用者は UniTask を導入している前提であり、UniTask 版が本来の動作である。

ただし汎用モジュールとして UniTask を**必須の依存にはしない**。UniTask が導入されていないプロジェクトでも動くよう、その場合に限り **`UnityEngine.Awaitable`**（Unity 6.0 以上）で動作させる。**`System.Threading.Tasks.Task` は使わない**。DIコンテナ（VContainer等）・R3 には依存しない。DIコンテナを導入するプロジェクトであっても、Manager内部の実装（Controller以下）は変更しない。

- **正（UniTask 導入時）**：コンパイルシンボル `ADDRESSABLE_UNITASK_SUPPORT` が定義され、`Cysharp.Threading.Tasks.UniTask` / `UniTask<T>` を使用する
- **UniTask が無い場合の代替**：`UnityEngine.Awaitable` / `Awaitable<T>` を使用する
- 切り替えは `#if` によるAPIシグネチャの出し分けで行う。`#if ADDRESSABLE_UNITASK_SUPPORT` 側に UniTask 版を、`#else` 側に Awaitable 版を書く。呼び出し側は `await` で使う限り、どちらの型が使われているか意識する必要はない

#### Task を使わない理由

- UniTask と Awaitable は、**どちらも1つのインスタンスを1回しか `await` できない**。同じ規則にそろえることで、切り替え後の挙動の差を小さくする
- WebGL はスレッドプールが動かないため、スレッドに依存する `Task` の機能（`Task.Delay`、`TaskScheduler.Default` を使う `ContinueWith` など）を持ち込まない。WebGL は設計上の対象に含めるが、**WebGL ビルドでの動作は検証していない**
- UniTask が無い環境では、呼び出しごとの経路（公開 API・Controller・Service・Worker）の GC アロケーションを減らせる。ただし共有完了（下記）は自身も確保するため、GC 削減は共有部分を置き換える理由にはならない

**失うもの**：`Task` の「何度でも安全に `await` できる」性質と、未観測の例外の検出（`TaskScheduler.UnobservedTaskException`）。自作の `SharedCompletion` を保守する負担も増える。

#### 複数の呼び出し元が待つ処理は、await できる値を共有しない

UniTask と Awaitable は1回しか `await` できず、2回目は例外になるか、プールで再利用された別の処理の状態を読む恐れがある。そのため、次の処理は `await` できる値ではなく、**共有完了（`SharedCompletion`）** を共有する。

| 共有完了 | 誰が待つか |
|---|---|
| ロード1件分（`AssetEntryModel.AccLoadTask`） | 重複ロードの呼び出し元すべて、Label 一括ロード、Worker の後始末。Entry の存続中は、完了した後にも後から来た Load が待つ |
| 初期化1回分（`InitializeStateModel` が持つ共有完了、`IAddressablesInitializeService`、`IInitializationGate`） | `InitializeAsync` の呼び出し元すべて、初期化を待つ Load、Download |

共有完了の考え方は次のとおり。

- 状態（待機中・成功・失敗・キャンセル）と例外を持つ
- 待ち手ごとに専用の CompletionSource（`UniTaskCompletionSource` / `AwaitableCompletionSource`）を作って登録し、完了時に全待ち手へ結果を配る
- 完了済みなら、待たずに結果を返す
- 待ち手のキャンセルは、その待ち手の登録を外すだけで、他の待ち手と実処理には影響しない
- 複数の完了の待ち合わせ（最初の失敗で打ち切る一括待機）は、完了通知のコールバックで作る

公開 API（`LoadAsync` / `LoadByLabelAsync` / `InitializeAsync`）は、共有完了を待ったうえで UniTask / Awaitable を返す。

**実装時に決めること**：`SharedCompletion` の名前（暫定）・配置・API、待ち手の CompletionSource の再利用の要否、`AsyncCompat` の Awaitable 版の実装、`AccLoadTask` などの識別子の改名、EditMode テストでの Awaitable の扱い（完了にプレイヤーループを使うため）。

#### Awaitable の制約

- メインスレッド専用。完了通知もメインスレッドから行う
- `await` されなかった例外は握りつぶされる。fire-and-forget にする箇所は、例外を観測する経路（ログ出力）を必ず用意する

#### 時間待ちは実時間で待つ

`UniTask.Delay` は既定で `Time.timeScale` の影響を受け、ポーズ中（`timeScale = 0`）は待機が終わらない。ポーズ中に失敗したロードのリトライが止まらないよう、時間待ちは `AsyncCompat.Delay` に集約し、UniTask 版では `ignoreTimeScale: true` を指定する。Awaitable 版も実時間で待つよう実装する（`Awaitable.WaitForSecondsAsync` は `timeScale` の影響を受けるため使わない）。ポーリングがポーズ中も進む `unscaledDeltaTime` を使うのと同じ考え方である（3.8）。

#### シンボルの自動設定

手動でスクリプティング定義シンボルを設定させると設定漏れが起きるため、**asmdefの Version Defines 機能**を使い、UniTaskパッケージが存在する場合に自動で `ADDRESSABLE_UNITASK_SUPPORT` を定義する。

| 項目 | 値 |
|---|---|
| Resource | `com.cysharp.unitask` |
| Define | `ADDRESSABLE_UNITASK_SUPPORT` |
| Expression | `2.5.0` （2.5.0 以上。Unity の表記では上限なし。Unity が自動整形する形に合わせた） |

下限を 2.5.0 とする根拠：2.5.0 で `cancelImmediately` フラグや `Awaitable.AsUniTask()` が追加されており、API の区切りとして妥当。執筆時点の最新は 2.5.11。上限は設けず、以降のバージョンすべてを対象とする。

**制約に注意**：Version Defines は **UPM（Package Manager）経由でインストールされたパッケージにのみ反応する**。UniTask を `.unitypackage` で導入して `Assets/` 配下に展開した場合、パッケージとして認識されないためシンボルが定義されず、UniTask が存在するのに代替の Awaitable 版が使われる。UniTask 版が正であるため、UPM（git URL指定を含む）で導入する運用に統一する。

キャンセル付き待機のヘルパー（`AsyncCompat`）と `AsyncOperationHandle` からのブリッジの実装は **`src/AddressableManager/Docs/specs/loading-spec.md` 6.2章**を参照。

本方針は Manager 全層（Controller / Service / Handler）に適用する。移植元の `AddressableLoaderBase<T>`（UniTask 直接依存）も改修対象に含め、上記の `#if` 切り替えに合わせる（3.5参照）。

検証用サンドボックス（`src/AddressableManager/`）には UniTask を導入しているため、テストは UniTask 版で実行される。代替の Awaitable 版を確かめる場合は、UniTask を外した状態でテストを実行する。

### 3.6 Model

- **AssetKey**：Path入口とAssetReference入口を両立させるための複合キー。「入口の種別＋値」が一致した場合のみ同一とみなす（`AssetKey` 自体は型を含めない）
- **`AssetEntryKey`**：Registry の判定キー。`(AssetKey, 型)` の組で、同じ `AssetKey` でも型が違えば別のエントリになる（3.4.2）
- **`AssetTicket<T>`**：Load1回ごとに発行される解放用チケット。読み込んだアセットを持ち、通し番号で一意になる
- **`AssetGroupTicket`**：Label 一括ロード1回ごとに発行される、まとめチケット。中身のアセットを型ごとに取り出せ、`Release` 1回でまとめて解放される（3.4.2）
- **AssetEntryModel / AssetRegistryModel**：1件分の状態（Worker, ロードの共有完了, RefCount, CountByScope, AssetType）と、その集合
- **InitializeStateModel**：初期化の状態・失敗の例外・共有完了・進捗の通知先。Manager が生成して InitializeController に注入する。`Succeeded` / `Failed` を終端とする遷移を自身で守る（詳細：`src/AddressableManager/Docs/specs/initialize-spec.md` 7.2）

詳細（フィールド定義・サンプルコード）は **`src/AddressableManager/Docs/specs/loading-spec.md` 7章**を参照。

### 3.7 Handler（IAddressableHandler）

- 責務は**呼び出し関数のみ**。状態を持たず、Controllerへの転送に徹する。**利用者が触れる操作の入口はこの Handler 1つ**にする（初期化・ダウンロード・読み込み・解放）
- Manager経由で取得する（`AddressableManager.AccInstance.AccHandler`）。素のstaticハンドラーは採用しない（LoadingControllerが状態を持つため、状態はインスタンスに閉じてリセット可能にしておく必要がある）
- 公開する型は具象クラスでなくインターフェース（`IAddressableHandler`）にする。名前は、対象が「アセット」から「Addressables の操作全体」に広がったため、`IAssetHandler` から改めた
- 1件ずつの `LoadAsync<T>` / `Release<T>` に加えて、Label 一括ロードの `LoadByLabelAsync` / `Release(AssetGroupTicket)` を窓口として提供する（3.4.2）
- 初期化の `InitializeAsync` / `AccInitializeState` / `AccIsInitialized` / `OnInitialized` / `OnInitializeFailed` も提供する（3.2）。**イベントは Handler にフィールドを持たせず、`add` / `remove` アクセサで `InitializeController` のイベントへ直接つなぐ**（購読の状態を Handler に作らないため）
- ダウンロードの `HasUpdateAsync` / `CheckUpdateAsync` / `DownloadAsync` / `AccDownloadStatus` も提供する（3.3）。Label を引数なしで呼ぶ版は、Label に `null` を渡し、`DownloadController` が Runtime SO の設定の Label に解決する（Handler は設定を持たない）
- 実装クラス `AddressableHandler` は、複数の機能の Controller を知るため、どの機能フォルダにも置かず `Runtime/Core/Handler/`（namespace `MAH.AddressableManager.Core`）に置く。機能フォルダに置くと、ほかの機能の namespace を `using` することになり、機能フォルダ同士の相互参照の禁止に反する
- Handler は各 Controller へ転送するだけで、実行順序を決めるロジックは持たない（順序の保証は Manager の役目。2章）

詳細（インターフェース定義・利用例）は **`src/AddressableManager/Docs/specs/loading-spec.md` 8章**、初期化の API は **`src/AddressableManager/Docs/specs/initialize-spec.md` 4章**、ダウンロードの API は **`src/AddressableManager/Docs/specs/download-spec.md` 4章**を参照。

### 3.8 Lifetime（寿命指定と自動回収）

- 読み込み・解放は**明示的に行う**のが主経路（`LoadAsync` → `Release(ticket)`）。Load時に**寿命を指定**し、その寿命の終了時点で解放されていないものは**自動的に回収される**
- 寿命の種類は `Manual / Scene / Owner` の3種（`Phase` は `Manual` に統合）
- Owner の破棄検知は内部専用の監視 Component `AssetLifetimeWatcher` で行う。インターフェースは外部公開せず、実装者は何も継承・実装しない
- 多重の安全網：Watcher の `OnDestroy`（主経路）→ 低頻度ポーリング（既定3秒間隔。取りこぼし対策）→ `Manager.Dispose` 時の全Entry強制解放（最終防衛線）
- リーク警告（エディタ限定）：寿命回収時に「明示 `Release` されずに回収された ticket」を、確保時のスタックトレース付きで記録する

詳細（寿命解決時の例外・Watcher/Binder のクラス設計・ポーリングの実装・PlayerLoopを採用しない理由）は **`src/AddressableManager/Docs/specs/loading-spec.md` 5章・9〜10章**を参照。


### 3.9 Editor拡張

#### 共通方針

- **読み取り専用**を原則とする。EditorWindow から Registry や寿命情報を書き換えることはしない（手動Release／強制回収ボタンは原則を崩すため実装しない）
- 読み取り専用の対象は、**実行時の状態を見る機能（(2)(3)(5)）と、アセットを静的に走査する機能（(4)）**に限る。例外は「アセットの登録」（(7)）だけで、**利用者が SO に書いたルール、または手動一括登録の画面での指示に従う場合に限り** `AddressableAssetSettings` に書き込む。実行時の状態（Registry・寿命情報）には、(7) も触れない（2026-10-24。6章）
- 実行中インスタンスへの到達手段は、**Manager が `Awake` で Model を窓口 `AddressableDebugReader` にコンストラクタ注入し、`#if UNITY_EDITOR` の internal アクセサ `AccDebugReader` で `IAddressableDebugReader`（読み取り専用）として渡す形**とする。Window は既存の `AddressableManager.AccInstance.AccDebugReader` だけを参照し、新しい static 状態は作らない。更新は Window 側の一定間隔 pull とする
- Editor 側の設定は `AddressableEditorSettingsSO`（3.1.1）から Inspector で操作する
- 共通基盤の詳細（窓口・スナップショット・ウィンドウの枠・設定・改修ガイド）は `src/AddressableManager/Docs/specs/editor-debug-window-spec.md` を正とする
- 実行時状態・リーク検出・owner Watcher は**同じデータソース（Registry と寿命スコープ情報）を参照する**ため、ウィンドウを分けず **1ウィンドウのタブ切り替え**として実装する
- Play せずにアセットを静的に走査する機能（未登録の依存の確認）は、データの出所（`AssetDatabase`・`AddressableAssetSettings`）が違うため、**別のウィンドウ**（`AddressableDependencyWindow`）にする。窓口（`IAddressableDebugReader`）・`AddressableManager` には触れず（Runtime のログ出力 `AddressableLogger` だけを使う）、Editor アセンブリだけで完結する
- 記録処理・表示処理はすべて `#if UNITY_EDITOR` で囲み、本番ビルドから完全に除外する（5.4参照）

#### 実装項目と優先度

| 項目 | 優先度 | 目的 |
|---|---|---|
| AddressablePath プルダウン属性 | 高 | 「SOで選ぶだけ」の方針の前提 |
| 実行時の読み込み/解放状態の確認 | 高 | 障害調査の基盤 |
| リーク検出一覧 | 高 | ログだと見逃すため。自動回収に頼りきらせない方針の実効性を担保 |
| 未登録の依存の確認（Dependency Check） | 中 | 未登録のアセットが、どこでどのくらい使われているかを見て、Group の作成・設定を見直す（粒度・更新頻度の混在・登録漏れ）。ビルドでの重複の参考情報も出す（読み込み計測から差し替え。2026-10-15） |
| owner Watcher の可視化 | 中 | 破棄検知が期待通り動作しているかの検証 |
| アセットの登録：自動登録 | 高 | ルールの SO に従い、インポートのたびにアセットを登録・更新して、登録・グループ分け・アドレスの調整の手間をなくす（2026-10-24） |
| アセットの登録：手動一括登録 | 中 | 選んだフォルダの配下を一覧に出し、拡張子とチェックで絞り込んで、決めたグループ・Label で一括で登録する（2026-10-24） |

#### (1) AddressablePath プルダウン属性

Inspector 上で Addressable のアドレスをプルダウンから選択できるようにする属性拡張。文字列の直接入力によるタイポを防ぐ。

- **属性クラス（`PropertyAttribute`）はランタイム側に置き、`PropertyDrawer` のみ `Editor/` に置く**。アドレス一覧は `AddressableAssetSettings` から取得するが、これはエディタ専用アセンブリからしか参照できないため、この分離が必須
- アドレス数が数百件規模になるとプルダウンが実用に耐えないため、**検索ボックス付きの `AdvancedDropdown` を用いる**か、Addressable グループでの絞り込みを設ける
- Label も同じ仕組みで選べるよう、`AddressableLabel` 属性を別クラスとして持つ（Label 一括ロードの Label 指定のタイポ防止。詳細：`src/AddressableManager/Docs/specs/addressable-label-attribute-spec.md`）

#### (2) 実行時の読み込み/解放状態の確認

Registry の内容を一覧表示する。表示項目：

- `AssetKey`（種別＋値）
- アセットの型
- アセットの名称（ロード完了後。2026-10-14 追加）
- `RefCount`
- **寿命スコープごとの確保内訳（`CountByScope`）** — 「誰が解放していないのか」の特定に直結するため重要
- ロードステータス（`AsyncOperationStatus`）
- 確保元のスタックトレース

寿命の種類（All / Manual / Scene / Owner）で絞り込める。実装済み（`RuntimeStateTab`）。画面・Snapshot・「ロード中／ロード完了」の意味などの詳細は、`src/AddressableManager/Docs/specs/editor-debug-window-spec.md` 8.1 を正とする。

#### (3) リーク検出一覧

3.8 のリーク警告をログ出力のみに留めず、**溜めて一覧表示する**。ログは流れて見逃されるため、方針（自動回収に依存した実装を是正させる）を機能させるにはこちらが必要。

- 寿命回収時に「明示 `Release` されずに回収された ticket」を記録し、確保時のスタックトレース付きで蓄積表示する
- 該当箇所へジャンプできるようにするとレビュー・是正が容易になる

実装済み（`LeakListTab`。2026-10-14）：

- 確保元（`AssetKey`・型・寿命の種類・スタックトレース、Label 一括は Label・寿命の種類・スタックトレース）ごとに**集約**して溜め、回数・最終発生時刻・最終発生のスコープ名（発生時に記録）を表示する。記録は Editor 限定（`#if UNITY_EDITOR`）の `LeakRegistryModel` で、読み取りは窓口 `CopyLeaksTo` を通す
- スタックトレースの各フレームと、最初の利用者のフレーム（呼び出し元）へのジャンプ。「実行時状態」タブのチケットにも共通の部品を使う
- 「表示をクリア」はタブ側の既読扱い（Runtime の記録は消さない）。「ログ出力」ボタンで、表示中の内容をテキストファイルに出力できる（「出力 → クリア」で区間ごとに残せる）。Play の終了時に自動で出力する設定もある（固定名の上書き＋前回分1世代）
- 画面・記録・ログ出力・設定の詳細は `src/AddressableManager/Docs/specs/editor-debug-window-spec.md` 8.2 を正とする

#### (4) 未登録の依存の確認（Dependency Check）

Addressables に登録したアセット（親）が使っている、**未登録のアセット（子）**を一覧で確認するウィンドウ。**一番の目的**は、未登録の子が「どこで（どのグループのどの親から）、どのくらい使われているか」を見て、Group の作成・設定を見直す材料にすること（粒度の調整、更新頻度の違うものが同じ Group に混ざっていないかの確認、登録漏れへの気づき）。Play もビルドもせずに使える。

ビルドしたときにどうなるかは**2番目の目的**（参考情報）。未登録の子は、使っている親のバンドルに暗黙的に同梱されるため、複数のグループから使われていると重複して入る可能性がある。ただし画面の「複数グループ」は、使っている親のグループの数であって、ビルドでの重複の判定ではない。正確な確認は Addressables 標準の Analyze（Check Duplicate Bundle Dependencies）で行う。

- 「Scan」ボタンで、全グループの登録済みアセット（フォルダーエントリの配下・Scene を含む）の依存を `AssetDatabase.GetDependencies`（直接の依存を1段ずつ）で辿る静的な走査。実際のロード・ビルド・ビルドレイアウトの計算は行わない
- 未登録の子1つにつき1行で、使っている親とグループを示す。登録済みのアセットを経由した使用は、経由先を付けて直接の使用と区別する。参照元のグループ数が2以上のものを複数グループとして強調する。型・名前・グループでの絞り込み、ファイルサイズ順の並べ替え、クリックで Project ウィンドウに表示、テキストでの出力ができる
- 読み取り専用（グループ・エントリを書き換えない。「登録する」ボタンは置かない）
- Addressables 標準の Analyze は、ビルドしたときの重複バンドルを確認するもので、目的が違う（ビルドレイアウトの計算を伴って重く、絞り込み・アセットへの移動もできない）。この機能は、Group を見直すための日常的な確認用に、軽い静的走査を持つ。ビルド結果の確認は Analyze に任せる
- 画面・判定・出力・注意点の詳細は `src/AddressableManager/Docs/specs/editor-dependency-check-spec.md` を正とする

**読み込み計測は作らない**（2026-10-15）：当初はこの項目で、ロード所要時間・リトライ回数等の計測（ビルド後のオーバーレイ表示と、デファインシンボルによるビルド含有の制御を含む）を予定していた。読み込み時間は端末・通信環境（場所）に大きく左右され、エディタやローカルでの計測値に意味が薄いため、取りやめた（6章）。

#### (5) owner Watcher の可視化

- owner寿命で登録されている GameObject の一覧と、`AssetLifetimeWatcher` が貼られているかの表示
- **`OnDestroy` で回収されたものと、ポーリングで回収されたものを区別して記録する**。ポーリング経由の回収が多い場合は Watcher が取りこぼしていることを示すため、破棄検知の不具合検出に使える（3.8 の多重の安全網が期待通り機能しているかの検証）

実装済み（`OwnerWatcherTab`。2026-10-15）：

- 監視中の Owner を、名前・InstanceID・確保中のチケット数・Watcher の状態（貼り付けと登録の有無・破棄済みで未回収）で一覧する。窓口 `CopyWatchedOwnersTo` を通し、生存・Watcher の判定は取得時に行う
- 回収は、回収経路（`OnDestroy` / ポーリング）と Owner 名ごとに集約して溜める。記録は Editor 限定の `OwnerReclaimRegistryModel` で、Owner の回収が起きる2つの入口（Watcher の通知・ポーリング）で、解放の直前に行う。解放の経路（`ReleaseInternal`）は変えない。読み取りは窓口 `CopyOwnerReclaimsTo` を通す
- 画面・記録・Snapshot・注意点の詳細は `src/AddressableManager/Docs/specs/editor-debug-window-spec.md` 8.3 を正とする

#### (6) EditMode でのサイズ概算（作らない）

**作らないことにした**（2026-10-15）。算出できるのは、元アセットのディスク上のファイルサイズの合計だけで、バンドルのサイズ・通信量・キャッシュ上のサイズ・実機メモリのどれとも一致せず、用途が限られるため（キー同士の相対比較ぐらい）。重い依存の把握は (4) のファイルサイズ列と並べ替えで足り、ビルド後のバンドルサイズは Addressables の Build Layout レポート、ダウンロードサイズは `CheckUpdateAsync` が返す合計バイト数（`DownloadCheckResult`）で確認できる（6章）。

#### (7) アセットの登録（自動登録・手動一括登録）

Addressables 特有の「アセットの登録」「グループ分け」「アドレスの調整」の手間を減らす。裏側のロジック（ルールの当たり判定・マクロの展開・Addressables への書き込み）を共通にして、2つのきっかけを用意する（2026-10-24）。

- **自動登録**：利用者が作る**ルールの SO**（`AddressableAutoRegistrationRuleSO`。対象フォルダ1つ・対象の拡張子〔または All〕・アドレス／グループ名／Label のテンプレート）に従い、アセットのインポート（AssetPostprocessor）のたびに登録・更新する。ルールの SO は何個でも作れ、同じフォルダでも拡張子が違えば別のルールにできる。1つのアセットに2つ以上のルールが当たったら**競合**として登録せず、Console にエラーを出す
- **既定で有効**とし、Editor SO のスイッチで**自動登録だけを完全にオフ**にできる（他の登録ツールを使うプロジェクトのため）。ルールの SO は自動では作らず、作った直後は拡張子が空で何も登録しないため、パッケージを入れただけではグループは変わらない
- 何度実行しても同じ結果になるようにし、差分がなければ書き込まない（各メンバーの環境で走るため、グループのアセットの git の衝突を防ぐ）。ルールの SO の変更では自動で当て直さず、ルールの一覧のウィンドウ（`AddressableRegistrationRuleWindow`）の「当て直す」で、差分を見てから適用する
- **手動一括登録**：Project ウィンドウでフォルダを右クリック（`MAH → Register to Addressables...`）し、専用ウィンドウ（`AddressableManualRegistrationWindow`）で、**対象の配下を読み込んで一覧に出し、拡張子のオン／オフと行のチェックで絞り込んで**、追加先のグループ・Label を決めて一括で登録する（決めなければ Editor SO の設定）。ルールの SO は使わず、自動登録のルールに当たるアセットは変更しない。すでに登録済みのアセットは一覧に出さない
- アドレス・グループ名・Label は、`{Asset.Name}` `{Folder.Top}` `{Path.Relative}` などの独自のマクロと、よく使う Unity の値（`{UnityEngine.Application.version}` 等）だけで組み立てる。Addressables の `EvaluateString` には任せない
- Runtime 側の型・窓口（`IAddressableDebugReader`）・`AddressableManager` には触れず、Editor アセンブリだけで完結する。`AddressableAssetSettings` に書き込むのは、本機能の書き込み担当の1か所に限る
- 画面・挙動・マクロ・設定・内部構成・注意点の詳細は `src/AddressableManager/Docs/specs/asset-registration-spec.md` を正とする

---

## 4. フォルダ構成

> **本章のフォルダ構成・個々のクラス名は暫定案**である。フォルダ分けの考え方を説明するために大雑把に置いたものであり、`conventions.md` の命名パターンに沿ってさえいれば、実装時に都度変更してよい。一方、**構成の骨組み**（Unityパッケージ構成の遵守、機能ごとの縦割り、`Core/` 直下は共通物のみ、という原則）と **namespace 規則**は維持する。

最上位は **Unity のパッケージ構成**（`Runtime/` `Editor/` `Samples/` `Tests/`）に従い、機能ごとの**縦割り**は `Runtime/Core/` の配下で行う。縦割りの判断軸は「一緒に変更されるものが近くにあるか」で、Loading の仕様変更時に Initialize 系のファイルを触ることはないため、機能単位で分けた方が変更の単位と一致する。Model も Controller ごとに従属するため、機能配下に置く。

下のツリーは**開発フォルダ**（`src/AddressableManager/Assets/@MAH/MAH-AddressableManager/`）の構成。UPM の**配布フォルダ**はリポジトリルートの `MAH-AddressableManager/` で、リリース時に開発フォルダから複製し、`package.json` を持ち、`Samples/` を `Samples~/` にする以外は同じ構成になる（6章 2026-10-15・`CONTRIBUTING.md` 4章）。

```
src/AddressableManager/Assets/@MAH/MAH-AddressableManager/
├─ Runtime/
│   ├─ Core/                          … モジュール実装の本体
│   │   ├─ AddressableManager.cs      … 生成・寿命管理・LateUpdate/sceneUnloaded中継
│   │   ├─ AssetKey.cs
│   │   ├─ AssetTicket.cs             … AssetTicket<T>
│   │   ├─ AssetLoadException.cs      … 読み込み失敗の例外（外部依存型）
│   │   ├─ AddressableRuntimeSettingsSO.cs … 実行時設定の SO。機能ごとの設定クラスを束ねる（3.1.1）
│   │   ├─ Logger/                    … AddressableLogger.cs, AddressableLogLevel.cs（デバッグ用途のため専用フォルダに分離）
│   │   ├─ AddressableInitializeException.cs … 初期化失敗の例外（外部依存型）
│   │   ├─ AddressableDownloadException.cs   … ダウンロード失敗の例外（外部依存型）
│   │   ├─ Interfaces/                … IAddressableHandler（利用者の唯一の入口）, IInitializationGate（Initialize と、Loading・Download の橋渡し）
│   │   ├─ Handler/                   … AddressableHandler.cs（各 Controller への転送のみ。複数機能を知るため機能フォルダの外に置く）
│   │   ├─ DebugReader/               … IAddressableDebugReader.cs, AddressableDebugReader.cs（Editor の読み取り専用の窓口。#if UNITY_EDITOR。複数機能の Model を知るため機能フォルダの外に置く。3.9）
│   │   │
│   │   ├─ Initialize/
│   │   │   ├─ Controller/  InitializeController.cs
│   │   │   ├─ Model/       InitializeStateModel.cs
│   │   │   ├─ Service/     IAddressablesInitializeService.cs, AddressablesInitializeService.cs,
│   │   │   │               CatalogCacheBustingService.cs
│   │   │   └─ Data/        AddressableInitializeSettings.cs（[Serializable]。Runtime SO が保持）, InitializeState.cs,
│   │   │                   InitializeMode.cs, LoadBeforeInitializePolicy.cs
│   │   │
│   │   ├─ Download/
│   │   │   ├─ Controller/  DownloadController.cs
│   │   │   ├─ Model/       DownloadStatusModel.cs
│   │   │   ├─ Service/     IAddressablesDownloadService.cs, AddressablesDownloadService.cs, DownloadSizeInfo.cs
│   │   │   └─ Data/        AddressableDownloadSettings.cs（[Serializable]。Runtime SO が保持）, CatalogUpdatePolicy.cs,
│   │   │                   DownloadPhase.cs, DownloadStatus.cs, DownloadCheckResult.cs（後の3つは外部依存型）
│   │   │
│   │   └─ Loading/
│   │       ├─ Controller/  LoadingController.cs
│   │       ├─ Model/       AssetEntryModel.cs, AssetRegistryModel.cs
│   │       ├─ Service/     AddressableLoaderBase.cs, AssetLoaderService.cs,
│   │       │               IAssetLoaderWorker.cs, IAssetLoaderServiceFactory.cs
│   │       ├─ Lifetime/    AssetLifetime.cs, LifetimeScopeId.cs, AssetLifetimeWatcher.cs,
│   │       │               IAssetLifetimeWatcherBinder.cs
│   │       └─ Data/        AddressableLoadingSettings.cs（[Serializable]。Runtime SO が保持）, キー定義SO
│   │
│   └─ Attribute/                     … AddressablePathAttribute.cs, AddressableLabelAttribute.cs（属性はランタイム側に置く）
│
├─ Editor/                            … 役割別に4区分。asmdefは分割せず1つに集約
│   ├─ Window/
│   │   ├─ AddressableDebugWindow.cs      … 実行時状態/リーク/Watcherをタブで表示（Play 中の窓口から pull）
│   │   └─ AddressableDependencyWindow.cs … 未登録の依存の確認（Play せずに静的に走査）
│   ├─ Attribute/
│   │   ├─ AddressablePathDrawer.cs       … プルダウン属性のPropertyDrawer（Dropdown と対）
│   │   └─ AddressableLabelDrawer.cs      … Label 用プルダウン属性のPropertyDrawer（Dropdown と対）
│   ├─ SettingsSync/
│   │   ├─ AddressableEditorSettingsSO.cs … エディタ専用設定の SO（3.1.1）
│   │   ├─ AddressableDebugWindowSettings.cs … デバッグウィンドウの設定（[Serializable]。更新間隔。Editor SO が保持）
│   │   ├─ AddressableLeakLogSettings.cs     … リークのログ出力の設定（[Serializable]。Editor SO が保持）
│   │   ├─ AddressableDependencyCheckSettings.cs … Dependency Check の出力先（[Serializable]。Editor SO が保持）
│   │   ├─ AddressableRegistrationSettings.cs … アセットの登録の設定（[Serializable]。自動登録のスイッチ・ルールの作成時の初期値・既定の追加先グループ〔手動一括登録のみ〕。Editor SO が保持）
│   │   ├─ AddressableEditorSettingsEditor.cs    … Editor SO の Inspector（見出し・ツールチップを表示言語で描く。editor-localization-spec.md 5.1）
│   │   └─ AddressableRuntimeSettingsEditor.cs   … Runtime の設定 SO の Inspector（同上）
│   ├─ Localization/                  … 表示言語の切り替え（構成は editor-localization-spec.md 2章。同梱の言語ファイル ja / en の JSON もここ）
│   └─ Registration/                  … アセットの登録（3.9(7)。構成は asset-registration-spec.md 9章）
│       ├─ AddressableAutoRegistrationRuleSO.cs … 自動登録のルール（利用者が何個でも作るデータ。3.1.1）
│       ├─ AutoRegistrationPostprocessor.cs     … インポートを受けて自動登録する（AssetPostprocessor）
│       ├─ AddressableRegistrationRuleWindow.cs … ルールの一覧・競合・当て直す
│       ├─ AddressableManualRegistrationWindow.cs … 手動一括登録
│       ├─ AddressableAutoRegistrationRuleEditor.cs … ルールの SO の Inspector とプレビュー
│       ├─ RegistrationRuleAssetCreator.cs       … ルールの SO の作成の入口（Create メニュー・一覧ウィンドウの「作成」）
│       ├─ RegistrationExtensionDropdown.cs / RegistrationLabelDropdown.cs / RegistrationGroupDropdown.cs … 拡張子・Label・グループの選択
│       ├─ RegistrationEntryWriter.cs            … AddressableAssetSettings に書き込む唯一の担当
│       ├─ RegistrationRuleScanner.cs            … ルールの SO・対象アセット・Addressables 側の状態の読み取り（キャッシュ付き）
│       └─ RegistrationRuleMatcher / MacroExpander / PlanBuilder / LogFormatter ほか … 判定・マクロの展開・計画の作成・ログの組み立て〔Unity 非依存〕と、その入力・出力の型
│
├─ Samples/                           … 動作確認用シーン（AddressableLoad/、AddressableInitialize/、AddressableDownload/）
│
└─ Tests/
    ├─ Editor/                        … EditModeテスト（偽の Worker・Watcher で Controller を単体テスト）
    └─ Runtime/                       … PlayModeテスト（Editor 上で実際の Addressables を使って確認）
```

方針：
- **最上位は Unity のパッケージ構成を守る**（`Runtime/` `Editor/` `Samples/` `Tests/`）。asmdef を Runtime / Editor / Tests で分ける Unity の慣例に従い、UPM パッケージとして配布可能な形を保つ
- **`Core/` 直下は共通物の置き場**。`AssetKey` や `AddressableLogger` のように、特定の機能に属さないものだけを置く。`Loading/` が `Download/` を参照したら設計違反、と機械的に判定できるようになり、フォルダ構成が依存ルールの見張り役になる
- **空になるフォルダは作らない**。Initialize には View が存在しないため、揃えるためだけの空フォルダは置かない（Model は `InitializeStateModel` を置く）。必要になった時点で追加する
- **Editor は役割別に5区分する**（`Window/` `Attribute/` `SettingsSync/` `Registration/` `Localization/`）。ただし asmdef は分割せず `Editor/` 配下1つに集約する。Unity が `Editor` フォルダを特別扱いし、Editor 用アセンブリは別建てになるため。`Registration/` は、`AddressableAssetSettings` に書き込む機能（3.9(7)）を、読み取り専用のウィンドウ（`Window/`）と分けて置くために足した（2026-10-24）
- **属性クラスは `Runtime/Attribute/` に置く**。`PropertyDrawer` のみ `Editor/Attribute/` に置く（3.9 (1) 参照）。同名フォルダだが別 namespace になる
- SO設定クラスの置き場は `Data/`（プロジェクト共通の命名慣習に合わせる）。クラス名は `Addressable<機能>Settings` のまま

### namespace 規則

ルートは `MAH.AddressableManager`。対応関係には**2つの軸**がある。

**(1) 機能単位ではフォルダと対応する**

```csharp
namespace MAH.AddressableManager.Core.Loading  { internal sealed class LoadingController { } }
namespace MAH.AddressableManager.Core.Download { internal sealed class DownloadController { } }
```

Download 側のファイルに `using MAH.AddressableManager.Core.Loading;` が現れたら即座に違反と分かる。**この機械的チェックが働くのは内部実装型どうしの参照**についてである。

**(2) 外部依存/内部実装の単位では対応しない**

区分基準は C# のアクセス修飾子ではなく、**「モジュール外部（利用者プロジェクト）のコードが直接その型に依存するか」**。外部依存型は `Runtime/Core/` フォルダに置かれていても namespace に `Core` を含めない。

```csharp
// 外部依存型：利用者が直接触れる。Coreを含めず短くする
namespace MAH.AddressableManager           { public sealed class AddressableManager : MonoBehaviour { } }
namespace MAH.AddressableManager           { public readonly struct AssetKey { } }
namespace MAH.AddressableManager           { public interface IAddressableHandler { } }
namespace MAH.AddressableManager.Attribute { public sealed class AddressablePathAttribute { } }
namespace MAH.AddressableManager.Attribute { public sealed class AddressableLabelAttribute { } }

// Editor：役割別4区分
namespace MAH.AddressableManager.Editor.Window       { internal sealed class AddressableDebugWindow { } }
namespace MAH.AddressableManager.Editor.Attribute    { internal sealed class AddressablePathDrawer { } }
namespace MAH.AddressableManager.Editor.SettingsSync { internal static class AddressableEditorSettingsLocator { } }
namespace MAH.AddressableManager.Editor.Registration { internal sealed class AddressableAutoRegistrationRuleSO { } }
```

`Runtime/` は namespace に含めない。型ごとの詳細な分類表とレイヤー別の命名パターンは `conventions.md`（7章・10章）が正とし、本書では重複して定義しない。

---

## 5. 補足・運用上の注意点

### 5.1 Addressablesの解放API
- 本モジュールは `LoadAssetAsync` 系のみを使用するため、解放は `Addressables.Release(handle)` に統一される
- `Addressables.InstantiateAsync` / `ReleaseInstance(go)` は使用しない（3.4.1参照）。この使い分けを実装者が意識する必要はない
- 同じkeyを複数回Loadした場合、内部参照カウントもN、Releaseも同じ回数呼ぶのが正しい挙動

### 5.2 他のManager（Audio/Fade/UI等）との比較における位置づけ
- グローバルアクセスの妥当性は「**状態を持つか／テストで差し替えたいか**」で判断する
  - 状態が薄く差し替えも不要なもの（Fade等）→ シンプルなstatic/シングルトンで十分
  - 状態を持ちリセット・テスト対象になるもの（Audio, UI, 今回のAddressable）→ 最低限シングルトンManager経由、理想はDI注入
- AddressableManagerは典型的に後者に該当するため、Manager経由でのハンドラー取得を採用する

### 5.3 今回あえて採用しない設計
- 素のstaticハンドラーが直接Registry/RefCountを保持する形（グローバル状態化・リセット不能のリスクがあるため不採用）
- Path/Reference間の完全なロケーション解決による同一判定（非同期コストが高く、割に合わないため不採用。実測で問題が出た場合のみ個別対応）
- 寿命所有をScopeのDisposeに一任する形（明示的なLoad/Releaseを主経路とする方針に変更したため不採用。寿命指定はあくまでセーフティネット）
- 破棄検知インターフェースの外部公開（実装漏れが `Release` 忘れと同じ失敗モードになり、安全網として機能しないため不採用）
- ピュアC#クラスのowner指定（GC依存で回収タイミングが不定のため不採用。Scene寿命またはIDisposableで代替）
- Manager内部実装をUniTask/DIコンテナ/R3等の特定ライブラリに直接依存させる設計（汎用モジュールとする方針のため不採用。UniTask は正として使うが、`#if` で出し分け、必須の依存にはしない。3.5.1参照）
- 1つの `await` できる値を複数の待ち手で共有すること（`.Preserve()` を含む）。2回目の `await` の誤りがコンパイルで検出できず、症状は重複ロードのタイミングなどでしか出ないため、待ち手ごとに値を作る共有完了（`SharedCompletion`）にする（2026-10-31 に、2026-10-05 の「共有は `Task` のままにする」を置き換えた。3.5.1・5.4参照）
- PlayerLoopへのシステム直接挿入（Controller側の実装は変わらず、実行順の確定性という利点が本モジュールの要件では効かないため不採用。`src/AddressableManager/Docs/specs/loading-spec.md` 9章参照）
- `InstantiateAsync` / `ReleaseInstance` 相当のAPI提供（戻り値のルールを一貫させるため不採用。Load系とInstantiate系という性質の異なる2系統を抱え込まない。3.4.1参照）
- EditorWindowからの手動Release／強制回収ボタン（Editorは読み取り専用とする原則を崩すため不採用。3.9参照）
- EditMode での実ロードによるサイズの算出（Play Mode Script 設定に算出値が左右され概算目的に適さないため不採用。静的算出方式で代替。3.9参照）
- 読み込み計測（ロード所要時間・リトライ回数等の記録と、ビルド後のオーバーレイ表示、デファインシンボルによるビルド含有の制御）（読み込み時間は端末・通信環境に大きく左右され、エディタやローカルでの計測値に意味が薄いため不採用〔2026-10-15〕。代わりに未登録の依存の確認を持つ。3.9(4)・6章参照）
- 未登録の依存の確認を、Addressables 標準の Analyze（Check Duplicate Bundle Dependencies）だけに任せる運用（Analyze はビルドでの重複バンドルを確認するもので、未登録の子が「どこでどのくらい使われているか」を見て Group を見直す目的とは違うため。ビルドレイアウトの計算を伴って重く、絞り込み・アセットへの移動もできない。軽い静的走査のウィンドウを持ち、ビルド結果の確認は Analyze に任せる。3.9(4)参照）
- 未登録の依存の確認の結果から、アセットを Addressables に登録する操作（「登録する」ボタン）（どのグループに入れるかは設計判断で、ルールを通さない登録は判断の根拠が残らないため。Dependency Check は読み取り専用とし、登録は自動登録のルールの追加か、手動一括登録で行う。3.9(4)(7)参照）〔2026-10-24 に理由を改めた。当初の「Editor 拡張は読み取り専用とする」は、アセットの登録の追加により範囲を限定した。6章〕
- 自動登録のルールの競合を、優先順位（ルールの並び順・フォルダの深さ）で解決すること（ルールの SO がプロジェクトに散らばると、どのルールが効いているかが分からなくなるため。競合したら登録せず、エラーにする。3.9(7)参照）
- 自動登録のオン／オフをデファインシンボルで切り替えること（自動登録はエディタだけの処理でビルドに含まれず、Editor SO のスイッチで最初に return すればコストは無視できるため。3.9(7)参照）
- 自動登録のマクロの展開を Addressables の `EvaluateString`（Profile の変数・任意の static プロパティ）に任せること（`[ ]` と `{ }` の扱いや Addressables のバージョンによる動きの違いに左右され、展開を EditMode テストできないため。よく使う値だけを独自のマクロとして用意する。3.9(7)参照）
- SO設定値の一覧・検証ウィンドウ（有用だが今回は見送り。必要になった時点で追加検討）
- Model を Editor へ直接公開し、Editor が Model を持つ設計（`InternalsVisibleTo` で `AddEntry` などの書き込み API も見え、読み取り専用が運用ルール頼みになるため不採用。読み取り専用の窓口 `IAddressableDebugReader` を介する。3.9参照）
- Runtime から Editor への push 通知（Model・Controller に発火箇所が散らばり、購読解除の漏れも増える。Window を閉じていても Runtime のコストが残るため不採用。Window 側の pull とする。3.9参照）
- Model ごとの読み取り専用インターフェイス（Window の依存が機能の数だけ増えるため不採用。窓口 `IAddressableDebugReader` の1つにまとめる。3.9参照）
- 機能ごとに設定 SO を分ける構成（利用者が作るアセットが機能の数だけ増え、ログレベルのような機能横断の設定の置き場所が定まらないため。Runtime SO／Editor SO の2つにまとめる。3.1.1参照）
- Service に `AssetReference` を渡す設計（入口ごとに派生 Service を作ることになるため。`AssetKey` を作る時点で string に変換し、Service は string キーだけを扱う。`src/AddressableManager/Docs/specs/loading-spec.md` 6章参照）
- `AddressableMultiLoaderBase<T>` の移植（複数アセットのロード中・ロード済み管理が、Controller の重複判定・RefCount と役割が重なるため。Label 一括ロードは、Controller が1件ずつの Load を組み合わせる形で提供する。3.4.2参照）
- Label 文字列を指定する解放（`ReleaseByLabel`）（同じ Label を複数の呼び出し元が読んだ場合に、どの分を解放するか決められないため。まとめチケットで解放する。3.4.2参照）
- Load 時に型を指定させる Label 一括ロード（`LoadByLabelAsync<T>` や型の配列を渡す形）（利用者が Label の中身の型を把握している必要があり、中〜大規模では運用できないため。型は取り出し時に指定する。3.4.2参照）
- 型を指定せずに `UnityEngine.Object` として読む Label ロード（画像が Sprite ではなく Texture2D として返り、型付きの単体 Load と食い違うため。ロケーションの型ごとに読む。3.4.2参照）
- まとめチケットからの基底型による取り出し（`GetAssets<Object>()` で、1枚の画像が Texture2D と Sprite の2件として重複して返るため。完全一致のみ）
- Label ロードで、1件の失敗後も残りのロードを最後まで待ってから巻き戻す方式（待ち時間が最も遅いロードに合わせて延びるだけで、確保は同期で終わっていて待つ必要がないため。最初の失敗で待機を打ち切る）
- 読み込みの前に `InitializeAsync` を必須にすること（Addressables は最初のロード時に自動で初期化されるため。既定では初期化を待たずに読み込む。SO の `LoadBeforeInitializePolicy.WaitForInitialize` を選んだ場合に限り、初期化の完了を待たせる〔2026-10-02〕。Download 導入時に起動シーケンスを再検討する〔2026-10-08 に検討し、Manager には起動シーケンスを持たせないことにした。6章〕。`src/AddressableManager/Docs/specs/initialize-spec.md` 5章参照）
- 初期化のリトライ・タイムアウト・失敗後のやり直し（Addressables 2.9.1 では、失敗した初期化をアプリの再起動までやり直せず、呼び直しても同じ失敗の操作が返るか同期で例外になるため。カタログ取得のタイムアウトは Addressables の `CatalogRequestsTimeout`、ネットワークの再試行は DownloadController で扱う〔2026-10-02〕。`src/AddressableManager/Docs/specs/initialize-spec.md` 8章・10.1参照）

### 5.4 パフォーマンス方針

汎用モジュールとして常駐するため、他システムへの負荷影響を抑える。ただし**削るべき箇所と実際にコストが乗る箇所は異なる**ため、以下の整理に基づいて対処する。

#### CPU負荷の実態

| 箇所 | 頻度 | 実測コスト |
|---|---|---|
| ポーリング走査（owner 200件の偽null判定） | 3秒に1回 | 数マイクロ秒（計測に現れない水準） |
| `LateUpdate` → イベント発火 → `Tick` の早期リターン | 毎フレーム | 1マイクロ秒未満／フレーム |

**ポーリング間隔を延ばすことは負荷対策としてほぼ意味を持たない**。3秒に1回の走査より、毎フレーム走る `LateUpdate` 中継の積算の方が総コストは大きいという逆転が起きている。したがって間隔値はメモリ許容遅延で決め（3.8参照）、負荷を削る場合は以下の「使わない機能のコストを払わせない」方針で対処する。

#### 使わない機能のコストを払わせない

- `Tick` の先頭で **owner寿命の登録数が0件なら即リターン**する。owner寿命を使わないプロジェクトでは実質的に何も走らない
- さらに徹底する場合、Manager 側で owner登録数が0→1で `OnLateUpdate += Tick`、1→0で `-=` と購読を動的に切り替え、`LateUpdate` の中継自体を止める（実装時に検討）

#### GCアロケーション対策（CPU時間より優先）

Unityではフレーム時間よりGCによるスパイクが体感品質に影響するため、以下を守る。

- **一時コレクションの都度確保を避ける**：`ReleaseLifetime` の一時退避に `ToArray()` / `ToList()` を使わず、フィールドに保持した `List` を `Clear()` して再利用する（`src/AddressableManager/Docs/specs/loading-spec.md` 3・11章のコード例参照）。大量のGameObjectが同時破棄されると `ReleaseLifetime` が連続で呼ばれるため、ここは実害が出やすい
- **クロージャ・ラムダの毎回生成を避ける**：Watcher へ渡すコールバックは使い回すか、フィールドにキャッシュした delegate を渡す
- **`foreach` による Dictionary 列挙**：`Dictionary` の enumerator は構造体のためボックス化しないが、`IEnumerable` にアップキャストすると発生する。列挙時は具体型のまま扱う
- **Label ロードの確保**：ロケーションのバッファは、`await` をまたいで並行に使われるため、`ReleaseLifetime` の使い回しバッファとは違い呼び出しごとに確保する（フィールドで使い回すと中身が混ざる）。まとめチケットの `GetAssets<T>()` は型ごとの結果をキャッシュして確保を避ける。`IReadOnlyList<T>` の `foreach` は列挙子がボックス化されるため、`for` で回す
- **非同期処理のアロケーション**：`Task<T>` は参照型のためLoad呼び出しごとにヒープ確保が発生する。シーン開始時に数百件を一括ロードする場面ではアロケーションが積むため、UniTask を正とし、無い場合は Awaitable を使う（3.5.1）。複数の呼び出し元が待つ共有完了は、ロード1件・初期化1回につき1回の確保に加え、待ち手ごとに確保する（再利用の要否は実装時に決める）

#### エディタ専用処理の除外

リーク検出のスタックトレース記録（`src/AddressableManager/Docs/specs/loading-spec.md` 10章）は取得コストが大きいため、`#if UNITY_EDITOR` で本番ビルドから完全に除外する。Editor 拡張のための記録（リーク・Owner の回収経路。3.9）も同様に、記録処理自体をエディタ専用とし、本番ビルドに残らないようにする。

---

## 6. 決定事項（更新履歴）

- カタログ更新／依存ダウンロードは `InitializeController` に内包せず、`DownloadController` として独立させる（3.3参照）。起動時のカタログ更新DLとゲーム内での追加DLの両方を同一の仕組みで扱う
- 読み込み・解放は明示的に行う方針とし、Scope の Dispose に暗黙で委ねる形は取りやめ。代わりに Load 時の寿命指定による自動回収をセーフティネットとして持つ（3.8参照）
- owner 指定は GameObject / Component のみに制限し、破棄検知は内部専用の Watcher コンポーネントで行う（3.8参照）
- フォルダ構成は Unity のパッケージ構成（`Runtime/` `Editor/` `Tests/`）を最上位に置き、機能ごとの縦割り＋ `Core/` 直下の共通置き場は `Runtime/Core/` 配下で構成する。`Editor/` は役割別に3区分（`Window/` `Attribute/` `SettingsSync/`）するが asmdef は分割しない。SO設定クラスの置き場はプロジェクト共通の慣習に合わせ `Data/` とする（クラス名は `Addressable<機能>Settings` のまま）（4章参照）→ **2026-10-24 に、`Editor/` に `Registration/` を足して4区分にした**（本章末尾を参照）
- namespace のルートは `MAH.AddressableManager`。機能単位（Initialize / Download / Loading）ではフォルダと対応させるが、外部依存/内部実装の単位では対応させない。区分基準は C# のアクセス修飾子ではなく「モジュール外部のコードが直接その型に依存するか」であり、外部依存型は `Core` を含めない短い namespace とする。型ごとの分類表とレイヤー別命名パターンは `conventions.md` を正とする（4章参照）
- フォルダ構成・個々のクラス名は暫定案であり、`conventions.md` の命名パターンに沿っていれば実装時に変更してよい。維持するのは構成の骨組みと namespace 規則（4章参照）
- 寿命種別は `Manual / Scene / Owner` の3種とする。`Phase` は Scene/Owner と異なり回収タイミングをユーザーが明示的に決める必要がある（＝実質手動管理）ため、独立させず `Manual` に統合する（3.8参照）
- owner破棄のポーリング間隔は既定3秒とし、Runtime SO 内の `AddressableLoadingSettings` で変更可能にする。ポーリング自体の無効化オプションも持たせる（3.8参照）
- Manager内部（Controller以下）はDIコンテナ導入の有無で実装を変更しない。汎用モジュールとするため、非同期処理は既定で `System.Threading.Tasks.Task` を使用し、UniTaskパッケージ導入時のみコンパイルシンボルで `UniTask` に切り替える。DIコンテナ・R3等には依存しない（3.5.1参照）→ **2026-10-05 に、UniTask を正とする方針へ改めた**（本章末尾を参照）
- ポーリングの実行主体は Manager（MonoBehaviour）とし、`LateUpdate` からイベント経由で Controller の `Tick(deltaTime)` を呼び出す。`Update` ではなく `LateUpdate` を使うのは、同一フレーム内で後から実行された `Destroy` を取りこぼさないため。PlayerLoopへの直接挿入は、Controller側の実装が変わらない一方でコストのみ増えるため採用しない。MonoBehaviourを継承するのは Manager / Editor / View のみとし、Controller・Service・Model はピュアC#に保つ（3.1 / 3.8参照）
- パフォーマンス方針として、ポーリング間隔はCPU負荷ではなくメモリ許容遅延で決める（間隔延長は負荷対策にならない）。負荷削減は「owner登録0件時に何も走らせない」方針で行い、CPU時間よりGCアロケーション（特に一時コレクションの都度確保）を優先して抑える（5.4参照）
- `LoadAsync<T>` は常に読み込んだアセットそのものを返す。`LoadAsync<GameObject>` はPrefabアセット本体を返し、`InstantiateAsync` / `ReleaseInstance` 相当のAPIはManagerでは提供しない。インスタンス化と破棄は実装者の責務とする（3.4.1参照）
- Version Defines の条件は Resource `com.cysharp.unitask`、Expression `2.5.0`（2.5.0以上、上限なし。Unity の自動整形に合わせた表記）とする。`.unitypackage` 導入時はシンボルが自動定義されないため、UPM経由での導入に統一する（3.5.1参照）
- Editor拡張は6項目（AddressablePathプルダウン／実行時状態確認／リーク検出一覧／読み込み計測／owner Watcher可視化／EditModeでのサイズ概算）を実装する。SO設定値の一覧表示と手動Release／強制回収ボタンは実装しない（後者は読み取り専用原則を崩すため）。同一データソースを参照する項目は1ウィンドウのタブ切り替えとする（3.9参照）→ **2026-10-15 に、読み込み計測を取りやめて未登録の依存の確認（Dependency Check）に差し替え、EditMode でのサイズ概算も作らないことにした**（本章末尾を参照）
- EditMode でのサイズ概算は AssetDatabase から静的に算出する方式とし、EditMode での実ロードは行わない（Play Mode Script 設定により測定値の意味が変わるため）。優先度は低（3.9参照） → **2026-10-15 に取り消した**（作らないことにしたため。本章末尾を参照）
- 読み込み計測はビルド後のアプリでも実行可能とする。ビルド含有の制御はデファインシンボル `ADDRESSABLE_PROFILING` で行い、そのシンボルはSOの enum（`None` / `TimeOnly` / `Detailed`）からエディタ拡張が自動更新する。SOフラグによる実行時制御のみでは「ビルドに含めない」ことが実現できないため採用しない。SOとシンボルの整合チェックと、CI/CDでのシンボル直接制御をセットで実装する（3.9.1参照）→ **2026-10-15 に取り消した**（読み込み計測を作らないため。本章末尾を参照）
- 計測結果の表示先は環境で分ける。エディタ実行時はEditorWindow（開発中にゲーム画面を覆わないため）、ビルド後は実行画面上のオーバーレイ表示とする。オーバーレイはUIライブラリ依存を避けるため `OnGUI`（IMGUI）で実装する。記録処理は `LoadProfiler` に集約し、両表示先が同一データソースを参照する（3.9参照）→ **2026-10-15 に取り消した**（読み込み計測を作らないため。本章末尾を参照）

- `LoadAsync<T>` の戻り値を `AssetTicket<T>` とし、読み込んだアセットをチケットに持たせる。チケットは通し番号で一意にし、発行済みチケットの管理で二重解放と `default` チケットの解放を防ぐ（詳細：`src/AddressableManager/Docs/specs/loading-spec.md` 3・7章）
- 同じキーを違う型で Load した場合は例外にする。`AssetKey` に型は含めない（詳細：`src/AddressableManager/Docs/specs/loading-spec.md` 3・7章）→ **2026-10-01 に取り消した**（判定キーを `(AssetKey, 型)` に変更。本章末尾を参照）
- ロード失敗時はエントリを即座に削除し、次の Load で再ロードできるようにする。ロード中に RefCount が 0 になった場合は、エントリは即座に削除し、Worker はロード完了後に解放する。失敗は `AssetLoadException` で通知する（詳細：`src/AddressableManager/Docs/specs/loading-spec.md` 3章）
- Manager は static な `AccInstance` を持ち、`DontDestroyOnLoad` とする。2つ目以降の Manager は破棄する。LoadingController は `Awake` で生成し、読み込み・解放は `InitializeAsync` に依存させない（詳細：`src/AddressableManager/Docs/specs/loading-spec.md` 2章）
- シーンのアンロード通知は Manager が `sceneUnloaded` を受けてイベントで中継する。Watcher の貼り付けは `IAssetLifetimeWatcherBinder` 経由とし、Controller から `AddComponent` を直接呼ばない（詳細：`src/AddressableManager/Docs/specs/loading-spec.md` 2・5章）
- `AddressableLoaderBase<T>` は別プロジェクトから移植して改修する。Service は string キーだけを受け取る `AssetLoaderService<T>` の1クラスとし、`AddressableMultiLoaderBase<T>` は移植しない。ログは内部用の `AddressableLogger` に置き換える（詳細：`src/AddressableManager/Docs/specs/loading-spec.md` 6章）
- テストは2本立てとする。EditMode テスト（`Tests/Editor/`）では偽の Worker・Watcher で Controller を単体テストし、Editor 上で実行する PlayMode テスト（`Tests/Runtime/`）では実際の Addressables を使って読み込み・明示解放・自動回収までを確認する（詳細：`src/AddressableManager/Docs/specs/loading-spec.md` 12章）
- （2026-09-24）読み込み・解放機能の実装にあたり、詳細仕様を `src/AddressableManager/Docs/specs/loading-spec.md` へ切り出した。本書（`addressable-manager-design.md`）は全体構成・他機能との関係を説明する概要とし、機能ごとの詳細仕様は `src/AddressableManager/Docs/specs/` 配下の機能仕様書を正とする運用にする（`CLAUDE.md` の「ドキュメント参照」も参照）
- （2026-09-25）設定 SO は機能ごとに分けず、`AddressableRuntimeSettingsSO`（実行時・ビルドに含まれる）と `AddressableEditorSettingsSO`（エディタ専用・ビルドに含まれない）の2つにまとめる。機能ごとの設定は `[Serializable]` の `Addressable<機能>Settings` として Runtime SO が保持し、Controller には自分の機能の設定だけを渡す（3.1.1参照）
- （2026-10-01）**Label 一括ロードを追加する**。`LoadByLabelAsync(label, lifetime, ct)` で Label が付いたアセットをまとめて読み込み、`AssetGroupTicket`（まとめチケット）を返す。解放は `Release(AssetGroupTicket)` で1回にまとめる。Load 時に型は指定せず、型は取り出し時（`TryGetAsset<T>` / `GetAssets<T>`）に完全一致で指定する。Label ごとの型を利用者が把握しなくてよくするため（3.4.2。詳細：`src/AddressableManager/Docs/specs/loading-spec.md` 13章）
- （2026-10-01）**重複ロードの判定キーを `(AssetKey, 型)` に変更する。5.3 で却下していた「`AssetKey` に型を含める設計」を取り消す**。`AssetKey` 自体は型を持たないままだが、Registry の判定キー `AssetEntryKey` に型を加えるため、実質的には却下した設計と同じ効果になる。方針を変えた理由：Label に型が混ざっていると、画像は Texture2D と Sprite の2件として読まれる。Label で読んだ Sprite と単体の `LoadAsync<Sprite>` を同じエントリで共有しつつ、型の混在を例外にしないためには、型を判定キーに含める必要がある。**失うもの**：単体の `LoadAsync<Texture2D>` と `LoadAsync<Sprite>` を取り違えたとき、これまでは `InvalidOperationException` で気づけたが、今後は何も言わずに成功する。同じアセットを別の型で読むと handle は別になるが、中身は同じバンドル内のオブジェクトなので、メモリ上で二重には持たない（3.4.2・3.6）
- （2026-10-01）解放は、すべてチケットの記録（`IssuedTicketRecord`）が持つエントリへの参照を起点にし、Registry にあるエントリが記録のものと同じ場合だけ取り除く。ロード失敗で削除されたエントリの後に、同じキーで新しいエントリができても、巻き戻しが新しいエントリの RefCount を誤って減らさないようにするため（既存の不具合の修正。3.4。詳細：`src/AddressableManager/Docs/specs/loading-spec.md` 3章）
- （2026-10-02）Label を Inspector のプルダウンから選べる `AddressableLabel` 属性を追加する。`AddressablePath` に引数を足して切り替える形にはせず、別の属性・Drawer・Dropdown として持つ（存在判定とプルダウンの構造が異なる〔Label はグループに属さず階層が無い〕ため）。Label 一括ロード API は変更しない（3.9(1)。詳細：`src/AddressableManager/Docs/specs/addressable-label-attribute-spec.md`）
- （2026-10-02）**初期化機能の仕様を決めた**。初期化は Runtime SO の `AddressableInitializeSettings` で自動（Manager の `Awake`）／手動を選べ、リトライ・タイムアウト・失敗後の扱い・`autoReleaseHandle` を設定できる。`InitializeAsync` は何度呼んでも同じ処理を共有し、完了・失敗は `await` とイベントの両方で通知する。初期化前の Load は既定で待たず（5.3 の方針を維持）、SO で待たせる設定を選べる。その場合も LoadingController は InitializeController を参照せず、`Core/` 直下の `IInitializationGate` を介して問い合わせる。あわせて、iOS/macOS の URL キャッシュで古いカタログの `.hash` が返る問題への対策（`InternalIdTransformFunc` でセッションごとのクエリを付ける。既定 OFF）を初期化機能に含める（3.2。詳細：`src/AddressableManager/Docs/specs/initialize-spec.md`）
- （2026-10-02）**初期化のリトライ・タイムアウト・失敗後のやり直しを取りやめ、`Failed` を終端状態にする**。Addressables 2.9.1 では、失敗した初期化をアプリの再起動までやり直せず、失敗の後は Load も失敗することを PlayMode テスト（`Tests/Runtime/AddressablesInitializeBehaviorTests.cs`）で確認したため。失敗は `await` とイベントの両方で知らせ、失敗した後は `LoadBeforeInitializePolicy` に関係なく `IInitializationGate` で Load を止めて `AddressableInitializeException` を返す。初期化の状態は新しい Model `InitializeStateModel` に持たせ、Manager から InitializeController に注入する。あわせて、URL キャッシュ対策のクエリは、カタログの `.hash` だけを対象に要求ごとに異なる値を付ける形に改めた（カタログ本体の URL は `.hash` の変換結果から作られるため。ゲーム内のカタログ更新の確認でも OS のキャッシュを避ける）（3.2・5.3。詳細：`src/AddressableManager/Docs/specs/initialize-spec.md` 2・5・6・8・10.1章）
- （2026-10-05）**初期化機能を実装した**。仕様は `src/AddressableManager/Docs/specs/initialize-spec.md` のとおり。フォルダ構成に `Initialize/Model/`（`InitializeStateModel`）を追加した（4章）
- （2026-10-05）**非同期処理は UniTask を正とし、UniTask が無い場合だけ `Task` を使う方針に改めた**。Unity のライフサイクルに組み込まれ、GC アロケーションが発生しにくいため。UniTask は必須の依存にはせず、`#if ADDRESSABLE_UNITASK_SUPPORT` による出し分けは維持する（実装の構造は変わらない）。例外として、複数の呼び出し元が待つ共有タスク（LoadTask・初期化のタスク）は、安全性を優先して UniTask 導入時も `Task` のままにする（何度 `await` しても安全なため）。あわせて、UniTask 版のリトライ待機が `timeScale = 0` の間に終わらない問題を直し、時間待ちを `AsyncCompat.Delay`（`ignoreTimeScale: true`）に集約した（3.5.1・5.3・5.4）

- （2026-10-05）**利用者が触れる入口を `IAddressableHandler` 1つに集約する**。初期化（`InitializeAsync`・`OnInitialized` / `OnInitializeFailed`・`AccInitializeState` / `AccIsInitialized`）を Manager から Handler へ移し、ダウンロードも同じ Handler に追加する前提とした（`IDownloadHandler` / `AccDownloadHandler` は作らない）。Manager の公開メンバーは `AccInstance` と `AccHandler` のみで、責務は機能内クラスの生成・寿命管理と Handler を返すことに絞る。あわせて、対象が広がったため `IAssetHandler` → `IAddressableHandler`、`AccAssetHandler` → `AccHandler` に改名し、実装クラスを機能フォルダの外（`Runtime/Core/Handler/`、namespace `MAH.AddressableManager.Core`）へ移した（複数機能の Controller を知るため。機能フォルダ同士の相互参照を避ける）。Handler のイベントはフィールドを持たず、アクセサで Controller のイベントへ直接つなぐ。**失うもの**：「成功後に初めてダウンロードの Handler を公開し、`null` で未初期化を判定する」案は使えなくなる。初期化前のダウンロードの扱いは Download の実装時に決める（3.3・3.7。詳細：`src/AddressableManager/Docs/specs/initialize-spec.md` 4・7.7章）

- （2026-10-08）**ダウンロード機能の仕様を決めた**。`IAddressableHandler` に、`AccDownloadStatus`・`HasUpdateAsync`（更新データの有無だけを返す。カタログは更新しない）・`CheckUpdateAsync`（カタログの確認・更新と、更新されたカタログ数・ダウンロードが必要なバンドル数・合計バイト数の返却）・`DownloadAsync` を追加する。対象は Label 単位で、SO（`AddressableDownloadSettings`）の既定値を、引数で上書きできる。`DownloadAsync` はカタログを更新せず、更新は `CheckUpdateAsync` で行う（間に件数と容量の確認ダイアログを挟めるようにするため）。**進捗の型を、3.3 の案の `IProgress<float>` から `IProgress<DownloadStatus>` に変える**（段階・バイト数・割合・リトライ回数を通知するため。`InitializeAsync` と同じ「引数で渡す」形で、イベントのような購読解除が要らない）。`DownloadStatus` は `readonly struct` とし、取得・通知した時点の値のコピーを渡す（前の処理の値を誤って読む問題が構造上起きない）。失敗の例外は `DownloadStatus` に持たせず、呼び出し元の `await` に `AddressableDownloadException` として返す。最後の通知（`Completed` / `Failed` / `Canceled`）は、通知先を外す前に届ける。初期化の前に呼ばれたときは、`IInitializationGate.WaitUntilInitializedAsync` で初期化の成功を待つ（未開始は `InvalidOperationException`、失敗は `AddressableInitializeException`）。Manager に起動時の自動ダウンロード（起動シーケンス）は持たせない（容量の確認ダイアログを挟めなくなるため）。**失うもの**：「1回取得して保持すれば、中身が更新され続ける」手軽さ（毎回 `AccDownloadStatus` を取得し直す）、`AssetKey` 単位のダウンロード（3.3 の案。7章へ移す）（3.3。詳細：`src/AddressableManager/Docs/specs/download-spec.md`）
- （2026-10-13）**Editor 拡張の共通基盤を決めた**（コードは機能1「実行時状態」の実装時に作る）。実行中の状態への到達手段は、Manager が `Awake` で Model を窓口 `AddressableDebugReader` にコンストラクタ注入し、`#if UNITY_EDITOR` の internal アクセサ `AccDebugReader` で読み取り専用の `IAddressableDebugReader` として渡す形にする。Window は既存の `AccInstance.AccDebugReader` だけを参照し、新しい static 状態は作らない。窓口は複数機能の Model を知るため、`AddressableHandler` と同じく機能フォルダの外（`Runtime/Core/DebugReader/`）に置く。取得は、Window 側の `List` へ `<対象>Snapshot`（`readonly struct`）をコピーする形で、更新は Window 側の pull（既定 0.5 秒・下限 0.1 秒）とする。窓口の名前は、Handler が「操作の窓口」を意味するため Reader とする。Editor 側の設定は `AddressableEditorSettingsSO` の `AddressableDebugWindowSettings` から操作し、SO は型で探して、0件ならエディター起動時に `Assets/MAH.Data/AddressableManager/Editor/` へ自動作成する。**3.9 に並記していた2案（static の弱参照リストへの登録／DI コンテナ経由で Manager を掴む）は採らない**（Manager がシングルトンで「リスト」は過剰なうえ `AccInstance` との二重の経路になる。DI コンテナには依存しない方針のため）。**失うもの**：Editor アセンブリに `InternalsVisibleTo` を与えるため Runtime の internal 型が Editor から見えるようになり、「窓口と `Snapshot` 以外の internal 型を使わない」ことは構造では防げず、レビューの観点になる（3.1.1・3.9。詳細：`src/AddressableManager/Docs/specs/editor-debug-window-spec.md`）（2026-10-14：SO の自動作成の既定パスを、MAH の他モジュールの置き場所に合わせて `Assets/Data/MAH/...` から `Assets/MAH.Data/...` へ変更した）

- （2026-10-15）**読み込み計測を作らないことにし、Editor 機能4を「未登録の依存の確認（Dependency Check）」に差し替えた**。読み込み時間は端末・通信環境（場所）に大きく左右され、エディタやローカルでの計測値に意味が薄いため。`LoadProfiler`・計測オーバーレイ（`ProfilingOverlayView`）・`ProfilingSymbolUpdater`・計測レベル（`ProfilingLevel`）・デファインシンボル `ADDRESSABLE_PROFILING` は作らず、3.9.1（計測のビルド含有制御）を削除し、7章の計測レベルの未決事項も削除した（コードは未作成だったため、ドキュメントのみの変更）。代わりに、登録済みアセットが参照する未登録のアセット（暗黙的に同梱され、複数グループから参照されると重複して同梱される）を、Play もビルドもせずに一覧で確認できるウィンドウ `AddressableDependencyWindow` を作る。データの出所が Play 中の Manager ではなく、アセットの静的な走査のため、デバッグウィンドウのタブにせず別のウィンドウにし、Runtime 側の型・窓口には触れない。Analyze（Check Duplicate Bundle Dependencies）はビルドレイアウトの計算を伴って重いため、軽い静的走査を自作する。**失うもの**：実機・ビルド後のロード時間の把握（必要なら Unity Profiler・Addressables の Event Viewer 等の既存ツールで行う）、バンドル単位の正確な重複判定（`Pack Separately` のグループ内の重複は、このウィンドウでは注記のみ）（3.9(4)。詳細：`src/AddressableManager/Docs/specs/editor-dependency-check-spec.md`）
- （2026-10-15）**Dependency Check の位置づけを明確にした**（上の差し替えの補足。上の項目の「重複して同梱される」「バンドル単位の正確な重複判定」という書き方は、この項目で置き換える）。このウィンドウの**一番の目的は、未登録のアセットが「どこで・どのくらい使われているか」を見て、Group の作成・設定（粒度・更新頻度の混在）を見直すこと**で、ビルドしたときにどうなるかは2番目（参考情報）。正確なビルド結果の確認は、Addressables 標準の Analyze に任せる。これに合わせて次を決めた。(1) 画面・出力・コードの「重複」を「**複数グループ**」に改める（`AccIsMultiGroup`。使っている親のグループが2つ以上であることを表し、ビルドでの重複を表さない）。(2) 登録済みのアセットを経由して届く親も、参照元に数える（Group の分け方を見直すには、どのグループがその子を使っているかを知りたいため）。ただし、参照元に**直接の使用か経由の使用か**を区別して付け、経由は経由先を表示する。(3) `Pack Separately` の印は残し、注記を「ビルド時の参考」の位置づけにする。(4) `Resources/` の印は残し、理由を「登録の漏れ・移行の漏れの手がかり」とする。**失うもの**：「複数グループ」の行が、ビルドで必ず重複するとは限らなくなる（経由先のバンドルに入るだけの場合がある）。これは Analyze で確認する（詳細：`src/AddressableManager/Docs/specs/editor-dependency-check-spec.md` 1章・2.2・7章）

- （2026-10-15）**EditMode でのサイズ概算（Editor 機能5）を作らないことにした**。`AssetDatabase.GetDependencies` でファイルサイズを積算した値は、元アセットのディスク上のサイズで、バンドルのサイズ（圧縮・プラットフォーム別のテクスチャ形式・重複同梱）・ダウンロードサイズ・キャッシュ上のサイズ（圧縮形式による）・実機メモリのいずれとも一致せず、使い道がキー同士の相対比較ぐらいに限られるため。重い依存の把握は Dependency Check のファイルサイズ列と並べ替えで足り、ビルド後のバンドルサイズは Addressables の Build Layout レポート、ダウンロードサイズは `CheckUpdateAsync`（`DownloadCheckResult`）で確認できる。これで Editor 拡張は、AddressablePath／AddressableLabel プルダウン・実行時状態・リーク検出一覧・owner Watcher の可視化・未登録の依存の確認の構成で完結する。**失うもの**：ビルドせずに、あるキーを読んだときに関わるファイルの規模をざっくり見る手段（3.9(6)）→ **2026-10-24 に、Editor 機能7「アセットの登録」を追加した**（本章末尾を参照）

- （2026-10-15）**配布用パッケージをリポジトリルート `AddressableManager/` に置き、開発は `src/AddressableManager/Assets/@MAH/AddressableManager/` で続ける**。リリース時に開発側から複製する（`Samples/` は `Samples~/` にして、利用者が Package Manager から選んで Import する形にする）。git URL の `?path=` を短くし、配布物に `package.json`・`Samples~` を置くため。**失うもの**：同じコードが2か所にあり、複製を忘れると配布物が古いままになる（複製の手順は `CONTRIBUTING.md` 4章）
- （2026-10-24）**パッケージの表示名と、配布・開発フォルダの名前を `MAH-AddressableManager` にした**（上の項目のフォルダ名は、この項目で置き換える）。配布フォルダはリポジトリルートの `MAH-AddressableManager/`（git URL は `?path=MAH-AddressableManager`）、開発フォルダは `src/AddressableManager/Assets/@MAH/MAH-AddressableManager/`。「AddressableManager」だけでは汎用的すぎて、他のパッケージと見分けにくいため。公開前なので、`?path=` の変更で困る利用者はいない。GitHub のリポジトリ名も `MAH-Lib/MAH-AddressableManager` に合わせる。パッケージID（`com.github.mah-lib.addressable-manager`）・namespace・asmdef・クラス名は変えない（クラス名の衝突は、Manager を1プロジェクトに1つしか入れない前提のため許容する）。**失うもの**：表示名・フォルダ名と、namespace・クラス名（`AddressableManager`）の表記がそろわなくなる

- （2026-10-24）**Editor 機能7「アセットの登録」（自動登録・手動一括登録）を追加することにした**（2026-10-24 に実装した。実装での変更は、次の項目）。Addressables の登録・グループ分け・アドレスの調整の手間を減らすため。**5.3 の「Dependency Check の結果から登録する操作は不採用（Editor 拡張は読み取り専用）」の理由を改め、読み取り専用の範囲を、実行時の状態を見る機能と静的な走査の機能に限定した**。アセットの登録だけは、利用者が SO に書いたルール、または手動一括登録の画面での指示に従う場合に限り `AddressableAssetSettings` に書き込む（「自動化すると誤った分割を招く」という当初の懸念には、ルールを利用者が明示的に書くこと・当て直す前に差分を見せること・競合を登録せずにエラーにすることで答える）。決めたことは次のとおり。(1) 自動登録は**既定で有効**とし、Editor SO のスイッチで自動登録だけを完全にオフにできる（他の登録ツールとの共存のため。手動一括登録にはスイッチを設けない）。(2) ルールは専用の SO（`AddressableAutoRegistrationRuleSO`）に1フォルダ1ルールで書き、何個でも作れる。ルールの SO は設定ではなくデータのため、3.1.1 の「機能ごとに SO を分けない」の対象外とする。ルールの SO は自動では作らない。(3) 1つのアセットに2つ以上のルールが当たったら競合とし、登録せずに Console にエラーを出す（優先順位では決めない）。(4) 差分がなければ書き込まず、何度実行しても同じ結果にする。ルールの SO の変更では自動で当て直さない。(5) マクロは独自の一覧と、よく使う Unity の値だけを展開し、`EvaluateString` には任せない。(6) `Editor/` に `Registration/` を足して4区分にする（namespace `MAH.AddressableManager.Editor.Registration`）。**失うもの**：Editor 拡張が読み取り専用だけで完結しなくなる（書き込む機能のレビューの観点が増える）。ルールを書いた時点から、利用者のグループが自動で書き換わるようになる（手で変えたエントリも、ルールに当たればルールの内容に戻る）。任意の static プロパティ・Profile の変数はマクロで使えない（3.1.1・3.9(7)・4章・5.3。詳細：`src/AddressableManager/Docs/specs/asset-registration-spec.md`）

- （2026-10-24）**アセットの登録を実装し、手動一括登録の画面を作り直した**。(1) 手動一括登録は、設定を先に決めて読み込む形をやめ、**先に対象の配下をすべて読み込んで一覧に出し、拡張子のオン／オフと行のチェックで絞り込む**形にした（設定箇所が多く手間がかかったため）。画面で決めるのは「追加先のグループ」と「Label」だけで、アドレスのテンプレート・除外パターン・新規グループの Template は、画面に出さず Editor SO の値を使う。(2) 一覧には**登録済みのアセットを出さない**（出さなかった件数だけ知らせる）。(3) Editor SO に、手動一括登録だけで使う「**既定の追加先グループ**」を足した（ルールの SO は、これまでどおり空なら `DefaultGroup`）。(4) ルールの SO の Create メニューは `CreateAssetMenu` をやめ、独自の `MenuItem` で作る（作成時に確実に初期値をコピーするため）。(5) グループ名を直接書く項目から「Template」の語を外し、「追加先のグループ」（`_targetGroup`）とした（Group Template の SO と紛らわしいため）。(6) 右クリックのメニューは `MAH → Register to Addressables...` の1階層にし、並び順の数字を離して他のパッケージのコマンドと区切る。(7) Addressables の `IsPathValidForEntry` は `internal` で呼べないため、同じ条件を自前で持った。**失うもの**：手動一括登録の画面で、アドレスのテンプレートと除外パターンを変えられない／登録済みのアセットのグループの移動ができない。画面の文言は日本語で、日本語と英語の切り替えは別の作業で実装する予定（詳細：`src/AddressableManager/Docs/specs/asset-registration-spec.md` 5・10・11.2章）
- （2026-10-24）**Editor 機能8「表示言語の切り替え」を実装した**（詳細は [`specs/editor-localization-spec.md`](specs/editor-localization-spec.md)）。公開パッケージで海外の利用者も使うため、Editor ツールの文言を日本語と英語で切り替えられるようにし、他言語は**利用者が JSON ファイルで足せる**形にした。(1) 言語は列挙型ではなく言語コードの文字列で持つ（利用者が足した言語を、コードの変更なしで扱うため）。(2) 翻訳データは JSON（同梱の ja / en は `Editor/Localization/`、利用者の追加分は `Assets/` 配下の `MAH.AddressableManager.Editor.<言語コード>.json`。同じ言語コードならキー単位で上書きし、足りないキーは英語にフォールバックする）。(3) 言語の設定は EditorPrefs（個人ごと。Editor SO には入れない）。(4) 対象は Editor ウィンドウ・Inspector・ダイアログ・Editor 側のログ・出力ファイルで、**Runtime のログ・例外は翻訳しない**（Runtime に仕組みを入れるとビルドにも載るため）。(5) `[Header]` / `[Tooltip]` は `PropertyField` が必ず描画してしまうため、Editor SO・ルールの SO・Runtime の設定 SO（とその設定クラス）から**外し**、文言は言語ファイルのキーで持つ（Runtime の属性を外しても、ビルドの挙動・内容は変わらず、むしろ日本語の文字列が Runtime から消える）。(6) 失敗メッセージの区切り「：」は、全角に固定せず `: ` にした。
- （2026-10-31）**非同期処理の代替を `Task` から `Awaitable` に改め、`Task` を完全に廃止することにした**（ドキュメントのみ先行して更新した。コードの移行はこれから行う）。UniTask と Awaitable はどちらも1回しか `await` できないため、規則をそろえて切り替え後の挙動を安定させること、WebGL にスレッド依存の `Task` の機能を持ち込まないことが理由。これに伴い、2026-10-05 に決めた「共有タスクは `Task` のままにする」を取り下げ、複数の待ち手は共有完了（`SharedCompletion`。名前は暫定）で扱う。5.3 の却下項目も、「1つの `await` できる値を複数の待ち手で共有すること」に置き換えた。代替案のうち、待ち手のポーリングは再開が最大1フレーム遅れ、EditMode テストの構造も崩れやすいため、共有部分だけ `Task` を残す案は `Task` の廃止という目的に反するため採らなかった。UniTask が無い環境では公開 API の戻り値の型が変わるため、**破壊的変更**である。**失うもの**：`Task` の複数回 `await` の安全性、未観測の例外の検出。`SharedCompletion` の保守が増える。WebGL ビルドでの動作は検証していない（3.5.1）

## 7. 未確定・今後の検討事項

- `AssetKey` 単位のダウンロード（3.3 の当初の案 `GetDownloadSizeAsync(AssetKey)`）。今回は Label 単位のみとした。必要になった時点で、Service の対象を広げる形で追加する（`src/AddressableManager/Docs/specs/download-spec.md` 9.4）
- 起動時の自動ダウンロード。確認ダイアログを挟めないため、SO の設定（既定 OFF）として追加する形を検討する（`src/AddressableManager/Docs/specs/download-spec.md` 9.3）
- owner登録数0→1／1→0でのイベント購読の動的切り替えを初期実装に含めるか（`Tick`先頭の早期リターンのみで十分か実測して判断。5.4参照）
- アセットの登録（3.9(7)）の実機での確認：自動登録の書き込みによる再インポートの連鎖が起きないか／Addressables 自身の Postprocessor との並び順（ソースでは問題ないと確認したが、大規模なプロジェクトでは未検証。`src/AddressableManager/Docs/specs/asset-registration-spec.md` 11.2）
