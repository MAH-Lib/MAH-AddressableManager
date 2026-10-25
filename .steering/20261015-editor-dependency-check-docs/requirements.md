# 要求内容：AddressableManager Editor 機能4の差し替え（ドキュメント）

> 作業ディレクトリ：`.steering/20261015-editor-dependency-check-docs/` ／ 作成日：2026-10-15 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.25f1
> 関連する永続的ドキュメント：`src/AddressableManager/Docs/addressable-manager-design.md`（3.1.1・3.9・3.9.1 削除・4章・5.3・5.4・6章・7章）／`src/AddressableManager/Docs/specs/editor-dependency-check-spec.md`（新規）／`src/AddressableManager/Docs/specs/editor-debug-window-spec.md`／`src/AddressableManager/Docs/conventions.md`／`Docs/glossary.md`／`README.md`／`CLAUDE.md`

> **本 steering の範囲はドキュメントの更新だけ**。Dependency Check ウィンドウの実装は、実装時に新しい steering を作って行う（実装の設計は `editor-dependency-check-spec.md` が正）。

## 1. 変更・追加する機能の説明

Editor 機能4を、「読み込み計測」から「未登録の依存の確認（Dependency Check）」に差し替える決定を、永続的ドキュメントに反映する。

- **読み込み計測は作らない**：読み込み時間は端末・通信環境（場所）に大きく左右され、エディタやローカルでの計測値に意味が薄いため。計測用の型・オーバーレイ・デファインシンボルも作らない（コードは未作成のため、ドキュメントの削除のみ）
- **代わりに Dependency Check ウィンドウを作る**：Addressables に登録したアセットが参照している未登録のアセット（暗黙的に同梱され、複数グループから参照されると重複同梱される）を、Play もビルドもせずに一覧で確認する Editor ウィンドウ。仕様は新規の機能仕様書に書く

## 2. ユーザーストーリー

- 後から読む開発者として、読み込み計測をなぜ作らなかったのか、代わりに何を作るのかを、設計仕様書から知りたい
- 実装担当者として、Dependency Check ウィンドウを実装するときに、仕様（判定・一覧・出力・内部構成）を1つの機能仕様書から読みたい
- 実装担当者として、存在しない計測機能の記述（型・シンボル・フォルダ）に惑わされないよう、ドキュメントから消えていてほしい

## 3. 受け入れ条件

- [ ] `editor-dependency-check-spec.md` に、走査・登録済みの判定・一覧・絞り込み・移動・テキスト出力・設定・内部構成・設計の判断と理由・改修ガイド・注意点が書かれている（実装手順・フェーズは書かない）
- [ ] 設計仕様書 3.9 の実装項目表と (4) が Dependency Check に差し替わり、(6) にこのウィンドウのタブとする旨がある。3.9.1 が削除されている
- [ ] 設計仕様書 4章のフォルダ構成から `Profiling/` 一式が消え、`AddressableDependencyWindow` と設定クラスがある
- [ ] 設計仕様書 5.3 に、読み込み計測・Analyze 任せの運用・登録操作の不採用理由がある
- [ ] 設計仕様書 6章に決定事項（2026-10-15）が書式どおりに追記され、計測に関する過去の決定に取り消しの印が付いている。7章から計測レベルの未決事項が削除されている
- [ ] 設計仕様書・conventions・用語集・`editor-debug-window-spec.md`・`CLAUDE.md` に、計測の型・シンボル名（`ADDRESSABLE_PROFILING` 等）が残っていない（設計仕様書 6章の経緯を除く）
- [ ] conventions・用語集に、Dependency Check の型・用語が追加されている
- [ ] `README.md` の「読み込みの計測機能を備える（予定）」が差し替わっている
- [ ] `CLAUDE.md` は計測の記述（`Profiling/`・`#if ADDRESSABLE_PROFILING`）と最終更新日だけが変わっている

## 4. 制約事項

- コード（`Assets/` 配下）は変更しない
- `CLAUDE.md` は計測の記述の削除だけを行う（ユーザーの了承済み）。ビルドシーンリストの記述など、他の箇所には触れない
- `CHANGELOG.md` は更新しない（担当者の運用）

## 5. スコープ外（今回やらないこと）

- Dependency Check ウィンドウの実装・テスト・図（実装時に新しい steering で行う）
- 機能5（EditMode でのサイズ概算）
