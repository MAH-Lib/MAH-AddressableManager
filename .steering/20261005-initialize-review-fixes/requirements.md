# 要求内容：初期化（Initialize）機能のレビュー指摘修正

> 作業ディレクトリ：`.steering/20261005-initialize-review-fixes/` ／ 作成日：2026-10-05 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.23f1
> 関連する永続的ドキュメント：`src/AddressableManager/Docs/specs/initialize-spec.md`（6.2・6.4・7.3・7.6・11章。更新要）／`src/AddressableManager/Docs/diagrams/initialize-class.md`・`initialize-sequence.md`（更新要）／`src/AddressableManager/Docs/addressable-manager-design.md`・`conventions.md`・`Docs/glossary.md`（影響なし）

## 1. 変更・追加する機能の説明

初期化機能のコードレビュー（db14079〜f5d964e）で見つかった指摘（🔴1・🟡2・🟢4）を、すべて修正する。

主な内容：
- 利用者の `IProgress` が例外を投げると、共有タスクが完了せず `InitializeAsync` の待機と Load の待機が終わらなくなる問題を直す（🔴）
- クエリ付きの `.hash` URL（CDN の署名トークン等）がキャッシュ対策の対象外になる問題を直す（🟡）
- サンプルに初期化失敗の購読と確認を足す（🟡）
- `Failed` の `InitializeAsync` のスタックトレース上書き、Manager に残った Task の観測処理、`Restore` で元の関数を外す問題、サンプルのコメントの誤りを直す（🟢）

今やる理由：本モジュールは UPM で他プロジェクトに配布する汎用モジュールで、利用側の `IProgress` の実装や URL の形は選べない。初期化が止まる不具合とキャッシュ対策の黙った無効化は、原因の特定が難しいため、配布前に塞ぐ。

## 2. ユーザーストーリー

- 利用者プロジェクトの実装担当者として、自分が渡した `IProgress` が例外を投げても、初期化と、初期化を待つ Load が止まらないでほしい
- 利用者プロジェクトの実装担当者として、CDN の署名付き URL を使っていても、カタログの URL キャッシュ対策が効いてほしい
- 利用者プロジェクトの実装担当者として、サンプルを見て、初期化の失敗の受け取り方（購読と、購読前に失敗していた場合の確認）をそのまま真似たい
- レビュアーとして、修正後も `Failed` が終端のまま、Initialize と Loading が互いを参照しない構成が保たれていることを確認したい

## 3. 受け入れ条件

### 🔴 進捗の通知先の例外（`InitializeStateModel`）
- [ ] 通知先の `Report` が例外を投げても、残りの通知先に値が届く
- [ ] 成功時の完了通知（1）で通知先が例外を投げても、状態は `Succeeded`、`OnInitialized` が1回発火し、`InitializeAsync` の待機が終わる
- [ ] `Tick` で通知先が例外を投げても、例外は呼び出し元に出ない（後ろの `LoadingController.Tick` が止まらない）
- [ ] 通知先の例外は `AddressableLogger.LogException` に流れる

### 🟡 キャッシュ対策の対象（`CatalogCacheBustingService`）
- [ ] `https://cdn.example.com/catalog.hash?token=abc` に `&t=<値>-<連番>` が付く
- [ ] `https://cdn.example.com/data.json?v=catalog.hash` は変換しない（同じ参照をそのまま返す）
- [ ] クエリが無い `.hash`、`.hash` 以外、`http` 以外の既存の規則は変わらない
- [ ] 対象外の InternalId で、新しい文字列を作らない

### 🟡 サンプル（`InitializeSampleView`）
- [ ] `OnInitializeFailed` を購読し、購読の直後に `AccInitializeState == Failed` も確認して、失敗を画面に出す
- [ ] 購読は `OnDestroy` と Manager の破棄ボタンの両方で解除される

### 🟢 その他
- [ ] `Failed` の後の `InitializeAsync` は、Service を呼ばず、毎回同じ例外のインスタンスを返す（スタックトレースは上書きされない）
- [ ] `InitializeController.StartAutoInitialize()` があり、Manager は Task の観測処理を持たない。失敗しても `UnobservedTaskException` に流れない
- [ ] `Restore` が上書きされていたときも、設定前の関数を保持したままにする。Warning の文面は、上書きした関数がこの関数を呼んでいない場合に限った内容になる
- [ ] サンプルの `OperationCanceledException` のコメント・ログが、View と Manager の両方の破棄を示している

### 既存機能とドキュメント
- [ ] 既存の EditMode テストが、期待値を変更した分（`TransformUsesAmpersandWhenQueryAlreadyExists` の入力）を除いて、すべて通る
- [ ] PlayMode テストが通る
- [ ] 追加したテストが、修正前のコードでは失敗する
- [ ] UniTask 導入時（`ADDRESSABLE_UNITASK_SUPPORT` 定義時）にコンパイルが通る。Task 版のコンパイルは、ユーザーが確認する
- [ ] `initialize-spec.md` の該当章（6.2・6.4・7.3・7.6・11章）と、2つの図が実装と一致している

## 4. 制約事項

- 共有タスクは UniTask 導入時も `Task` のまま。`#if ADDRESSABLE_UNITASK_SUPPORT` の外で UniTask を `using` しない（CLAUDE.md「設計の絶対ルール」）
- VContainer・R3・その他の DI/Rx コンテナに依存させない
- MonoBehaviour を継承するのは Manager / Editor / View のみ。Controller・Service・Model はピュア C#
- 3つの Controller は互いを参照しない。`Core/Initialize/` から `Core/Loading/` を `using` しない
- `Failed` は終端。`Failed` の後に `Addressables.InitializeAsync` を呼ぶ経路を作らない
- 一時コレクション・クロージャを都度確保しない。例外の観測は、キャッシュ済みの `OBSERVE_FAULT` を使い回す
- 変換関数は全ロケーションで呼ばれるため、対象外では文字列を作らない
- 公開 API（`IAddressableHandler`）の形は変えない
- 仕様書と挙動が変わる箇所は、同じ作業の中で `initialize-spec.md` と図も更新する
- `CHANGELOG.md` は更新しない（担当者の運用のため）
- 命名・namespace は `src/AddressableManager/Docs/conventions.md`、用語は `Docs/glossary.md` に従う
- 1ステップごとにユーザーが確認する。ユーザーの確認が済むまで、次のステップに進まない
- コミットはユーザーの指示があるまで行わない

## 5. スコープ外（今回やらないこと）

- `IAddressableHandler` の公開 API の変更（失敗の例外を取り出すアクセサの追加など）
- `InternalIdTransformFunc` 呼び出しのスレッドセーフ対応（Addressables はメインスレッドで解決するため）
- `CHANGELOG.md`・利用者向けドキュメント（`Documentation~/` など）の更新
- 図の `index.md` の登録の変更（既存の図の更新のみ）
