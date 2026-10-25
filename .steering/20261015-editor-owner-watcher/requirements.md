# 要求内容：AddressableManager Editor 機能3「owner Watcher の可視化」

> 作業ディレクトリ：`.steering/20261015-editor-owner-watcher/` ／ 作成日：2026-10-15 ／ ステータス：承認待ち ／ 対象 Unity バージョン：6000.3.25f1
> 関連する永続的ドキュメント：`src/AddressableManager/Docs/specs/editor-debug-window-spec.md`（冒頭の実装状況・2.1 の注入 Model の記述・4.1 の表・8章の状態表を更新し、8.3 に機能3の節を追記）／`src/AddressableManager/Docs/addressable-manager-design.md`（3.9(5) を実装済みの記述と spec 8.3 への参照に更新）／`src/AddressableManager/Docs/specs/loading-spec.md`（5.4 の Watcher に渡す delegate の記述を、`HandleOwnerDestroyed` 経由に追随）／`src/AddressableManager/Docs/diagrams/editor-foundation-class.md`（新しい型を追加）／`src/AddressableManager/Docs/conventions.md`・`Docs/glossary.md`（新しい命名パターン・用語が出たときだけ確認して更新）

## 1. 変更・追加する機能の説明

Owner 寿命で監視中の GameObject と、`AssetLifetimeWatcher` の貼り付け状態を一覧し、Owner の回収が **`OnDestroy` 経由か、ポーリング経由か**を区別して記録する、デバッグウィンドウの新しいタブ「Owner」を追加する。

背景：Owner の破棄検知は、Watcher の `OnDestroy`（主経路）→ 低頻度ポーリング（取りこぼし対策）→ Manager の Dispose（最終防衛線）の多重の安全網で成り立っている（設計仕様書 3.8）。ポーリング経由の回収が多いことは、Watcher が取りこぼしている（例：一度も Active にならず `OnDestroy` が呼ばれない GameObject）ことを示すが、現状はそれを確かめる手段がない。設計仕様書 3.9(5) の項目で、1・2 と同じデータソース（Registry と寿命スコープ）を見るため、同じウィンドウのタブとして実装する。

決定済みの仕様：

- **監視中の Owner の一覧**：`OwnerWatchRegistryModel` が保持する Owner を、名前・InstanceID・生存状態・Watcher の貼り付け／登録状態・確保中のチケット数で表示する
- **回収経路の区別**：回収の契機を `OnDestroy` と `Polling` に分けて記録する。記録は、リークと同じく**集約して溜める**（キー：回収経路＋Owner 名）。回数・回収したチケット数の累計・最終時刻・最終 InstanceID を持つ
- **回収の動作は変えない**：解放は従来どおり `ReleaseLifetime` → `ReleaseInternal` の1経路。記録は、その呼び出しの直前に、呼び出し元（Watcher からの通知／ポーリング）で行う
- **Owner 名は登録時に文字列で保持する**（Editor 限定）。ポーリングで見つかる Owner は偽 null で名前が取れず、取りこぼしの調査に名前が必要なため

## 2. ユーザーストーリー

- モジュールの開発者として、破棄検知の多重の安全網が期待通り動いているか確かめるために、監視中の Owner と Watcher の貼り付け状態を Play 中に見たい
- モジュールの開発者として、Watcher の取りこぼしに気づくために、`OnDestroy` 経由とポーリング経由の回収の回数を区別して見たい
- 実装担当者として、どの Owner がポーリングでしか回収されないかを特定するために、回収の記録を Owner 名ごとに見たい
- 実装担当者として、「回収されるはずの Owner が残っている」状況を調べるために、破棄済みなのに未回収の Owner を見分けたい
- レビュアーとして、本番ビルドの挙動・コストが変わらないことを確認するために、記録と表示がすべて Editor 限定であってほしい

## 3. 受け入れ条件

### 回収経路の記録（Runtime・Editor 限定）
- [ ] Watcher の `OnDestroy` 経由で Owner が回収されると、経路 `OnDestroy` で1件記録される
- [ ] ポーリングで破棄済みの Owner が見つかって回収されると、経路 `Polling` で1件記録され、名前は登録時のものが使われる
- [ ] 記録するチケット数は、回収前（`ReleaseLifetime` の前）に残っていた件数である
- [ ] 同じ経路・同じ Owner 名の回収が繰り返されると、行が増えず、回数・チケット累計が加算され、最終時刻・最終 InstanceID・通し番号が更新される。経路か名前が違えば別の行になる
- [ ] 全チケットを明示 Release して監視が外れた Owner の破棄は、記録されない（回収ではないため）。`OnDestroy` で回収済みの Owner が、後のポーリングで二重に記録されない
- [ ] Manager の Dispose（`Dispose` 後の通知を含む）は記録されない
- [ ] 回収の動作（RefCount・リーク警告・リークの記録）が従来どおり変わらない
- [ ] 窓口 `IAddressableDebugReader` に `CopyWatchedOwnersTo`・`CopyOwnerReclaimsTo` が追加され、取得済みの `Snapshot` は、取得後に Model が変わっても値が変わらない

### 「Owner」タブ
- [ ] ツールバーに「実行時状態」「リーク」「Owner」の3タブが並ぶ
- [ ] 監視中の Owner の行に、名前・InstanceID・確保中のチケット数・Watcher の状態（貼り付け済みで登録あり：緑／貼り付けなしまたは未登録：赤）が表示される。破棄済みでまだ回収されていない Owner は、黄色で「破棄済み・未回収」と表示される
- [ ] 同じ Owner で複数のチケットを確保しても、行は1つで、チケット数だけが増える
- [ ] 回収の記録の行に、経路（`OnDestroy` / ポーリング）・Owner 名・回数・チケット累計・最終時刻が表示され、ポーリングの行が上に、その中で新しい順に並ぶ
- [ ] 回収の合計（`OnDestroy` の回数・ポーリングの回数・ポーリングの割合）が見出しに表示される
- [ ] 明示 Release で全チケットが尽きた Owner は、次の更新で監視中の一覧から消える
- [ ] Play していない間は「未起動」と表示され、Play の終了で一覧・記録が破棄される。別のタブから戻った直後は、更新間隔を待たずに取り直される
- [ ] ウィンドウ上に、Release・強制回収・Runtime の記録を消す操作が存在しない

### 構造
- [ ] Runtime 側の追加（`OwnerReclaimRegistryModel`・各 Snapshot・enum・記録の呼び出し・窓口のメソッド・Manager の注入・`OwnerWatchRegistryModel` の名前保持）が、すべて `#if UNITY_EDITOR` で囲まれ、本番ビルドの動作を変えない
- [ ] `OwnerReclaimRegistryModel` は Manager が生成し、`LoadingController` と `AddressableDebugReader` へコンストラクタ注入される（Controller 内で new しない）
- [ ] 窓口に Model を書き換えるメソッドが無い。Editor 側が、窓口と `Snapshot` 以外の internal 型（`AssetLifetimeWatcher` を含む）を使っていない
- [ ] コピー処理が `List` の再利用で行われ、`ToArray()` / `ToList()` を使っていない。`Draw` の中で文字列・`GUIContent`・`GUILayoutOption` を毎回作っていない
- [ ] 解放経路（`ReleaseInternal`）と `ReleaseLifetime(LifetimeScopeId)` のシグネチャが変わっていない。Watcher に渡す delegate は、フィールドにキャッシュされている（毎回生成しない）
- [ ] `Core/Loading/` のファイルから `Core/DebugReader/` を `using` していない

### テスト・ドキュメント
- [ ] 記録の集約（Model）、Controller の記録の有無（`OnDestroy`／ポーリング／明示 Release 済み／二重記録／Dispose 後）、窓口のコピー（生存・破棄済み・Watcher の貼り付け状態）に対する EditMode テストが追加され、既存テストを含めてすべて通る
- [ ] spec の 8章に機能3の節（8.3）が、8.1 と同じ構成で追記され、状態表が更新されている。新しい型名が spec・design・loading-spec・図で表記揺れなく一致している

## 4. 制約事項

- Editor 拡張は**読み取り専用**。窓口に書き換えメソッドを足さず、手動 Release・強制回収・記録の消去のボタンを作らない（CLAUDE.md「設計の絶対ルール」）
- Controller・Model はピュアC#のまま（`OwnerWatchRegistryModel` が既に `GameObject` を持つ点は既存のまま）。Snapshot に `UnityEngine.Object` の参照を持たせない（名前は文字列で持つ）
- 明示解放と自動回収は同じ経路（`ReleaseInternal`）を通す。新しい解放経路を作らない
- 3つの Controller は互いを参照しない。窓口は機能フォルダの外（`Runtime/Core/DebugReader/`）に置く
- DI コンテナ・R3 に依存しない（Manager の手動 `new` と注入）
- 記録・表示処理は `#if UNITY_EDITOR` で本番ビルドから完全に除外する。一時コレクションを都度確保しない。クロージャ・ラムダを毎回生成しない
- 描画は IMGUI（既存の Editor コードと揃える）
- 既存の Runtime の挙動（解放経路・ログ・例外）を変えない。`LoadingController` のコンストラクタ引数の追加に伴い、生成箇所（Manager・テスト基盤）を更新する
- CHANGELOG は更新しない（担当者の運用）。コードレビュー・手動検証・ドキュメントの最終確認は Claude の担当外

## 5. スコープ外（今回やらないこと）

- 「表示をクリア」（既読扱い）・ログのファイル出力（「リーク」タブにはあるが、本機能では見送り。必要になれば同じ方式で足す）
- 明示 Release で全チケットが尽きたことによる監視の解除の記録（回収ではない）
- Dispose（最終防衛線）による強制解放の記録（リークと同じく対象外）
- Scene 寿命の可視化（「実行時状態」タブで足りている）
- 監視中 Owner の Hierarchy での選択（Ping）ボタン
- Play 終了後の記録の保持（Manager の破棄で消える。共通基盤の仕様のまま）
- 記録の件数上限と設定項目（Owner 名の種類で頭打ちになる）
- 本番ビルド・実機での記録
- 読み込み計測（機能4）・サイズ概算（機能5）
