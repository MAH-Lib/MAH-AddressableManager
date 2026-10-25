# タスクリスト：v1.0.0 配布に向けたドキュメント更新

> 対象：`design.md`（承認済み版） ／ 最終更新：2026-10-15 ／ ステータス：承認済み（全タスク完了。ドキュメントの最終確認はユーザー）

## タスク

状態：⚪未着手 → 🔵作業中 → ✅完了（🔴ブロック中はブロック理由を備考へ）

本 steering の範囲はドキュメントの更新だけ。コード・`package.json` は変更しない。コミットはユーザーの指示があるまで行わない。

| No | タスク | 状態 | 完了条件 | 備考 |
|---|---|---|---|---|
| 1 | 事実の確認：`IAddressableHandler.cs`・`MENU_PATH`・`Editor/SettingsSync/*.cs` の Tooltip・`package.json` の samples・`Samples~/` のシーン/スクリプト・サンプルの Addressables 登録の要否 | ✅ | 2〜3 の記述の根拠が揃っている | Import したサンプルは、アドレス・Label の登録と Build Settings へのシーン登録が必要（`Samples~` に Addressables の設定は含まれない）。`download-spec.md` 11.1 は対象 Label の既定を `Manual` としているが、サンプルの `DownloadSettings.asset` は `Preset`（報告で指摘） |
| 2 | `Docs/usage.md`：導入への導線、「Editor ツール」章の新設、「サンプル」章の詳細化、API 記述の突き合わせ、最終更新日 | ✅ | メニュー名・設定項目・API がコードと一致している | README のリンク先になるため先に作る |
| 3 | `README.md`：「開発中」注記の削除、導入3小節、「サンプル」「Editor ツール」節、開発者向け節、最終更新日 | ✅ | URL がすべて `?path=AddressableManager`。`usage.md` へのリンク・アンカーが切れていない | |
| 4 | `AddressableManager/README.md`（パッケージ内）の新規作成 | ✅ | 概要と git URL のみ | |
| 5 | `Docs/glossary.md`・`Docs/release-notes-template.md` の更新 | ✅ | 配布パス・追記先が現状と一致している | |
| 6 | `CONTRIBUTING.md`：開発場所／配布物の関係、リリース手順（ルートへの複製・`release-notes.md` への追記）、5章の表 | ✅ | 手順が上から順に実行できる | |
| 7 | 設計仕様書：冒頭の参照先、4章の注記、6章の決定事項（design.md 2.2） | ✅ | 6章が既存書式どおり | |
| 8 | `CLAUDE.md`：ツリー・置き場所ルール表・罠節・最終更新日 | ✅ | 200行以内。Build Settings の行が変わっていない | |
| 9 | `CHANGELOG.md`：`[Unreleased]` への実装履歴の追記、`[1.0.0] - 2026-10-15` 節の新設、目次のリンク（design.md 2.1） | ✅ | 冒頭の書式どおり | 今回のドキュメント更新の行は「直接コミット」と記載。ハッシュは次のコミットで添える |
| 10 | `Docs/release-notes.md` の新規作成（v1.0.0 の節） | ✅ | テンプレート本体の構成どおりで、内部リファクタ・レビュー修正を含まない | 9 の後に作る（CHANGELOG の要約のため） |
| 11 | 検証：design.md 5章の grep・リンク・`git status` | ✅ | 古い記述が残っておらず、コード・`package.json` に変更がない | grep で古い記述 0 件、相対リンク・アンカーの切れ 0 件。`package.json` の差分は作業中にユーザーが行った修正（UniTask の依存の削除・Addressables を `2.9.0` に固定）で、本作業では変更していない |

## 完了の定義（Definition of Done）

- [x] requirements.md の受け入れ条件をすべて満たす
- [x] 古い記述（`@MAH/AddressableManager#`・準備中・未リリース・`Assembly-CSharp`）が残っていない（`grep` で確認。設計仕様書 6章の経緯を除く）
- [x] コード・`package.json` を変更していない（`git status` で確認）
- [x] `package.json` の問題点を報告で指摘した
- [ ] ドキュメントの最終確認が完了している（Claude の担当外。ユーザーが実施）
