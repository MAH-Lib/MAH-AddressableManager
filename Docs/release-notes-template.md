# リリースノートテンプレート

> 最終更新日：2026-10-24 ／ 対象 Unity バージョン：6000.3.26f1
> **リリースノートは英語で書く**（利用者向けドキュメントは英語が基準のため。本書の説明部分は開発者向けなので日本語）
> 本書は GitHub Releases の本文を書く際に複製して使うテンプレート。実リリース時の内容は複製先で編集し、本ファイルには書き足さない
> **複製先は [release-notes.md](release-notes.md)**。全バージョンのリリースノートを1ファイルにまとめ、新しいバージョンを**先頭（既存の節の上）に追記**する。同じ内容を GitHub Releases の本文にも貼る
> `CHANGELOG.md` が変更点の機械的な列挙であるのに対し、本書は**要約して伝える**ことが目的

---

## 想定読者について（`changelog-release-notes` スキルからの変更点）

スキルの既定は非エンジニア向け（クラス名・技術用語を避ける）だが、本モジュールの利用者は**導入先プロジェクトの Unity エンジニア**であるため、以下の通り読み替える：

- 公開 API 名（`LoadAsync<T>` 等）は明記してよい
- 破壊的変更（`BREAKING CHANGES`）は最上部に独立セクションを設ける
- 対応 Unity バージョン・Addressables バージョンを明記する
- 一方で「内部リファクタ・CI・依存更新は省略する」「1 項目 1 行」はスキルの原則通り維持する
- 文体はスキルの「です・ます調」に代えて、英語の平叙文（例：`Added ...` / `Fixed ...`）で書く

---

## テンプレート本体（ここから下を複製して使う）

```markdown
## vX.Y.Z (YYYY-MM-DD)

### BREAKING CHANGES
<!-- List breaking changes, if any. Delete this whole section if there are none -->
-

### New Features
<!-- New features visible to users. One item per line -->
-

### Improvements
<!-- Improvements to existing features, performance improvements, etc. -->
-

### Bug Fixes
<!-- Describe by symptom. e.g. "Fixed an exception thrown when changing scenes while LoadAsync is running" -->
-

### Compatibility
- Unity: 
- Addressables: 

### Installation
```
https://github.com/MAH-Lib/MAH-AddressableManager.git?path=MAH-AddressableManager#vX.Y.Z
```
```

---

## 執筆時の注意

- 内部リファクタ・CI 変更・依存パッケージの更新は省略する（利用者に見える変化がないため）
- 構成順は New Features → Improvements → Bug Fixes。目玉機能を先頭に
- セキュリティ修正は詳細を書かない（攻撃手がかりになるため「Improved security」程度に留める）
- `CHANGELOG.md` の `[Unreleased]` セクションを要約する形で作成する。切り出し元の日付・コミット単位の情報はここには持ち込まない
