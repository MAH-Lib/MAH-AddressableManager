# 設計：v1.0.0 配布に向けたドキュメント更新

> 対象：`requirements.md`（承認済み版） ／ 作成日：2026-10-15 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.25f1

> 本 steering の範囲はドキュメントの更新だけ。コード・`package.json` は変更しない。

## 1. 実装アプローチ

- **原本は1か所に保つ**（`CLAUDE.md` の置き場所ルール）。README には導入・最短の使い方・サンプル/Editor ツールの**一覧と1〜2行の説明**だけを書き、手順・設定項目・操作の詳細は `Docs/usage.md` に書いてリンクする。パッケージ内 README は概要と git URL のみ
- **事実はコードと `package.json` から起こす**。メニュー名は `*Window.cs` の `MENU_PATH`、設定項目は `Editor/SettingsSync/*.cs` の Tooltip、サンプル名は `package.json` の `samples[].displayName`、シーン・スクリプトは `AddressableManager/Samples~/` の実ファイルを正とする。Editor ツールの説明は `editor-debug-window-spec.md`・`editor-dependency-check-spec.md` の利用者に見える動作の部分から要約する（内部構成は書かない）
- **開発場所と配布物の関係**（開発は Assets 側、リリース時にルートへ複製）は、手順を `CONTRIBUTING.md` 4章、理由を設計仕様書 6章、罠を `CLAUDE.md` に書き分ける
- **リリースノートは `Docs/release-notes.md` 1ファイルに新しい順に積む**。テンプレート（`release-notes-template.md`）は書式の原本として残し、追記先を明記する
- **動作環境の表記**は、検証済みの環境（Unity 6000.3.25f1・Addressables 2.10.3）を書く。`package.json` の値（`unity: 6000.0`・`>=2.9.0`）との差はドキュメント側で取り繕わず、報告で指摘する
- 却下した案：README にサンプル・Editor ツールの詳細まで書く（`usage.md` と二重管理になるため）／リリースノートをバージョンごとの別ファイルにする（ユーザーの運用が1ファイル追記方式のため）

## 2. 変更するコンポーネント

パスはリポジトリルートを起点とする。

| 対象 | 変更内容 | 新規/修正 |
|---|---|---|
| `README.md` | 「開発中」注記の削除。「導入」を3小節（Package Manager から／`manifest.json` に直接書く／バージョンを指定する）に。UniTask の導入順の注意。「サンプル」節（3サンプルの表＋Import 方法）と「Editor ツール」節（Debug Window・Dependency Check・属性・EditorSettings）を新設し `usage.md` へリンク。開発者向け節に開発場所と配布物の関係。最終更新日 | 修正 |
| `Docs/usage.md` | 冒頭に導入（README）への導線。**新章「Editor ツール」**（Debug Window の3タブとリークのログ出力、Dependency Check、EditorSettings の項目と自動作成場所）。**「サンプル」章の詳細化**（Import 手順、サンプルごとのシーン・操作・確認できること・参考になるスクリプト、Addressables への登録など動かす前の準備）。API 記述を `IAddressableHandler.cs` と突き合わせ。章番号の振り直し。最終更新日 | 修正 |
| `Docs/release-notes.md` | v1.0.0 の節（初回リリースの主な機能・対応バージョン・導入 URL） | 新規 |
| `Docs/release-notes-template.md` | 導入 URL を `?path=AddressableManager#vX.Y.Z` に。追記先 `release-notes.md`（先頭に新しい版を積む）を明記。最終更新日 | 修正 |
| `Docs/glossary.md` | UPM 行の配布パス、「Unity パッケージ構成」に配布物では `Samples~/` になる旨。必要なら「配布フォルダ」の用語を追加。最終更新日 | 修正 |
| `AddressableManager/README.md`（パッケージ内） | モジュール概要と git URL のみ（使い方はルート README へのリンク） | 新規 |
| `CONTRIBUTING.md` | 「作業を始める前に」に開発場所／配布物の関係。4章に「ルート `AddressableManager/` へ複製（`Samples/` → `Samples~/`、`package.json` の `version` 更新）」をタグ付けの前に追加。手順6を `release-notes.md` への追記に。「`package.json` を作成した後は」を現在形に。5章の表に `release-notes.md`。最終更新日 | 修正 |
| `src/AddressableManager/Docs/addressable-manager-design.md` | 冒頭の利用者向け参照先をルート `README.md`／`Docs/usage.md` に。4章のツリーの前に「配布物はルート `AddressableManager/`（`Samples` は `Samples~`）、ツリーは開発側」の注記。6章に決定事項（2026-10-15）。最終更新日 | 修正 |
| `CLAUDE.md` | ツリーにルート `AddressableManager/`（配布物）を追加。置き場所ルール表のパッケージ README の行をルート `AddressableManager/README.md` に、ルート `Docs/` の説明にリリースノートを追加、`?path=` が指すフォルダを明記。罠節の「asmdef が1つも無い」を削除し、「開発は Assets 側・配布はルートへ複製」の罠を追加。Build Settings の行は触らない。最終更新日 | 修正 |
| `CHANGELOG.md` | `[Unreleased]` に未記載の実装履歴（下記）。`## [1.0.0] - 2026-10-15` 節を新設（パッケージ作成・ドキュメント更新）。目次 `<details>` にリンクを追加 | 修正 |

### 2.1 CHANGELOG に書く内容

- `[Unreleased]`
  - Added：リーク検出一覧（「リーク」タブ・ログ出力）（e655606）／owner Watcher の可視化（「Owner」タブ）（ac6f85c）／未登録の依存の確認ウィンドウ `AddressableDependencyWindow`（c8b038c）
  - Changed：読み込み計測をやめ Editor 機能4を Dependency Check に差し替えたドキュメント更新（970a58f）
  - Fixed：Editor 拡張のレビュー指摘の修正（8c64c2f／efb43a1／96778f0）
  - 載せない：steering の修正（4b1dcba）、CHANGELOG 自体の更新（65a969d）
- `[1.0.0] - 2026-10-15`
  - Added：UPM 配布用パッケージ（ルート `AddressableManager/`、`package.json`・`Samples~`）（5a9a250）／パッケージ内 README・リリースノート `Docs/release-notes.md`（ハッシュは次のコミットで添える）
  - Changed：README の導入手順（3通り）・サンプル/Editor ツールの説明、`usage.md` の Editor ツール章・サンプル章、リリース手順（ハッシュは次のコミットで添える）

### 2.2 設計仕様書 6章に追記する決定事項（案）

- （2026-10-15）**配布用パッケージをリポジトリルート `AddressableManager/` に置き、開発は `src/AddressableManager/Assets/@MAH/AddressableManager/` で続ける**。リリース時に開発側から複製する（`Samples/` は `Samples~/` にして、利用者が Package Manager から選んで Import する形にする）。git URL の `?path=` を短くし、配布物に `package.json`・`Samples~` を置くため。**失うもの**：同じコードが2か所にあり、複製を忘れると配布物が古いままになる（複製の手順は `CONTRIBUTING.md` 4章）

## 3. データ構造の変更

変更なし（ドキュメントのみ）。

## 4. 影響範囲の分析

- 既存機能への影響：なし（コード・`package.json` を変更しない）
- 永続的ドキュメントへの影響：2章の表のとおり。機能仕様書（`specs/`）・図は、`Samples/...` のパスが開発側の記述として正しいため変更しない
- テストへの影響：なし
- 確認が必要な点：Import したサンプルを動かすのに Addressables への登録（グループ・アドレス・Label）が要るか。`Samples~` にアドレス設定が含まれない場合は、`usage.md` のサンプル章に準備手順として書く

## 5. 検証方法

- `grep -rn "@MAH/AddressableManager#\|準備中\|未リリース\|Assembly-CSharp" README.md Docs CONTRIBUTING.md CLAUDE.md src/AddressableManager/Docs` で古い記述が残っていないこと（設計仕様書 6章の経緯を除く）
- `usage.md` のメニュー名・設定項目・API を、`MENU_PATH`・Tooltip・`IAddressableHandler.cs` と突き合わせる
- README ↔ `Docs/usage.md` ↔ `Docs/glossary.md` ↔ `Docs/release-notes.md` の相対リンクとアンカーが切れていないこと
- `CLAUDE.md` が200行以内で、Build Settings の行が変わっていないこと
- `git status` で、コード・`package.json` に変更がないこと
- ドキュメントの最終確認はユーザーが行う
