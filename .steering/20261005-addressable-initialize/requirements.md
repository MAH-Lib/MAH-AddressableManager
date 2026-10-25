# 要求内容：AddressableManager 初期化（Initialize）機能

> 作業ディレクトリ：`.steering/20261005-addressable-initialize/` ／ 作成日：2026-10-05 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.23f1
> 関連する永続的ドキュメント：`src/AddressableManager/Docs/specs/initialize-spec.md`（今回の機能の正。仕様は確定済みで、実装による更新は 10.1 最終行〔`.hash` が変換関数に渡るかの実機確認結果〕のみ）／`src/AddressableManager/Docs/addressable-manager-design.md`（3.2・5.3・更新履歴。2026-10-02 に更新済みで追加の更新なし）／`src/AddressableManager/Docs/diagrams/initialize-class.md`・`initialize-sequence.md`（更新済み。実装が図と食い違った場合のみ更新）／`src/AddressableManager/Docs/specs/loading-spec.md`（LoadAsync / LoadByLabelAsync の先頭に初期化ゲートの待機が入るため、3.1・13.2 への参照追記が要るか実装時に確認）

## 1. 変更・追加する機能の説明

`AddressableManager` に、`Addressables.InitializeAsync` の実行と進行状態（未開始・実行中・成功・失敗）の管理を追加する。初期化を自動（Manager の `Awake`）か手動（利用者が `InitializeAsync` を呼んだ時点）かを Runtime SO で選べる。完了・失敗は、`await` できる `InitializeAsync` と、イベント（`OnInitialized` / `OnInitializeFailed`）の両方で知らせる。

あわせて次の2つを含める。
- **初期化前・失敗後の Load の扱い**：Loading が `IInitializationGate` だけを通して「読み込んでよいか」を問い合わせる。既定は待たずに読み込む（`PassThrough`）が、初期化に失敗した後は設定に関係なく Load を止めて `AddressableInitializeException` を返す
- **リモートカタログの URL キャッシュ対策**：iOS / macOS で OS の URL キャッシュにより古い `.hash` が返る問題を、`InternalIdTransformFunc` で `.hash` の URL に要求ごとのクエリを付けて避ける（既定 OFF）

今やる理由：今の実装は初期化の完了・失敗を知る手段が無く、失敗の後も Load を試して原因の分からないエラー（キーが無効）になる。また Addressables は失敗した初期化をアプリの再起動までやり直せないため（PlayMode テストで確認済み）、失敗を確実に伝えて Load を止める必要がある。

## 2. ユーザーストーリー

- 実装担当者として、起動シーンの処理で初期化の完了を待ってから次へ進めるように、`await AddressableManager.AccInstance.InitializeAsync()` で、実行中の自動初期化をそのまま待ちたい
- 実装担当者として、利用規約の同意後など任意のタイミングで初期化できるように、手動モードを選びたい
- 実装担当者として、初期化の失敗に気づいて再起動を促せるように、失敗を例外とイベントの両方で受け取りたい
- 実装担当者として、イベントを購読する前に初期化が終わっていても取りこぼさないように、`AccIsInitialized` / `AccInitializeState` で状態を確認したい
- 実装担当者として、初期化が終わる前に Load が走って不整合を起こさないように、必要なら初期化の完了を待ってから読み込ませたい（`WaitForInitialize`）
- 実装担当者として、iOS / macOS でサーバーのカタログ更新が反映されないことを避けるために、URL キャッシュ対策を設定だけで有効にしたい
- レビュアーとして、Initialize と Loading が互いを参照しないように、橋渡しが `Core/` 直下の `IInitializationGate` だけになっていてほしい

## 3. 受け入れ条件

### 初期化の実行と状態
- [ ] 自動モード（既定）で Play すると、Manager の `Awake` で初期化が始まり、完了後に `AccInitializeState == Succeeded`、`AccIsInitialized == true` になる
- [ ] 手動モードでは、`InitializeAsync` を呼ぶまで `NotStarted` のままで、呼んだ後に `Succeeded` になる
- [ ] `InitializeAsync` は何度呼んでも初期化を1回だけ行い、実行中・完了済み（成功・失敗とも）の同じ処理を共有する。`Succeeded` の後はすぐ完了する
- [ ] 状態は `NotStarted → Running → Succeeded / Failed` のみ遷移し、`Succeeded` / `Failed` からは変わらない（`InitializeStateModel` が保証する）
- [ ] Addressables が初期化済みの場合、自動モードでも `Awake` の中で `Succeeded` になり、`OnInitialized` が発火する（購読前に完了しても `AccIsInitialized` で確認できる）

### 完了・失敗の通知
- [ ] `OnInitialized` が成功時に1回発火する
- [ ] 失敗時は、待っているすべての `InitializeAsync` に `AddressableInitializeException`（`InnerException` に元の例外）が届き、`OnInitializeFailed` が1回発火する。自動モードで待っている利用者がいなくても発火し、未観測の例外にはならない
- [ ] `Failed` の後の `InitializeAsync` は、`Addressables.InitializeAsync` を呼ばずに、毎回同じ `AddressableInitializeException` を返す
- [ ] `Addressables.InitializeAsync` が同期で例外を投げた場合も、`Failed` になる
- [ ] 呼び出し元の `ct` のキャンセルは、その呼び出し元の待機だけを `OperationCanceledException` で終わらせ、初期化・他の呼び出し元には影響しない
- [ ] `progress` に待機中の進捗（0〜1）が届き、完了時に必ず 1 が届く。後から合流した呼び出し元にも合流以降の進捗が届く
- [ ] Manager の破棄で、待機中の `InitializeAsync` が `OperationCanceledException` で終わり、破棄後に状態が変化しない

### 初期化前・失敗後の Load
- [ ] `PassThrough`（既定）では、初期化の前でも Load がそのまま動く（既存のサンプル・テストの動作が変わらない）
- [ ] `WaitForInitialize` では、`Running` の間は Load が完了を待ち、`Succeeded` 後は待たない。`NotStarted`（手動モード）では `InvalidOperationException` になる
- [ ] 初期化に失敗した後は、設定に関係なく `LoadAsync` / `LoadByLabelAsync` が `AddressableInitializeException` になり、RefCount・チケット・Watcher に何も残らない。`Release` は止まらない
- [ ] 待機中に `ct` がキャンセルされると `OperationCanceledException`、Manager が破棄されると `ObjectDisposedException`、寿命（owner・シーン）が終わると `OperationCanceledException` になり、いずれも何も残らない。待機の前から無効な寿命は従来どおり `ArgumentException`
- [ ] ゲートが開いているときの Load は、`await` も確保も追加で発生しない

### URL キャッシュ対策
- [ ] 設定 OFF（既定）のときは `InternalIdTransformFunc` に触れない
- [ ] ON のとき、`http` で始まり `.hash` で終わる InternalId にだけ `?t=<起動時の値>-<連番>`（既に `?` があれば `&t=…`）を付け、それ以外（`.bin`・`.json`・ローカル・アセットバンドル等）は同じ文字列をそのまま返す。連番は呼ぶたびに進む
- [ ] 既に設定されている変換関数を上書きせず、先に通してから連結する
- [ ] Manager の破棄で、設定前の関数へ戻す。ただし `InternalIdTransformFunc` が自分の関数のままのときだけで、利用者が上書きしていた場合は触らず Warning を出す
- [ ] 重複した Manager（2つ目以降）は変換関数を設定しない。Play を繰り返しても `t=` が二重に付かない

### 設定とその他
- [ ] Runtime SO の Inspector に「初期化」見出しの下に、`_mode`（既定 Auto）・`_loadBeforeInitialize`（既定 PassThrough）・`_autoReleaseHandle`（既定 ON）・`_enableCatalogCacheBusting`（既定 OFF）が表示される
- [ ] `_autoReleaseHandle` が OFF のとき、handle を保持し、Manager の破棄（`Dispose`）で `Addressables.Release` する
- [ ] `OnDestroy` の順序が「イベントの購読解除 → LoadingController の `Dispose` → InitializeController の `Dispose` → 変換関数の復元」になっている
- [ ] 既存の EditMode テスト（Loading 関連）が、偽のゲートを渡す修正だけで通る。既存の PlayMode テスト（`LoadingPlayModeTests`・`AddressablesInitializeBehaviorTests`）が通る
- [ ] 仕様書 11章のテスト（EditMode：`InitializeController`・`InitializeStateModel`・`CatalogCacheBustingService`・ゲート／PlayMode：自動・手動の初期化とイベント）が追加され、すべて通る

## 4. 制約事項

- 非同期処理の既定は `System.Threading.Tasks.Task`。UniTask は `#if ADDRESSABLE_UNITASK_SUPPORT` の出し分けの中（公開 API のシグネチャ）でのみ使い、直接 `using` しない。共有タスク・ゲート・Service の戻り値は、シンボルに関係なく常に `Task`（CLAUDE.md「設計の絶対ルール」・仕様書 7.3）
- VContainer・R3・その他の DI/Rx コンテナに依存させない。配線は `AddressableManager.Awake` での手動 `new` のみ
- MonoBehaviour を継承するのは Manager のみ。InitializeController・InitializeStateModel・Service はピュア C#
- InitializeController と LoadingController は互いを参照しない（`Core/Loading/` に `using …Core.Initialize;` を書かない。逆も同様）。橋渡しは `Core/Interfaces/IInitializationGate` のみ
- `InitializeStateModel` は Controller の中で `new` せず、Manager から注入する
- `Failed` の後に `Addressables.InitializeAsync` を呼ぶ経路を作らない（Addressables が `ArgumentOutOfRangeException` を同期で投げるため）
- ライフサイクルの中継は `LateUpdate`。`OnLateUpdate`・`OnInitialized` 等の購読と `InternalIdTransformFunc` の設定は、`Awake` / `OnDestroy` で必ず対にする
- 一時コレクション・クロージャを都度確保しない（進捗の通知先リストはフィールドで使い回す。delegate はキャッシュする）。変換関数は対象外の InternalId で新しい文字列を作らず、比較は `StringComparison.Ordinal`
- 変換関数のログは、クエリを付けたときだけ `AddressableLogger.Log`（Verbose）で出す
- 仕様は確定済み。仕様と矛盾する実装を思いついた場合は、黙って実装せず先に仕様書の更新提案を出す
- 命名・namespace は `src/AddressableManager/Docs/conventions.md`、用語は `Docs/glossary.md` に従う。コードコメントは日本語
- `.meta` は Unity が生成するため手で作らない。`CHANGELOG.md` は更新しない

## 5. スコープ外（今回やらないこと）

- 初期化後のカタログ更新・依存ダウンロード、およびそれらを続ける「起動シーケンス」（DownloadController の実装時に Manager 側で組み立てる。仕様 9.3）
- 初期化のリトライ・タイムアウト・失敗後のやり直し（`Failed` は終端。カタログ取得のタイムアウトは Addressables の `CatalogRequestsTimeout`、ネットワークの再試行は Download で扱う）
- 初期化のサンプルシーン、Editor 拡張（Window での状態表示）
- `.hash` の InternalId が変換関数に渡ることの実機確認（リモートカタログ環境が必要。確認できた場合のみ仕様 10.1 を更新する）
- UniTask 版のコンパイル確認（UniTask 導入環境でユーザーが行う）
- `CHANGELOG.md`・利用者向けドキュメント（`Documentation~/` など）の更新
