# タスクリスト：AddressableManager Editor 機能3「owner Watcher の可視化」

> 対象：`design.md`（承認済み版） ／ 最終更新：2026-10-15 ／ ステータス：承認済み（全タスク完了。No.2・3 はテスト実行結果の確認待ち、No.4 は Play 中の表示確認待ち、No.5 は最終確認待ち）

## タスク

状態：⚪未着手 → 🔵作業中 → ✅完了（🔴ブロック中はブロック理由を備考へ）

各タスクは1コミット単位で、**各タスクの完了ごとに、ユーザーが確認してから次へ進む**。コミットはユーザーの指示があるまで行わない。

**Claude の担当は実装（コードとテストコード、ドキュメントの作成・更新）まで**。コードレビュー・手動検証・ドキュメントの最終確認・CHANGELOG の更新は担当外（ユーザーが実施）。

| No | タスク | 状態 | 完了条件 | 備考 |
|---|---|---|---|---|
| 1 | Runtime：回収の記録。`OwnerReclaimRoute`・`OwnerReclaimSnapshot`・`OwnerReclaimRegistryModel`、`OwnerWatchRegistryModel` の名前保持（`_ownerNames`・`TryGetOwnerName`）、`LoadingController`（コンストラクタ引数・`HandleOwnerDestroyed`・`PollDestroyedOwners` と `RecordOwnerReclaim`・`Dispose` での `Clear()`）、Manager の注入、既存テストの生成引数の更新（`LoadingControllerTestBase`・`LoadingControllerPollingTests`・`AddressableHandlerInitializeTests`・`AddressableHandlerDownloadTests`） | ✅ | コンパイルが通る（警告なし）。追加分がすべて `#if UNITY_EDITOR` で囲まれている（grep で確認）。本番ビルド側で `_onOwnerDestroyed` が従来どおり `ReleaseLifetime` のまま。`Core/Loading/` から `Core/DebugReader/` を `using` していない（grep で確認）。`ReleaseLifetime(LifetimeScopeId)` のシグネチャと `ReleaseInternal` の経路が変わっていない。既存の EditMode テストがすべて通る | 記録は、入口（Watcher の通知・ポーリング）の `ReleaseLifetime` の直前に置く。`IsWatching` のときだけ記録し、チケット数は回収前に読む。delegate はコンストラクタで1回だけ代入する（毎回生成しない）。コピーは `List` の `Clear()` と再利用で行い、`ToArray()` / `ToList()` を使わない。`Dictionary` は具体型のまま `foreach` する |
| 2 | Runtime のテスト：`OwnerReclaimRegistryModelTests`・`LoadingControllerOwnerReclaimTests` の新規 | ✅ | 次を検証するテストが通る：同じ経路・同じ名前で1行／経路か名前が違えば別行／回数・チケット累計・最終時刻・最終 ID・通し番号の更新／`Clear()`／ゴミの入った `List` が `Clear()` される／取得後に記録が増えても取得済みの値が変わらない／`OnDestroy` 経路の記録／ポーリング経路の記録（登録時の名前）／チケット数が回収前の件数／全チケットを明示 Release した後の破棄は記録しない／`OnDestroy` 回収後のポーリングとポーリング回収後の `OnDestroy` で二重記録しない／`Dispose` 後は記録しない／回収の動作（RefCount・リークの記録）が従来どおり。追加分を含めて EditMode テストがすべて通る | 既存の `LoadingControllerTestBase`・`FakeAssetLifetimeWatcherBinder`（`SimulateOwnerDestroyed`）の書式に合わせる。ポーリングは、owner を `DestroyImmediate` して `Tick` で再現する。実行結果を報告する |
| 3 | Runtime：監視中の Owner の読み取りと窓口。`OwnerWatchSnapshot`、`OwnerWatchRegistryModel.CopyOwnersTo`、`AssetLifetimeWatcher.IsRegistered`、`IAddressableDebugReader`・`AddressableDebugReader` の `CopyWatchedOwnersTo`・`CopyOwnerReclaimsTo`（コンストラクタに2つの Model を追加）、Manager の注入の追加、`AddressableDebugReaderTests` の生成引数の更新とケースの追加 | ✅ | コンパイルが通る（警告なし）。追加分がすべて `#if UNITY_EDITOR` で囲まれている（grep で確認）。次を検証するテストが通る：`CopyWatchedOwnersTo` の生存・破棄済み・登録時の名前・本物の `AssetLifetimeWatcherBinder` で Bind した場合の `AccHasWatcher`/`AccIsRegistered`・Watcher を貼っていない場合の false・Scene/Manual スコープが含まれない・ゴミの入った `List` が `Clear()` される・取得後の不変／`CopyOwnerReclaimsTo`／コンストラクタの null 引数。追加分を含めて EditMode テストがすべて通る | 窓口に Model を書き換えるメソッドを足さない。`GameObject`・`Watcher` の参照を `Snapshot` に持たせない（`bool` と文字列だけ）。EditMode では `new GameObject()` と本物の Binder で Watcher の貼り付けを検証する |
| 4 | Editor：`OwnerWatcherTab`（監視中の行・状態の色・InstanceID ごとのチケット数・回収の行と並び・合計）と、`AddressableDebugWindow._tabs` への登録 | ✅ | コンパイルが通る（警告なし）。Editor 側が窓口と `Snapshot` 以外の internal 型（Model・Controller・`AssetLifetimeWatcher`）を使っていない（grep で確認）。タブに Registry や Runtime の記録を書き換える操作がない。`Clear()` で3つの `List`・集計が空になる。`Draw` の中で文字列・`GUIContent`・`GUILayoutOption` を毎回作っていない。既存の EditMode テストがすべて通る | 窓口への参照を保持しない（`Refresh` の引数で受け取って使い切る）。表示用の文字列・合計・並びは `Refresh` で作る。回収の行の並びは、ポーリング優先、その中で `AccSequence` の降順。色は Dark / Light 両方で読める中間の明るさにし、`RuntimeStateTab` と考え方を揃える。タブの並びは「実行時状態」「リーク」「Owner」。動作確認（Play 中の表示）はユーザーが実施 |
| 5 | ドキュメント更新：spec（冒頭の実装状況・2.1・2.4・4.1・8章の状態表・8.3 の新設）、`addressable-manager-design.md` 3.9(5)、`loading-spec.md`（5.4・7.3）、`diagrams/editor-foundation-class.md`、`conventions.md`・`Docs/glossary.md`（新しい命名・用語が出た場合のみ） | ✅ | 新しい型名（`OwnerReclaimRegistryModel`・`OwnerReclaimSnapshot`・`OwnerReclaimRoute`・`OwnerWatchSnapshot`・`OwnerWatcherTab` ほか）が spec・design・loading-spec・図で表記揺れなく一致している（grep で確認）。spec 8章の状態表が更新され、8.3 が 8.1 と同じ構成（概要・画面・Snapshot と窓口・設計の判断と理由・改修ガイド・注意点・テスト）で書かれている。図・リンク先が存在する（シェルで確認） | spec には実装手順・フェーズを書かない（動作・内部構成・改修ガイド）。注意点に、名前は登録時のものであること・`CopyOwnersTo` の負荷・Dispose と明示 Release 済みは記録しないことを書く。`CHANGELOG.md` は更新しない |

## 完了の定義（Definition of Done）

- [x] requirements.md の受け入れ条件をすべて満たす
- [x] 本番ビルドに含まれる側（`#if UNITY_EDITOR` の外）に、`OwnerReclaimRegistryModel`・各 `Snapshot`・記録の呼び出し・窓口のメソッドがなく、`_onOwnerDestroyed` が従来どおり `ReleaseLifetime` である
- [x] 解放経路（`ReleaseInternal`）と `ReleaseLifetime(LifetimeScopeId)` のシグネチャが変わっていない
- [x] 既存テストと追加テストがすべて通る（Claude が実行して結果を報告する）
- [x] 影響のある永続的ドキュメント（Docs/）の更新が完了している
- [x] コードレビュー・手動検証（Play 中の表示・`OnDestroy` 経路とポーリング経路の区別・Watcher の状態の色の確認）・ドキュメントの最終確認が完了している（Claude の担当外。ユーザーが実施）
- [x] `CHANGELOG.md` は更新していない（担当者の運用）
