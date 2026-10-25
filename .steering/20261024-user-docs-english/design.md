# 設計：利用者向けドキュメントの英語化

> 対象：`requirements.md`（承認済み版） ／ 作成日：2026-10-24 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.25f1

> 本 steering の範囲はドキュメントと `package.json` の説明文だけ。コードは変更しない。

## 1. 実装アプローチ

- **翻訳のみ・構成は維持**：章立て・表・コード例・リンク先は英語化前と同じにし、記述内容（仕様・既定値・手順）を変えない。翻訳の過程で内容の誤りに気づいた場合は、直さずに報告で指摘する
- **訳語は用語集の「英語」列に従う**。先に用語集の英語列を確認し、usage.md・README の訳語をそろえる（下記 1.1）
- **見出しを先に決め、アンカーを確定させてからリンクを書く**。usage.md を先に英訳し、README・README_JP・release-notes はその見出しのアンカーにリンクする（下記 1.2）
- **Editor ツールの UI 文言は画面の表示（日本語）のまま書き、括弧で英訳を添える**（例：`**ログ出力** (Export Log)`）。Editor ツールの UI は日本語で表示されるため、英訳だけを書くと画面と一致しない。usage.md 6章の冒頭にその旨の注記を置く（2026-10-24 作業中に追加）
- **README_JP.md は英語化前の README の複製**とし、変更はリンク（言語切り替え・usage.md のアンカー）と最終更新日だけにする
- **用語集は日本語のまま**：利用者は自動翻訳で読む前提とし、冒頭に英語の読者向けの案内を1行だけ置く。英語の用語は表の「英語」列、コード上の名前は「コード上の命名」列で示されるため、自動翻訳でも用語の対応は失われない（2026-10-24 作業中に変更。当初は全行の説明欄に `日本語<br>English` で英訳を併記したが、同じ内容が2回並んで読みにくいため取りやめた）
- 却下した案：用語集を `glossary_JP.md` と英語版に分ける（語彙の正が2つになり乖離するため）／用語集の全行に英訳を併記する（同じ内容が2回並んで読みにくく、英訳の保守も倍になるため。一度実施して取りやめた）／README を1ファイルで日英併記にする（トップページが約2倍の長さになるため）／英語化前の日本語ファイルを `usage_JP.md` 等として全部残す（今回の要求は README のみ）

### 1.1 主な訳語

| 日本語 | 英語 | 備考 |
|---|---|---|
| 読み込み／ロード | load | |
| 解放 | release | 利用者が `Release` を呼ぶこと |
| 回収・自動回収 | reclaim / automatic reclaiming | 寿命の終了で Manager が解放すること。release と混同しない（用語集の Reclaim と、コードの `OwnerReclaimRoute` に合わせた。2026-10-24 作業中に collect から変更） |
| 破棄 | destroy | GameObject の `Destroy` |
| 寿命 | lifetime | `AssetLifetime` |
| チケット／まとめチケット | ticket / group ticket | |
| 参照カウント | reference count | |
| 解放漏れ／リーク | leak | |
| 設定アセット | settings asset | |
| データ更新 | content update | |
| 未登録の依存 | unregistered dependency | |
| 確保元 | allocation source | |

用語集の「英語」列と食い違う場合は用語集を正とする。

### 1.2 英語化後の見出しとアンカー

GitHub のアンカー規則（小文字化・英数字とハイフン以外を除去・空白を `-`）で生成される値。リンクを書き換える箇所のみ記載する。

| ファイル | 旧アンカー | 新しい見出し | 新アンカー |
|---|---|---|---|
| `README.md` | `#導入` | `## Installation` | `#installation` |
| `README.md` | `#editor-ツール` | `## Editor Tools` | `#editor-tools` |
| `usage.md` | `#1-設定アセット` | `## 1. Settings Asset` | `#1-settings-asset` |
| `usage.md` | `#23-初期化前の-load-の扱い` | `### 2.3 Loading Before Initialization` | `#23-loading-before-initialization` |
| `usage.md` | `#42-キーassetkey` | ``### 4.2 Keys (`AssetKey`)`` | `#42-keys-assetkey` |
| `usage.md` | `#5-属性プルダウン選択` | `## 5. Attributes (Dropdown Selection)` | `#5-attributes-dropdown-selection` |
| `usage.md` | `#6-editor-ツール` | `## 6. Editor Tools` | `#6-editor-tools` |
| `usage.md` | `#61-デバッグウィンドウ` | `### 6.1 Debug Window` | `#61-debug-window` |
| `usage.md` | `#62-dependency-check` | `### 6.2 Dependency Check` | `#62-dependency-check` |
| `usage.md` | `#63-editor-設定` | `### 6.3 Editor Settings` | `#63-editor-settings` |
| `usage.md` | `#8-サンプル` | `## 8. Samples` | `#8-samples` |
| `usage.md` | `#82-動かす前の準備` | `### 8.2 Setup Before Running` | `#82-setup-before-running` |
| `usage.md` | `#85-download` | `### 8.5 Download` | `#85-download` |

`README_JP.md` 内の自ファイルへのアンカー（`#editor-ツール` 等）は日本語の見出しのままなので変更しない。

## 2. 変更するコンポーネント

パスはリポジトリルートを起点とする。

| 対象 | 変更内容 | 新規/修正 |
|---|---|---|
| `CHANGELOG.md` | `[Unreleased]` の各エントリ末尾の `（<7桁ハッシュ>）` を `（直接コミット）` に置換。本文・書式説明は変更なし | 修正 |
| `Docs/usage.md` | 全文英訳。冒頭の最終更新日を `Last updated: 2026-10-24 / Unity: 6000.3.25f1` に。内部アンカー・README へのリンクを 1.2 に従い更新 | 修正 |
| `README_JP.md` | 英語化前の `README.md` を複製。冒頭に `[English](README.md) \| 日本語`。usage.md へのリンクを新アンカーにし、リンク文言に「（英語）」を付ける。最終更新日 | 新規 |
| `README.md` | 全文英訳。冒頭に `English \| [日本語](README_JP.md)`。開発者向けドキュメント（設計仕様書・conventions・specs・CONTRIBUTING）へのリンクに「(Japanese)」。最終更新日 | 修正 |
| `Docs/release-notes.md` | 前文を英訳。README へのリンクを `#installation` に | 修正 |
| `Docs/release-notes-template.md` | テンプレート本体の見出しを `BREAKING CHANGES` / `New Features` / `Improvements` / `Bug Fixes` / `Compatibility` / `Installation` に、コメントの記述例を英語に。前文に「リリースノートは英語で書く」を追加。「です・ます調」の記述を英語の書き方（平叙文・1項目1行）に修正。最終更新日 | 修正 |
| `MAH-AddressableManager/README.md` | 概要文と案内文を英訳 | 修正 |
| `src/AddressableManager/Assets/@MAH/MAH-AddressableManager/package.json`／`MAH-AddressableManager/package.json` | `description` と `samples[].description` を英訳。2ファイルを同一内容に保つ | 修正 |
| `Docs/glossary.md` | 冒頭に英語の読者向けの案内を1行追加（「英語」列・「公開」列の読み方）。最終更新日 | 修正 |
| `CLAUDE.md` | 置き場所ルールに言語ルール（英語で書くファイル／日本語で書くファイル／`README_JP.md` は日英を同時に更新／用語集は日本語）を追加。置き場所の表に `README_JP.md` の行を追加。最終更新日。200行以内を維持 | 修正 |

## 3. データ構造の変更

`package.json` の `description`・`samples[].description` の文字列のみ変更。キー・バージョン・`samples[].path` は変更しないため、利用者のプロジェクトへの影響（再インポート・サンプルのパス変更）はない。マイグレーション不要。

## 4. 影響範囲の分析

- 既存機能への影響：なし（コード変更なし）
- 永続的ドキュメントへの影響：
  - `src/AddressableManager/Docs/addressable-manager-design.md` 冒頭は `README.md` と `Docs/usage.md` をアンカーなしで参照しているだけのため変更不要
  - `CONTRIBUTING.md` は `release-notes-template.md`・`release-notes.md`・`glossary.md` をアンカーなしで参照しているだけのため変更不要。CHANGELOG 運用（3章「次のコミットでハッシュを添えて追記する」）は新リポジトリでも有効なため変更しない
  - `.steering/` 配下の旧アンカーへのリンクは作業履歴のため修正しない（スコープ外）
- テストへの影響：なし

## 5. 図表

なし。
