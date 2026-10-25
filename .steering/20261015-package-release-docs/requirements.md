# 要求内容：v1.0.0 配布に向けたドキュメント更新

> 作業ディレクトリ：`.steering/20261015-package-release-docs/` ／ 作成日：2026-10-15 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.25f1
> 関連する永続的ドキュメント：`README.md`（導入・サンプル・Editor ツール）／`Docs/usage.md`（Editor ツール章の新設・サンプル章）／`Docs/glossary.md`／`Docs/release-notes-template.md`／`Docs/release-notes.md`（新規）／`AddressableManager/README.md`（新規。パッケージ内）／`CONTRIBUTING.md`（リリース手順）／`src/AddressableManager/Docs/addressable-manager-design.md`（冒頭・4章・6章）／`CLAUDE.md`／`CHANGELOG.md`

> **本 steering の範囲はドキュメントの更新だけ**。コード・`package.json` は変更しない。

## 1. 変更・追加する機能の説明

必要機能の実装が完了し、配布用パッケージをリポジトリルート `AddressableManager/`（`package.json`・`Samples~/` 付き）に作成した（5a9a250）。ドキュメントには「導入は準備中」「開発中・v1.0.0 未リリース」「配布パスは `src/AddressableManager/Assets/@MAH/AddressableManager`」「asmdef が無い」などの古い記述が残り、利用者向けの使い方には Editor ツールの説明が無い。これを **v1.0.0 リリース前提**の内容に揃え、あわせて CHANGELOG の更新とリリースノートの作成を行う。

- **開発場所と配布物の関係**：開発は従来どおり `src/AddressableManager/Assets/@MAH/AddressableManager/` で行い、リリース時にルート `AddressableManager/` へ複製する（`Samples/` → `Samples~/`）。配布の git URL は `https://github.com/MAH-Lib/AddressableManager.git?path=AddressableManager#v1.0.0`
- **README の導入手順**：Package Manager から／`manifest.json` に直接書く／バージョン（タグ）を指定する、の3通りを載せる
- **README にサンプルと Editor ツールの簡易説明**を載せ、詳細は `Docs/usage.md` に書く
- **リリースノートは1ファイル（`Docs/release-notes.md`）に追記していく方式**とし、v1.0.0 の節を作る

## 2. ユーザーストーリー

- 導入する利用者として、Package Manager・`manifest.json` のどちらからでも、バージョンを固定して導入できる手順を README から知りたい
- 導入する利用者として、どんなサンプルと Editor ツールがあるかを README で一覧し、使い方の詳細を `Docs/usage.md` で読みたい
- リリース担当者として、開発フォルダから配布フォルダへ複製する手順と、リリースノートの追記先を CONTRIBUTING から知りたい
- 後から読む開発者として、配布物をルートに置き、開発場所と分けた理由を設計仕様書から知りたい

## 3. 受け入れ条件

- [ ] `README.md` から「開発中」注記と「導入は準備中」が消え、導入が「Package Manager から（バージョン指定なし）」「manifest.json に直接書く（バージョン指定 `#v1.0.0`）」の2小節で書かれ、UniTask の URL を載せていない。URL はすべて `?path=AddressableManager` を使う（2026-10-15 ユーザーの指示で3小節から変更）
- [ ] `README.md` に「サンプル」節（3サンプルの簡易説明と Samples タブからの Import 方法）と「Editor ツール」節（Debug Window・Dependency Check・属性・EditorSettings をメニュー名つきで）があり、それぞれ `Docs/usage.md` の該当章へリンクしている
- [ ] `Docs/usage.md` に「Editor ツール」章があり、メニュー名・設定項目が `*Window.cs` の `MENU_PATH`・`Editor/SettingsSync/*.cs` の Tooltip と一致している。「サンプル」章に Import 手順と各サンプルのシーン・操作・確認できることが書かれている
- [ ] `Docs/usage.md` の API 記述が `IAddressableHandler.cs` と一致している
- [ ] `AddressableManager/README.md`（パッケージ内）が作成され、モジュール概要と git URL **のみ**が書かれている
- [ ] `Docs/release-notes-template.md` の導入 URL が `?path=AddressableManager#vX.Y.Z` になり、追記先 `Docs/release-notes.md` が示されている
- [ ] `Docs/release-notes.md` に v1.0.0 の節があり、内部リファクタ・レビュー修正を含まない
- [ ] `CONTRIBUTING.md` のリリース手順に「ルート `AddressableManager/` への複製」と「`Docs/release-notes.md` への追記」がある
- [ ] 設計仕様書の冒頭の利用者向け参照先が直り、4章に配布物の置き場所の注記、6章に決定事項（2026-10-15）が既存書式で追記されている
- [ ] `CLAUDE.md` のツリー・置き場所ルール表・罠節が現状に合い（asmdef が無い旨の削除を含む）、Build Settings の記述は変わっておらず、200行以内である
- [ ] `CHANGELOG.md` の `[Unreleased]` に 10-14〜10-15 の未記載の実装履歴があり、`[1.0.0] - 2026-10-15` 節（パッケージ作成・ドキュメント更新）が作られ、目次にリンクがある
- [ ] `grep -rn "@MAH/AddressableManager#\|準備中\|未リリース\|Assembly-CSharp"` で利用者向け・開発者向けドキュメントに古い記述が残っていない（`.steering/` と設計仕様書 6章の経緯を除く）

## 4. 制約事項

- コード・`package.json` は変更しない（`package.json` の問題点は報告で指摘するだけ）
- `CLAUDE.md` のビルドシーンリスト（Build Settings）の記述には触れない
- 利用者向けドキュメントを `src/AddressableManager/Docs/` に複製しない。パッケージ内 README に使い方を書かない（`CLAUDE.md` の置き場所ルール）
- `CHANGELOG.md` は冒頭の書式（`- [YYYY-MM-DD] 内容（ハッシュ）`）に従う。今回のドキュメント更新のハッシュは、コミット後の次のコミットで添える（CONTRIBUTING 3章）

## 5. スコープ外（今回やらないこと）

- `package.json` の修正（UniTask を必須の依存にしている点・範囲指定のバージョン・`unity` の値）
- 開発フォルダからルートへの複製の自動化（ツール・スクリプト）
- タグ `v1.0.0` の作成・`main` へのマージ・GitHub Releases への投稿
- 機能仕様書（`src/AddressableManager/Docs/specs/`）・図の内容の変更（パスの確認のみ）
- `.meta` の整理（ルート `AddressableManager/Samples.meta` 等。報告で指摘のみ）
