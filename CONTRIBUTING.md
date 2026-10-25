# コントリビューションガイド

> 最終更新日：2026-10-15 ／ 対象 Unity バージョン：6000.3.26f1
> 本書は本リポジトリに変更を加える開発者（コミット・PR・リリース担当者）向けに、**いつ誰が何をするか**を定める
> CHANGELOG の**記入書式**は [CHANGELOG.md](CHANGELOG.md) 冒頭の記述が正。本書には書式を転記しない

---

## 作業を始める前に

- clone した後、**Unity で開くのは `src/AddressableManager/`**。リポジトリルートは Unity プロジェクトではなく、[CLAUDE.md](CLAUDE.md)／`Docs/`（用語集・リリースノート雛形）／[CHANGELOG.md](CHANGELOG.md) はルート側にあって階層がずれている。設計仕様書・コーディング規則・機能仕様書は Unity プロジェクト内の `src/AddressableManager/Docs/` にある
- **コードの変更は開発フォルダ `src/AddressableManager/Assets/@MAH/MAH-AddressableManager/` で行う**。リポジトリルートの `MAH-AddressableManager/` は UPM の配布フォルダ（`package.json`・`Samples~/` を持つ）で、リリース時に開発フォルダから複製する（4章）。配布フォルダを直接編集しない（次の複製で上書きされ、開発側にも残らないため）
- 変更を加える前に、設計仕様書の **6章「決定事項」** と **5.3「今回あえて採用しない設計」** を読む（多くの"良さそうな改善案"は検討済みで、意図的に却下されている）→ [addressable-manager-design.md](src/AddressableManager/Docs/addressable-manager-design.md)

---

## 1. ブランチ運用

| ブランチ | 役割 | 補足 |
| --- | --- | --- |
| `main` | リリース済みの安定版 | タグはこのブランチに打つ。直接 push しない |
| `develop` | 開発中の変更を集約する | PR のマージ先 |
| `feature/〇〇` | 機能追加の作業用 | `develop` から切り、`develop` へ戻す |
| `fix/〇〇` | 不具合修正の作業用 | 同上 |

単一モジュールで並行リリースが発生しないため、`release/x.y.z` ブランチは作らない。

**現状の例外**：PR 運用を始めるまでは `develop` への直接コミットも許容する。PR 運用を始めたら `feature/〇〇` 経由に切り替える。

---

## 2. Pull Request

- マージ先は `develop`
- 本文は [.github/PULL_REQUEST_TEMPLATE.md](.github/PULL_REQUEST_TEMPLATE.md) に従う。**項目は PR テンプレートが正であり、本書には転記しない**
- マージ前に、設計仕様書の 6章・5.3 に反していないかを確認する

---

## 3. CHANGELOG の運用

- **PR 経由**：マージ担当者が、マージ時に `[Unreleased]` の該当セクションへ 1 行追記する
- **直接コミット**：コミットハッシュはコミットした後でないと分からないため、**次のコミットでハッシュを添えて追記する**
- 書式は [CHANGELOG.md](CHANGELOG.md) 冒頭が正（本書には転記しない）
- `v1.0.0` のリリースまではバージョンセクションを切らず、`[Unreleased]` に蓄積し続ける

---

## 4. リリース手順

1. `[Unreleased]` の内容を `## [X.Y.Z] - YYYY-MM-DD` セクションへ切り出す
2. 冒頭の `<details>` 目次に新バージョンへのリンクを 1 行追加する
3. `[Unreleased]` を空のセクション構成（7 見出し）で作り直す
4. **開発フォルダの内容を配布フォルダへ複製する**：`src/AddressableManager/Assets/@MAH/MAH-AddressableManager/` の `Runtime/` `Editor/` `Tests/` `Documentation~/` と `README.md` `LICENSE.md`（`.meta` 含む）を、ルート `MAH-AddressableManager/` の同名フォルダに上書きする。`Samples/` は `Samples~/` に上書きする（`~` 付きのフォルダは Unity にインポートされず、利用者が Package Manager から選んで Import する形になる）。`.meta` も一緒に複製する（GUID が変わると、利用者のプロジェクトで参照が切れるため）。開発側で削除したファイルは、配布側からも削除する
5. ルート `MAH-AddressableManager/package.json` の `version` を `X.Y.Z` に更新する（タグ名 `vX.Y.Z` と一致させる）。サンプルを追加・改名したときは `samples` も更新する
6. `develop` を `main` へマージし、`main` に `vX.Y.Z` のタグを打つ
7. **別プロジェクトから UPM で取得できることを確認する**（タグ名や `?path=` を間違えても手元では気づけず、利用者が導入した時点で発覚するため）。URL の書式は [release-notes-template.md](Docs/release-notes-template.md)「導入方法」が正。Samples の Import も確認する
8. [release-notes-template.md](Docs/release-notes-template.md) の「テンプレート本体」を複製して [release-notes.md](Docs/release-notes.md) の**先頭に追記**し、同じ内容で GitHub Releases の本文を書く

### 最初のリリース

- **最初のタグは `v1.0.0`**。それより前（0.x 相当）はタグを打たない
- `v1.0.0` より前に導入先で試す場合は、git URL の末尾でブランチかコミットハッシュを指定する（例：`#develop`）

### バージョン番号の刻み方

| 区分 | 上げる条件 |
| --- | --- |
| MAJOR | 利用者側の修正なしでは追従できない変更。①[glossary.md](Docs/glossary.md) で「公開＝外部」に分類された型（`LoadAsync<T>`／`AssetTicket`／`AssetKey`／`IAddressableHandler` 等）のシグネチャ・挙動の変更 ②SO 設定クラス（`Addressable<機能>Settings`）のシリアライズ済みフィールドの改名・削除（利用者の設定アセットの値が黙って失われるため） ③Unity／Addressables の最低対応バージョンの引き上げ |
| MINOR | 後方互換を保った機能追加 |
| PATCH | 後方互換を保った不具合修正 |

- 「公開 API か」の判定は [glossary.md](Docs/glossary.md) の公開列に委ねる（本書で型を列挙し直すと二重管理になるため、例示にとどめる）
- MAJOR の変更はリリースノートの `BREAKING CHANGE` 節に載せる
- ルート `MAH-AddressableManager/package.json` の `version` とタグ名（`v` を除く）を一致させる

---

## 5. 関連ドキュメント

| 文書 | 定めるもの |
| --- | --- |
| [CHANGELOG.md](CHANGELOG.md) | 変更履歴と、その**記入書式** |
| [.github/PULL_REQUEST_TEMPLATE.md](.github/PULL_REQUEST_TEMPLATE.md) | PR 本文の項目 |
| [release-notes-template.md](Docs/release-notes-template.md) | GitHub Releases 本文のテンプレート |
| [release-notes.md](Docs/release-notes.md) | 全バージョンのリリースノート（新しい順に先頭へ追記） |
| [addressable-manager-design.md](src/AddressableManager/Docs/addressable-manager-design.md) | 設計仕様・採用理由・不採用の設計 |
| [conventions.md](src/AddressableManager/Docs/conventions.md) | 命名規則・namespace 構成 |
| [glossary.md](Docs/glossary.md) | 用語の語彙・公開／内部の区分 |
| [`src/AddressableManager/Docs/specs/`](src/AddressableManager/Docs/specs/) | 機能ごとの詳細仕様（手順・例外・サンプルコード） |
| [CLAUDE.md](CLAUDE.md) | Claude への作業指示。**ドキュメントの置き場所ルール**はここが正 |
