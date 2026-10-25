# タスクリスト：AddressableManager Editor 機能4「未登録の依存の確認（Dependency Check）」

> 対象：`design.md`（承認済み版） ／ 最終更新：2026-10-15 ／ ステータス：承認済み（全タスク完了。テストの実行・手動検証・ドキュメントの最終確認もユーザーが完了済み）

## タスク

状態：⚪未着手 → 🔵作業中 → ✅完了（🔴ブロック中はブロック理由を備考へ）

本 steering は、実装を先に行ったため、実施した内容の履歴として作成した。コミットはユーザーの指示があるまで行わない。

**Claude の担当は実装（コードとテストコード、ドキュメントの作成・更新）まで**。コードレビュー・手動検証・ドキュメントの最終確認・CHANGELOG の更新は担当外（ユーザーが実施）。

| No | タスク | 状態 | 完了条件 | 備考 |
|---|---|---|---|---|
| 1 | 出力の共通化：`EditorTextFileWriter` の新規と `LeakLogWriter` の委譲化、`EditorTextFileWriterTests` | ✅ | コンパイルが通る。`LeakLogWriter` の公開シグネチャ・警告文言が変わらない。既存の `LeakLogWriterTests` と新規テストが通る | `WriteLastPlay` はリーク固有のため `LeakLogWriter` に残し、`ENCODING` だけ共有する |
| 2 | 設定：`AddressableDependencyCheckSettings`・SO への追加・`FindDependencyCheckSettings()`・`AddressableDependencyCheckSettingsTests` | ✅ | コンパイルが通る。設定のテストが通る。既存の SO を開いても既定値が補われる | `OnValidate` で補完するためマイグレーション不要 |
| 3 | 集約：`DependencyCheckInput`・`DependencyCheckReferrer`・`DependencyCheckRow`・`DependencyCheckAggregator`・`DependencyCheckAggregatorTests` | ✅ | 集約のテストがすべて通る。Unity の型を使っていない | 行は `[Serializable]` のクラス |
| 4 | 整形：`DependencyCheckLogFormatter`（`FormatSize` を含む）・`DependencyCheckLogFormatterTests` | ✅ | 仕様書 2.5 の出力例と一致するテストが通る | 初回は参照元のパスの幅を行ごとにそろえていて失敗した。仕様書の例に合わせ、出力全体の最長にそろえるよう修正した |
| 5 | 走査：`DependencyCheckScanner` | ✅ | コンパイルが通る。Settings 無し・キャンセル・完了を `ScanResult` で返し、進捗バーが `finally` で消える。グループ・エントリを書き換える API を呼んでいない | Unity のアセットに依存するため、テストは書かず手動で確認する |
| 6 | ウィンドウ：`AddressableDependencyWindow`（Scan・一覧・展開・注記・移動・絞り込み・並べ替え・テキスト出力）・`AddressableDependencyWindowTests` | ✅ | メニューから開き、Scan → 一覧・絞り込み・移動・出力ができる。`BuildVisibleRows` のテストが通る | Scan は `delayCall` で実行する |
| 7 | ウィンドウ：列の区切り線と列幅のドラッグ変更・横スクロール | ✅ | 見出しと各行に区切り線が出る。境界のドラッグで列幅が変わる（最小 30px）。幅の合計が表示幅を超えると横スクロールになり、見出しも動く | ユーザーの要望で追加。パスの列は固定幅に変更した |
| 8 | ドキュメント更新：`diagrams/editor-dependency-check-class.md`（新規）・`diagrams/index.md`・`diagrams/editor-foundation-class.md`・`specs/editor-dependency-check-spec.md`・`specs/editor-debug-window-spec.md` | ✅ | 図のリンク先が存在する。仕様書・図・コードの型名が一致している | `CHANGELOG.md` は更新しない |

## 完了の定義（Definition of Done）

- [x] requirements.md の受け入れ条件をすべて満たす
- [x] Runtime アセンブリの型・窓口に触れていない。グループ・エントリ・アセットを書き換える API を呼んでいない
- [x] 既存テストと追加テストがすべて通る
- [x] 影響のある永続的ドキュメント（Docs/）の更新が完了している
- [x] コードレビュー・手動検証・ドキュメントの最終確認が完了している（ユーザーが実施済み）
- [x] `CHANGELOG.md` は更新していない（担当者の運用）
