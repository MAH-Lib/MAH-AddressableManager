# 要求内容：利用者向けドキュメントの英語化

> 作業ディレクトリ：`.steering/20261024-user-docs-english/` ／ 作成日：2026-10-24 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.25f1
> 関連する永続的ドキュメント：`README.md`（英語化）／`README_JP.md`（新規）／`Docs/usage.md`（英語化）／`Docs/release-notes.md`（英語化）／`Docs/release-notes-template.md`（テンプレート本体のみ英語化）／`Docs/glossary.md`（日本語のみ。英語の読者向けの案内を冒頭に1行追加）／`MAH-AddressableManager/README.md`（英語化）／`package.json` ×2（説明文の英語化）／`CHANGELOG.md`（コミット参照の置き換え）／`CLAUDE.md`（言語ルールの追記）

> **本 steering の範囲はドキュメントと `package.json` の説明文だけ**。コードは変更しない。

## 1. 変更・追加する機能の説明

GitHub で公開する際、利用者が読むファイルを英語基準にする。開発者向けドキュメント（設計仕様書・conventions・機能仕様書・CONTRIBUTING・CLAUDE.md・CHANGELOG）は日本語のまま残す。

- **英語化**：ルート `README.md`・`Docs/usage.md`（API の使い方）・`Docs/release-notes.md`・配布フォルダの `README.md`・`package.json` の説明文
- **日本語版 README**：現在の日本語 README を `README_JP.md` として残し、英語版と相互にリンクする
- **用語集**：日本語のまま残し、英語の読者向けの案内を冒頭に1行だけ英語で置く（利用者は自動翻訳で読む前提。英語の用語は表の「英語」列で示す）。言語別のファイルには分けない（2026-10-24 作業中に変更。当初は全行の説明に英訳を併記したが、同じ内容が2回並んで読みにくいため取りやめた）
- **CHANGELOG**：新しいリポジトリへ移すため旧コミットハッシュが無効になる。既存エントリのハッシュを「直接コミット」に置き換える（本文は日本語のまま）

## 2. ユーザーストーリー

- 海外の利用者として、導入から API の使い方までを英語で読みたい
- 日本語の利用者として、README を日本語でも読みたい
- 英語の利用者として、用語集で各用語の正式な英語表記を知りたい（usage.md から用語集にリンクされるため。説明は自動翻訳で読む）
- 開発者として、新リポジトリで無効になるコミットハッシュを CHANGELOG から取り除きたい
- 後から作業する開発者（Claude を含む）として、どのファイルを英語で書き、どのファイルを日本語で書くかを CLAUDE.md で知りたい

## 3. 受け入れ条件

- [ ] `README.md`・`Docs/usage.md`・`Docs/release-notes.md`・`MAH-AddressableManager/README.md` が英語で書かれ、内容・構成は英語化前と同じである（翻訳のみ。コード例のコメントも英語。Editor ツールの UI 文言とサンプルの `// [検証用]` マーカーは画面・コードの表示どおり日本語で書き、英訳を添える）
- [ ] `README_JP.md` があり、英語化前の `README.md` と同じ内容である。`README.md` と `README_JP.md` の冒頭に互いへの言語切り替えリンクがある
- [ ] 4ファイル（`README.md`・`README_JP.md`・`Docs/usage.md`・`Docs/release-notes.md`）内のアンカーリンクが、英語化後の見出しから GitHub が生成するアンカーと一致している
- [ ] 日本語のまま残るドキュメント（設計仕様書・conventions・specs）へ英語ファイルからリンクする箇所に「(Japanese)」が付いている
- [ ] 開発側 `src/AddressableManager/Assets/@MAH/MAH-AddressableManager/package.json` と配布側 `MAH-AddressableManager/package.json` の `description`・`samples[].description` が英語で、2ファイルが同一内容であり、JSON として読める
- [ ] `Docs/release-notes-template.md` のテンプレート本体（見出し・記述例）が英語で、前文・執筆時の注意は日本語のまま「リリースノートは英語で書く」旨が書かれている
- [ ] `Docs/glossary.md` は日本語のみで、冒頭に英語の読者向けの案内（「英語」列・「公開」列の読み方）が1行ある。英語ファイルから用語集へのリンクに、日本語である旨が添えられている
- [ ] 英訳の用語が `Docs/glossary.md` の「英語」列と一致している（解放＝Release／回収＝reclaim／破棄＝destroy の使い分けを維持）
- [ ] `CHANGELOG.md` に `（[0-9a-f]{7}）` 形式のハッシュが残っておらず、該当エントリが `（直接コミット）` になっている
- [ ] `CLAUDE.md` の置き場所ルールに、英語で書くファイル・日本語で書くファイル・`README_JP.md` の同時更新ルールが書かれ、置き場所の表に `README_JP.md` の行があり、200行以内である

## 4. 制約事項

- コードは変更しない
- 開発者向けドキュメント（`src/AddressableManager/Docs/`・`CONTRIBUTING.md`・`CHANGELOG.md` の本文・`CLAUDE.md`）は日本語のまま
- `CHANGELOG.md` は指示がない限り更新しない運用だが、今回はユーザーの指示によりハッシュの置き換えだけを行う（エントリの追加はしない）
- 用語集は語彙の唯一の正であるため、言語別のファイルに分けない
- 英訳は翻訳に留め、記述内容（仕様・既定値・手順）を変えない

## 5. スコープ外（今回やらないこと）

- `.steering/` 配下の過去の作業記録の修正（作業履歴のため。旧アンカーへのリンクもそのまま）
- `Docs/usage.md` の日本語版の作成
- `CHANGELOG.md` 本文の英語化
- 配布フォルダへの開発フォルダの複製（リリース手順で行う）
- 新リポジトリの作成・移行作業そのもの
