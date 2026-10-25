# タスクリスト：Dependency Check ウィンドウのレビュー指摘修正

> 対象：`design.md`（承認済み版） ／ 最終更新：2026-10-15 ／ ステータス：完了（実装・テスト・手動検証・ドキュメントの最終確認まで、ユーザーが確認済み）

## タスク

状態：⚪未着手 → 🔵作業中 → ✅完了（🔴ブロック中はブロック理由を備考へ）

本 steering は、実装を先に行ったため、実施した内容の履歴として作成した（実装計画書のステップ0を飛ばした）。コミットはユーザーの指示があるまで行わない。

**Claude の担当は実装（コードとテストコード、ドキュメントの作成・更新）まで**。テストの実行（Test Runner は MCP から起動できない）・コードレビュー・手動検証・ドキュメントの最終確認・CHANGELOG の更新は担当外。

| No | タスク | 状態 | 完了条件 | 備考 |
|---|---|---|---|---|
| 1 | 言葉の統一：`AccIsDuplicate` → `AccIsMultiGroup`、`[複数グループ]`・件数行・状態表示・注記の書き換え、`DependencyCheckRow` のコメント、テストの改名と期待値 | ✅ | コンパイルが通る。「重複」が画面・出力・コードの名前に残らない（`Pack Separately` のビルド時の参考の説明を除く） | 初回は `PackageInfo` が `UnityEditor.PackageInfo` と曖昧になりコンパイルエラーになった（ユーザーの指摘で、名前空間付きの呼び出しに修正）。以後、コンパイルは通っている |
| 2 | 経由の参照元：`DependencyCheckInput` の直接の依存の表、Scanner の幅優先の走査、Aggregator の直接／経由の判定、`DependencyCheckReferrer` の経由情報、`FormatVia`、ウィンドウの表示、テストの追加 | ✅ | コンパイルが通る。Aggregator・Formatter のテストに、経由のケースが揃っている | テストはユーザーが Test Runner で実行し、成功を確認済み |
| 3 | Scanner のサイズ取得：`Packages/` の実体の場所の解決（`PackageInfo`）・例外の受け方 | ✅ | コンパイルが通る | 計画書の「`Path.GetFullPath` で解決」は誤りで、`PackageInfo` に変更した。手動検証で `Packages/` のサイズを確認する |
| 4 | OnGUI の負荷：件数・高さ・状態の文字列の保持（`RefreshVisibleSummary`）、展開時の差分更新、日時の `InvariantCulture` | ✅ | コンパイルが通る。`DrawStatus`・`DrawList` に表示行全体のループが残っていない | 展開・折りたたみの描画は手動検証で確認する |
| 5 | ドキュメント更新：`specs/editor-dependency-check-spec.md`・`addressable-manager-design.md`（6章に追記）・`diagrams/editor-dependency-check-class.md`・`Docs/glossary.md`・`README.md` | ✅ | 文書から Dependency Check の文脈での「重複」「重複同梱」が消えている（ビルド時の参考の説明を除く）。コードの名前と文書が一致している | `CHANGELOG.md` は更新しない。設計仕様書 6章の過去の項目は書き換えていない |
| 6 | steering の作成（requirements・design・tasklist） | ✅ | 3ファイルが存在する | 実装後に作成した（承認の手順は省略）。メモリに「steering は実装前に作る」を記録した |

## 完了の定義（Definition of Done）

- [x] requirements.md の受け入れ条件をすべて満たす
- [x] Runtime の窓口・`AddressableManager` に触れていない。グループ・エントリ・アセットを書き換える API を呼んでいない
- [x] 既存テストと追加テストがすべて通る（ユーザーが Test Runner で実行し、成功を確認済み）
- [x] 影響のある永続的ドキュメント（Docs/）の更新が完了している
- [x] コードレビュー・手動検証・ドキュメントの最終確認が完了している
- [x] `CHANGELOG.md` は更新していない（担当者の運用）
