# タスクリスト：AddressableManager Editor 機能4の差し替え（ドキュメント）

> 対象：`design.md`（承認済み版） ／ 最終更新：2026-10-15 ／ ステータス：承認済み（全タスク完了。ドキュメントの最終確認はユーザー）

## タスク

状態：⚪未着手 → 🔵作業中 → ✅完了（🔴ブロック中はブロック理由を備考へ）

本 steering の範囲はドキュメントの更新だけ。Dependency Check ウィンドウの実装は、実装時に新しい steering を作って行う。コミットはユーザーの指示があるまで行わない。

| No | タスク | 状態 | 完了条件 | 備考 |
|---|---|---|---|---|
| 1 | ドキュメントの更新：`specs/editor-dependency-check-spec.md`（新規）、設計仕様書、`specs/editor-debug-window-spec.md`、`conventions.md`、`Docs/glossary.md`、`README.md`、`CLAUDE.md` | ✅ | requirements.md の受け入れ条件を満たす | `grep` で、計測の型・シンボル名が残るのは設計仕様書 6章（取り消しの印を付けた過去の決定2件と今回の決定）だけであることを確認した。`editor-dependency-check-spec.md` から図（`diagrams/editor-dependency-check-class.md`）へのリンクは、実装時に図を作るまで切れている |

## 完了の定義（Definition of Done）

- [x] requirements.md の受け入れ条件をすべて満たす
- [x] 計測の型・シンボル名が、設計仕様書 6章以外に残っていない（`grep` で確認）
- [x] コード（`Assets/` 配下）を変更していない（`git status` で確認）
- [x] ドキュメントの最終確認が完了している（Claude の担当外。ユーザーが実施）
- [x] `CHANGELOG.md` は更新していない（担当者の運用）
