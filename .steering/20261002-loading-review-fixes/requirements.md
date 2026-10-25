# 要求内容：Loading 機能のレビュー指摘修正

> 作業ディレクトリ：`.steering/20261002-loading-review-fixes/` ／ 作成日：2026-10-02 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.23f1
> 関連する永続的ドキュメント：`src/AddressableManager/Assets/Docs/specs/loading-spec.md`（3章・5.4・5.7・10章・13章。更新要）／`src/AddressableManager/Assets/Docs/addressable-manager-design.md`（影響なし。概要レベルの記述は変わらない）／`src/AddressableManager/Assets/Docs/conventions.md`・`Docs/glossary.md`（影響なし）

## 1. 変更・追加する機能の説明

Addressable の読み込み・解放処理のコードレビューで見つかった指摘（🔴1・🟡5・🟢4）を、すべて修正する。

主な内容：
- ロード中に Manager が破棄されると、破棄の後にリトライが走り、handle がリークする問題を直す（🔴）
- Owner 寿命の監視データ（`OwnerWatchRegistryModel`・`AssetLifetimeWatcherBinder`）が、例外時や Owner の大量生成と破棄のあとに残り続ける問題を直す（🟡）
- ロード中に寿命が終わった分に、誤ったリーク警告が出る問題を直す（🟡）
- `Time.deltaTime` によるポーリングが、`timeScale = 0` の間に止まる問題を直す（🟡）
- Watcher の `ToArray()`、`ct.Register` のクロージャ、`AssetGroupTicket` の依存方向、回復不能な例外のリトライ、古いコメントを直す（🟡・🟢）

今やる理由：本モジュールは UPM で他プロジェクトに配布する汎用モジュールで、利用側の使い方（Manager の作り直し、Owner の大量生成、ポーズ）を選べない。配布前に、破棄・寿命終了とロードが重なるエッジケースを塞いでおく。

## 2. ユーザーストーリー

- 利用者プロジェクトの実装担当者として、ロード中に Manager を破棄（シーン切り替え・全体リセット）しても handle がリークしないように、破棄とロードの競合が安全に処理されてほしい
- 利用者プロジェクトの実装担当者として、Owner 寿命の GameObject を大量に生成・破棄しても、メモリや走査コストが増え続けないようにしたい
- 利用者プロジェクトの実装担当者として、リーク警告は「自分が Release を呼び忘れたとき」だけ出てほしい。呼び出し元が結果を受け取る前に寿命が終わった場合は、警告に惑わされたくない
- 利用者プロジェクトの実装担当者として、ポーズ中（`timeScale = 0`）でも、破棄された Owner のアセットが回収されてほしい
- 利用者プロジェクトの実装担当者として、アドレスを書き間違えたときは、リトライの待ち時間なしに、すぐエラーで気づきたい
- レビュアーとして、修正後も解放経路が `ReleaseInternal` の1本のままで、二重解放や RefCount の不整合が起きないことを確認したい

## 3. 受け入れ条件

### 🔴 ロード中の破棄（`AddressableLoaderBase`）
- [x] ロード中に Manager を破棄しても、破棄の後にリトライが走らない。完了した handle は、Worker 自身が解放して抜ける
- [x] ロード中に破棄された場合、`LoadAsync` は `OperationCanceledException` で終わる。共有ロードタスクは Faulted にならない（未観測の例外が出ない）
- [x] 破棄済みの Worker に対する2回目以降の `Dispose()` は、何もしない
- [x] ロード中でないときの `Dispose()` は、従来どおり即座に handle を解放する

### 🟡 Owner 寿命の監視（`LoadingController`・`AssetLifetimeWatcherBinder`・`AssetLifetimeWatcher`）
- [x] 空のキー（`default(AssetKey)`）を渡した `LoadAsync` は、Owner の Watcher を登録する前に `ArgumentException` で失敗する。Watcher の登録が残らない
- [x] チケットが1件も無いスコープに対して `ReleaseLifetime` が呼ばれても、`OwnerWatchRegistryModel` と Watcher の登録が外れる（以後、ポーリングで再走査されない）
- [x] `AssetLifetimeWatcherBinder` が持つ Watcher の記録が、Owner の破棄・明示 Release・ポーリング回収のどの経路でも取り除かれる（`AccBoundCount == 0` に戻る）
- [x] `AssetLifetimeWatcher.OnDestroy` で、`ToArray()` による配列の確保をしない

### 🟡 リーク警告の条件（`LoadingController`・`IssuedTicketRecord`・`IssuedTicketRegistryModel`）
- [x] 呼び出し元がチケットを受け取る前に寿命が終わった場合（単体の Load・Label 一括ロードとも）は、リーク警告を出さない。`OperationCanceledException` は従来どおり届く
- [x] 呼び出し元がチケットを受け取った後に、Release されないまま寿命が終わった場合は、従来どおりリーク警告が出る（まとめチケットは1枚につき1件）
- [x] 受け取り前に寿命が終わったまとめチケットも、無効化される（`AccIsReleased == true`）

### 🟡 ポーリング（`AddressableManager`）
- [x] `Time.timeScale = 0` の間も、ポーリング間隔が進み、破棄された Owner が回収される

### 🟢 その他
- [x] `AssetGroupTicket` が、`MAH.AddressableManager.Core.Loading` の型（`AssetEntryKey`・`AssetEntryModel`）を参照しない。`using MAH.AddressableManager.Core.Loading;` が無い
- [x] `AsyncCompat.WithCancellation` が、呼び出しのたびにクロージャを確保しない
- [x] `InvalidKeyException`（アドレスの書き間違い）は、リトライせずに1回で `AssetLoadException` になる。それ以外の例外は、従来どおりリトライされる
- [x] `LoadingController` の古いコメント「Owner寿命の実装ステップで拡張」が無い

### 既存機能とドキュメント
- [x] 既存の EditMode テスト（全75件）が、期待値を変更した分を除いて、すべて通る。期待値を変えるのは、リーク警告の条件に関わる1件（`LoadByLabelAsyncLifetimeEndsWhileLoadingThrowsOperationCanceled`）と、`Fill` の引数に関わる `AssetGroupTicketTests` のみ
- [x] PlayMode テストが通る
- [x] 追加したテストが、修正前のコードでは失敗する（🔴は、Dispose 中のロードの後に `LogAssert.NoUnexpectedReceived()` で検出する。ロードが即座に完了して再現できない場合は `Assert.Inconclusive`）
- [x] UniTask 導入時（`ADDRESSABLE_UNITASK_SUPPORT` 定義時）にコンパイルが通る。Task 版のコンパイルは、ユーザーが確認する
- [x] `loading-spec.md` の該当章（3章・5.4・5.7・10章・13章）が、実装と一致している

## 4. 制約事項

- 非同期処理の既定は `System.Threading.Tasks.Task`。UniTask は `#if ADDRESSABLE_UNITASK_SUPPORT` の出し分けの中でのみ使い、直接 `using` しない（CLAUDE.md「設計の絶対ルール」）
- VContainer・R3・その他の DI/Rx コンテナに依存させない
- MonoBehaviour を継承するのは Manager / Editor / View のみ（`AssetLifetimeWatcher` は既存の内部専用 Component であり、新しく増やさない）。Controller・Service・Model はピュア C#
- 明示解放・自動回収・巻き戻しは、すべて既存の `ReleaseInternal` の1経路を通す。別の解放経路を作らない
- RefCount は await の前に加算する。キャンセル・失敗時は `catch` で巻き戻す
- 一時コレクション・クロージャを都度確保しない。`ToArray()` / `ToList()` を新しく加えない
- `IAssetLifetimeWatcherBinder`・`IAssetLoaderWorker`・`IAssetHandler` のシグネチャは変えない（Fake とテスト、利用者への波及を避けるため）
- 公開 API（`IAssetHandler`・`AssetTicket<T>`・`AssetGroupTicket` の公開メンバー）の形は変えない
- 仕様書と挙動が変わる箇所は、同じ作業の中で `loading-spec.md` も更新する
- `CHANGELOG.md` は更新しない（担当者の運用のため）
- 命名・namespace は `src/AddressableManager/Assets/Docs/conventions.md`、用語は `Docs/glossary.md` に従う
- 1ステップごとにユーザーが確認する。ユーザーの確認が済むまで、次のステップに進まない

## 5. スコープ外（今回やらないこと）

- `AssetTicket<T>` が `LifetimeScopeId`（Loading の型）を持つ依存の解消（公開 API の形に関わるため）
- `IssuedTicketRegistryModel.TryRemove` の `List.Remove` による O(N²)（unity-performance-review で実測してから判断する）
- リトライ待機中に Dispose されたとき、待機を即座に打ち切る仕組み（内部 CTS）。最大 `_retryDelayMs` だけ遅れて抜ける動作を許容する
- Manager 破棄後に、利用者が持ち続けている `AssetGroupTicket` を無効化すること（発行済みのまとめチケットを辿る索引が無いため）
- `CHANGELOG.md`・利用者向けドキュメント（`Documentation~/` など）の更新
