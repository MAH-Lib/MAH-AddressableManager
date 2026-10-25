# 設計：AddressableManager Editor 機能4の差し替え（ドキュメント）

> 対象：`requirements.md`（承認済み版） ／ 作成日：2026-10-15 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.25f1

> 本 steering の範囲はドキュメントの更新だけ。Dependency Check ウィンドウの実装の設計は `src/AddressableManager/Docs/specs/editor-dependency-check-spec.md` を正とし、実装時に新しい steering で扱う。

## 1. 実装アプローチ

- Dependency Check の仕様は、新しい機能仕様書 `editor-dependency-check-spec.md` に書く。`editor-debug-window-spec.md`（Play 中の状態を窓口から pull する共通基盤）とは仕組みが違うため、別の spec にする
- 設計仕様書には概要と決定事項だけを書き、詳細は spec への参照にする
- 計測の記述は削除する。経緯は、設計仕様書 6章の決定事項（過去の決定に取り消しの印）と 5.3 の不採用理由にだけ残す。用語集の「読み込み計測（不採用）」の項は、型・シンボル名を出さずに書く

## 2. 変更するコンポーネント

パスは `src/AddressableManager/Docs/` を起点とする（リポジトリルートのものは明記）。

| 対象 | 変更内容 | 新規/修正 |
|---|---|---|
| `specs/editor-dependency-check-spec.md` | 機能仕様書（概要・動作・設定項目・内部構成・設計の判断と理由・改修ガイド・注意点） | 新規 |
| `addressable-manager-design.md` | 2章の構成図、3.1.1 の Editor SO の中身、3.9 の共通方針・表・(4) の差し替え・(6)、3.9.1 の削除、4章のフォルダ構成と namespace の例、5.3、5.4、6章（決定事項の追記・過去の決定の取り消し）、7章 | 修正 |
| `specs/editor-debug-window-spec.md` | 冒頭・1章・2.4・3.1・3.2・8章から計測の記述を削除し、Dependency Check の spec への参照を足す | 修正 |
| `conventions.md` | `Profiling` の記述の削除、7.2・7.3・10章に Dependency Check の型と命名パターン（`<対象>Scanner`・`<対象>Aggregator`）を追加 | 修正 |
| `Docs/glossary.md`（ルート） | 計測・シンボル・整合チェック・ビルドターゲット・CI/CD の用語を削除し、4.2 を「未登録の依存の確認」の用語に差し替え | 修正 |
| `README.md`（ルート） | 「読み込みの計測機能を備える（予定）」を差し替え | 修正 |
| `CLAUDE.md`（ルート） | 機能の縦割りから `Profiling/` を削除、「計測機能は `#if ADDRESSABLE_PROFILING` で囲む」を削除、最終更新日を更新 | 修正 |

## 3. データ構造の変更

変更なし（ドキュメントのみ）。

## 4. 影響範囲の分析

- 既存機能への影響：なし（コードを変更しない）
- 永続的ドキュメントへの影響：2章の表のとおり
- テストへの影響：なし

## 5. 検証方法

- `grep` で、設計仕様書・conventions・用語集・`editor-debug-window-spec.md`・`CLAUDE.md` に計測の型・シンボル名が残っていないこと（設計仕様書 6章を除く）を確認する
- `git status` で、`Assets/` 配下に変更がないことを確認する
- ドキュメントの最終確認はユーザーが行う
