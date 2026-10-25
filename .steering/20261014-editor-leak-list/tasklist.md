# タスクリスト：AddressableManager Editor 機能2「リーク検出一覧」

> 対象：`design.md`（承認済み版） ／ 最終更新：2026-10-15 ／ ステータス：承認済み（全タスク完了。No.6 はレビュー指摘の対応で、ユーザーの確認待ち）

## タスク

状態：⚪未着手 → 🔵作業中 → ✅完了（🔴ブロック中はブロック理由を備考へ）

各タスクは1コミット単位で、**各タスクの完了ごとに、ユーザーが確認してから次へ進む**。コミットはユーザーの指示があるまで行わない。

**Claude の担当は実装（コードとテストコード、ドキュメントの作成・更新）まで**。コードレビュー・手動検証・ドキュメントの最終確認・CHANGELOG の更新は担当外（ユーザーが実施）。

| No | タスク | 状態 | 完了条件 | 備考 |
|---|---|---|---|---|
| 1 | Runtime 側：`LeakSnapshot`・`LeakRegistryModel`、`LoadingController`（コンストラクタ引数・`ReleaseLifetime` の記録・`_unloadingSceneName`）、窓口（`IAddressableDebugReader`・`AddressableDebugReader` の `CopyLeaksTo`）、Manager の注入、既存テストの生成引数の更新（`LoadingControllerTestBase`・`LoadingControllerPollingTests`・`AddressableHandlerInitializeTests`・`AddressableHandlerDownloadTests`・`AddressableDebugReaderTests`） | ✅ | コンパイルが通る（警告なし）。追加分がすべて `#if UNITY_EDITOR` で囲まれている（grep で確認）。`Core/Loading/` から `Core/DebugReader/` を `using` していない（grep で確認）。`ReleaseLifetime(LifetimeScopeId)` のシグネチャと `ReleaseInternal` の経路が変わっていない。既存の EditMode テストがすべて通る | 記録は既存の `LogLeakWarning` の隣に置くだけで、判定（`AccIsDelivered`・まとめチケット1枚1件）は既存のまま使う。スコープ名は、Owner の `GameObject` 参照をループの前に取り、記録するときだけ `name` を読む。コピーは `List` の `Clear()` と再利用で行い、`ToArray()` / `ToList()` を使わない。`Dictionary` は具体型のまま `foreach` する |
| 2 | Runtime のテスト：`LeakRegistryModelTests`・`LoadingControllerLeakTests` の新規、`AddressableDebugReaderTests` への `CopyLeaksTo` の追加 | ✅ | 次を検証するテストが通る：同じ確保元で回数が加算され1行のまま／トレースが違えば別行／単体と Label 一括が別行／最終時刻・スコープ名・通し番号の更新／ゴミの入った `List` が `Clear()` される／取得後に記録が増えても取得済みの値が変わらない／返し済みの回収は記録・ロード中の回収と明示 Release 済みは記録しない／まとめチケットは1枚1件／Owner・Scene のスコープ名／破棄済み Owner は名前なし。追加分を含めて EditMode テストがすべて通る | 既存の `LoadingControllerTestBase`・`FakeAssetLoaderWorker`・`AddressableDebugReaderTests` の書式に合わせる。実行結果を報告する |
| 3 | Editor：スタックトレースの共通部品（`StackTraceParser`・`StackTraceView`）と `StackTraceParserTests`、`RuntimeStateTab` の `DrawStackTrace` の差し替え | ✅ | コンパイルが通る（警告なし）。`StackTraceParserTests` が通る：Mono 形式の行／ファイル情報の無い行／Windows パス／相対パス／最初の利用者フレームの選択（`Runtime/`・`PackageCache` を飛ばす）。既存の `RuntimeStateTabTests` を含む EditMode テストがすべて通る。`RuntimeStateTab` から `_stackTraceStyle`・`_measureContent` が無くなっている | 実環境の `Environment.StackTrace` を1度採取し、テストの入力に使う。解析できない行は例外にせず、ジャンプ不可で表示する。`OpenFileAtLineExternal` が使えない場合は代替 API を探す。`Draw` の中で文字列・`GUIContent`・`GUILayoutOption` を毎回作らない（解析は初めて見たトレースの1回だけ）。動作確認（Play 中のジャンプ）はユーザーが実施 |
| 4 | Editor：`LeakListTab`（件数・「表示をクリア」の既読扱い・行・ジャンプボタン・開閉）と、`AddressableDebugWindow._tabs` への登録 | ✅ | コンパイルが通る（警告なし）。Editor 側が窓口と `Snapshot` 以外の internal 型（Model・Controller）を使っていない（grep で確認）。タブに Registry や Runtime の記録を書き換える操作がない。`Clear()` で Snapshot・開閉・基準・`StackTraceView` が空になる。既存の EditMode テストがすべて通る | 窓口への参照を保持しない（`Refresh` の引数で受け取って使い切る）。表示用の文字列は `Refresh` で作る。行の並びは `AccSequence` の降順。クリアは、基準の通し番号と、行 ID → その時点の回数で表現する。動作確認（Play 中の表示）はユーザーが実施 |
| 4.5 | ログ出力の設定とロジック：`AddressableLeakLogSettings`、`AddressableEditorSettingsSO` への項目の追加、`LeakLogFormatter`、`LeakLogWriter`、テスト3種（`AddressableLeakLogSettingsTests`・`LeakLogFormatterTests`・`LeakLogWriterTests`） | ✅ | コンパイルが通る（警告なし）。次を検証するテストが通る：設定の既定値（出力先 `Logs/AddressableManager`・自動出力 OFF）と、空・空白の出力先が既定値に戻ること／文字列の組み立て（見出しの件数と出力日時・渡した行の順・回数は渡した値・単体と Label 一括の表記・スコープ名と、名前が無いときの ID・handle・呼び出し元〔無ければ「不明」〕・スタックトレース全文・0件のときの文）／出力先の解決（相対・絶対・Windows のドライブレター）／手動のファイル名の書式と、同名のときの連番／`WriteLastPlay` の1世代の入れ替え（2回目で `-prev` に移り、3回目で最初の内容が消える）／手動のログが消えない／書き込めないパスで null が返り例外が出ない。追加分を含めて EditMode テストがすべて通る。Editor 側が窓口と `Snapshot` 以外の internal 型を使っていない（grep で確認）。既存の SO アセットの Inspector に「リーク」の項目が既定値で出る | 組み立てと IO を分け、組み立ては Unity 非依存にする。IO の失敗は、例外を握って Console に警告を出し、null を返す。`WriteLastPlay` が触るのは固定名の2ファイルだけ（パターンでの一括削除をしない）。`File.Move` は上書きできないため、先に `-prev` を削除する。テストの一時フォルダは、`Path.GetTempPath()` の下に一意の名前で作り、テスト後に削除する。Test Runner での実行はユーザーに依頼する |
| 4.6 | ログ出力の UI と自動出力：`LeakListTab`（「ログ出力」ボタン・`_rowCounts`・0件で無効）、`LeakLogAutoExporter` | ✅ | コンパイルが通る（警告なし）。Editor 側が窓口と `Snapshot` 以外の internal 型を使っていない（grep で確認）。「ログ出力」は、押しても行の数が変わらず、「表示をクリア」の動きが変わっていない。`LeakLogAutoExporter` が、窓口を保持せず、毎回 `AccInstance` から取り直している。既存の EditMode テストがすべて通る | ボタンの並びは `[ログ出力][表示をクリア]`。出力先は、押したときに `AddressableEditorSettingsLocator.FindSettings()`（無ければ既定値）から読む。自動出力は、SO のフラグが ON で Manager が生きているときだけ動き、0件でも「リークはありませんでした」で上書きする。`ExitingPlayMode` の中で例外を投げない。動作確認（Play 中のボタン・Play 終了時のファイル）はユーザーが実施 |
| 5 | ドキュメント更新：spec（実装状況・2.1・2.4・4.1・7章・8章の状態表・8.2 の新設・8.1 のスタックトレース表示）、`addressable-manager-design.md` 3.9(3)、`loading-spec.md` 10章、`diagrams/editor-foundation-class.md`、`conventions.md`・`Docs/glossary.md`（新しい命名・用語が出た場合のみ）。ログ出力の分として、spec 3.1 の設定表（`AddressableLeakLogSettings`）と、8.2 のログ出力の節（手動・自動・固定名の上書きと1世代・0件のとき・Play 停止そのものが原因の回収を含まないこと）も含める | ✅ | 新しい型名（`LeakRegistryModel`・`LeakSnapshot`・`LeakListTab`・`StackTraceView`・`StackTraceParser`・`AddressableLeakLogSettings`・`LeakLogFormatter`・`LeakLogWriter`・`LeakLogAutoExporter` ほか）が spec・design・loading-spec・図で表記揺れなく一致している（grep で確認）。spec 8章の状態表が更新され、8.2 が 8.1 と同じ構成（概要・画面・Snapshot と窓口・設計の判断と理由・改修ガイド・注意点・テスト）で書かれている。図・リンク先が存在する（シェルで確認） | spec には実装手順・フェーズを書かない（動作・内部構成・改修ガイド）。`CHANGELOG.md` は更新しない |
| 6 | コードレビュー（コミット e655606）の指摘対応：🔴 `LeakRecord`・`ParsedTrace` のフィールドを規約どおりに（private＋アクセサ・メソッド）、引数名 `a, b` の改名／🟡 Manager の入れ替わりで全タブを `Clear()`（`AddressableDebugWindow`・`InstanceID` の比較）、モジュール内のフレームの判定をメソッドの名前空間に（`StackTraceParser`・`TraceFrame.AccFileName`）／🟢 `EditorProjectPath` の新設、`AddressableLogger.LogInfo` の追加と Editor のログの置き換え、`LogLeakWarning` の削除（文言は `LoadingController.CreateLeakMessage` へ）、`Dispose` での `LeakRegistryModel.Clear()`、`LeakListTab.Refresh` の作り直しの省略、`ResolveFolder` の null 対策、テストの更新・追加、spec・loading-spec・conventions・図の更新 | ✅ | コンパイルが通る。EditMode テストがすべて通る（391 件成功・失敗 0）。コード内に `LogLeakWarning`・`LeakLogWriter.GetProjectRoot`・Editor のログの `"[AddressableManager] "` の直書き（`AddressableEditorSettingsLocator` を除く）が残っていない | 実装計画書の方針どおり。Locator のログは、`Debug.Log` の context 引数（作成した SO の Ping）を使うため、`AddressableLogger` に置き換えない。動作確認（Manager の入れ替わり・ジャンプ・ログ出力の Console 表示）はユーザーが実施 |

## 完了の定義（Definition of Done）

- [x] requirements.md の受け入れ条件をすべて満たす
- [x] 本番ビルドに含まれる側（`#if UNITY_EDITOR` の外）に、`LeakRegistryModel`・`LeakSnapshot`・記録の呼び出し・窓口のメソッドがない
- [x] ログ出力が Editor アセンブリだけで完結し、Runtime・窓口のインターフェイスに変更がない。自動出力が削除・移動するのは固定名の2ファイルだけ
- [x] 既存テストと追加テストがすべて通る（Claude が実行して結果を報告する）
- [x] 影響のある永続的ドキュメント（Docs/）の更新が完了している
- [x] コードレビュー・手動検証（Play 中の表示・ジャンプ・ログ出力・Play 終了時の自動出力の確認）・ドキュメントの最終確認が完了している（Claude の担当外。ユーザーが実施）
- [x] `CHANGELOG.md` は更新していない（担当者の運用）
