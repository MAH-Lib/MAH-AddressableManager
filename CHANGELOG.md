# CHANGELOG

このファイルはプロジェクトの変更履歴を記録するためのものです。
各変更は Pull Request 単位で、マージ担当者が追記してください。

- 記述時はセクションごとに `- [YYYY-MM-DD] 更新内容（PullRequest番号 or 直接コミット）` で記述します。
- Note セクションには変更履歴時の注意事項・共有内容を記載します。

記法は [Keep a Changelog](https://keepachangelog.com/ja/1.0.0/) に準拠しています。  
バージョン番号は [Semantic Versioning](https://semver.org/lang/ja/) に従います。

[最新開発ログはこちら](#unreleased)

<details>
<summary>過去のバージョンを見る</summary>

- [Unreleased](#unreleased)
- [1.0.0 - 2026-10-24](#100---2026-10-24)
- [X.Y.Z - YYYY-MM-DD](#xyz---yyyy-mm-dd)

</details>

---

## [Unreleased]

<!-- リリース前（開発中・v0.x.x相当）の変更はここに蓄積する。正式リリース時に下へ切り出す -->

### Note
- [2026-09-15] 本ファイルの運用を開始。リリース前のため、バージョンセクションは切らず [Unreleased] に蓄積する
- [2026-10-24] 新しいリポジトリへ移行するため、既存エントリのコミットハッシュを「直接コミット」に置き換えた（旧リポジトリのハッシュは移行後に参照できないため）（直接コミット）
- [2026-10-24] `develop` ブランチを作成。PR 運用を始めるまでは、開発中の変更を `develop` に直接コミットする（`CONTRIBUTING.md` 1章の例外）（直接コミット）

### Added
- [2026-09-08] リポジトリと Unity プロジェクトを作成（直接コミット）
- [2026-09-10] 開発方針ドキュメント `CLAUDE.md` を作成（直接コミット）
- [2026-09-15] 設計仕様書・コーディング規則・用語集を整備（直接コミット）
- [2026-09-29] Addressables の読み込み・解放機能を実装（`AddressablePath` 属性を含む）（直接コミット）
- [2026-09-30] 読み込み・解放機能のサンプルシーン（Prefab / Sprite / Audio）を作成（直接コミット）
- [2026-10-02] Label 一括ロード（`LoadByLabelAsync` / `AssetGroupTicket`）とサンプルシーン（LabelLoadScene）を追加（直接コミット）
- [2026-10-02] `AddressableLabel` 属性を追加。Inspector のプルダウンから Label を選べる（直接コミット）
- [2026-10-05] Addressables の初期化機能を実装。自動／手動の切り替え・初期化前の Load の扱い・カタログの URL キャッシュ対策を Runtime SO で設定できる（直接コミット）
- [2026-10-05] 初期化機能のサンプルシーン（InitializeScene）を作成（直接コミット）
- [2026-10-08] コンテンツのダウンロード機能を実装。`IAddressableHandler` に更新の判定（`HasUpdateAsync`）・カタログの確認と件数/容量の取得（`CheckUpdateAsync`）・Label 単位のダウンロード（`DownloadAsync`）・状態の取得（`AccDownloadStatus`）を追加。カタログ更新の方針・対象 Label・リトライ回数/待機時間を Runtime SO で設定できる（直接コミット）
- [2026-10-08] ダウンロード機能のサンプルシーン（DownloadScene）を作成（直接コミット）
- [2026-10-08] 利用者向けの使い方（`Docs/usage.md`）を作成。設定アセットの全項目と、初期化・データ更新・読み込み/解放・属性の API の使い方を1ファイルにまとめた（直接コミット）
- [2026-10-13] Editor 拡張の機能仕様書（`editor-debug-window-spec.md`）を作成（直接コミット）
- [2026-10-14] 実行時の読み込み/解放状態を確認する EditorWindow `AddressableDebugWindow`（メニュー `MAH/Addressable/Debug Window`）を実装。Play 中の Registry を読み取り専用で一覧し、`AssetKey`・型・アセット名・`RefCount`・ロードステータス・寿命スコープ別の確保数・発行済みチケットの確保元スタックトレースを確認できる。あわせて Editor 拡張の共通基盤（読み取り窓口 `IAddressableDebugReader`・`Snapshot`・Editor 用 SO・タブの枠）を追加（直接コミット）
- [2026-10-14] 同一アドレスの Prefab を複数回ロードする動作確認用サンプルシーン（PrefabSameAddressLoadScene）を追加（直接コミット）
- [2026-10-14] リーク検出一覧（デバッグウィンドウの「リーク」タブ）を追加。`Release` されずに自動回収されたものを確保元ごとに集計し、呼び出し元へのジャンプ・テキストへのログ出力（手動・Play 終了時の自動）ができる（直接コミット）
- [2026-10-15] owner Watcher の可視化（デバッグウィンドウの「Owner」タブ）を追加。監視中の Owner の状態と、回収が `OnDestroy` 経由かポーリング経由かを表示する（直接コミット）
- [2026-10-15] 未登録の依存を確認する EditorWindow `AddressableDependencyWindow`（メニュー `MAH/Addressable/Dependency Check`）を追加。登録済みアセットが使っている未登録のアセットを、Play もビルドもせずに一覧し、テキストに出力できる（直接コミット）
- [2026-10-24] ルート README の日本語版 `README_JP.md` を追加。英語版 `README.md` と相互に言語切り替えリンクを置いた（直接コミット）
- [2026-10-24] アセットの登録（自動登録・手動一括登録）を追加。ルールの SO（`AddressableAutoRegistrationRuleSO`）に従ってインポートのたびに登録する自動登録と、Project ウィンドウの右クリック（`MAH > Register to Addressables...`）から選んだフォルダを一括で登録する手動一括登録。ルールの一覧・競合・アドレスの重複・当て直しは、EditorWindow `AddressableRegistrationRuleWindow`（メニュー `MAH/Addressable/Auto Registration`）で確認できる（直接コミット）
- [2026-10-24] Editor ツールの表示言語の切り替え（日本語・英語）を追加。`Edit > Preferences > MAH Addressable > Language` で切り替える（個人ごとの設定。未設定のときは PC の言語設定から決め、日本語を優先する）。言語ファイル（JSON）で、利用者が他の言語を足したり、同梱の訳を一部だけ直したりできる（`Assets/` 配下に `MAH.AddressableManager.Editor.<言語コード>.json` を置く。雛形はメニュー `MAH/Addressable/Localization/Export Translation Template` で書き出せる）。手順は `Docs/usage.md` の「6.6 Editor Language」、仕様は `editor-localization-spec.md` を参照（直接コミット）

### Changed
- [2026-10-05] 利用者の入口を `IAddressableHandler` に集約し、初期化の API（`InitializeAsync`・`OnInitialized` 等）を Manager から Handler へ移動。`IAssetHandler` → `IAddressableHandler`、`AccAssetHandler` → `AccHandler` に改名（直接コミット）
- [2026-10-08] Unity Editor を 6000.3.23f1 → 6000.3.25f1 に更新。あわせて Addressables を 2.9.1 → 2.10.3（ScriptableBuildPipeline 2.6.1 → 2.6.2）に更新し、各ドキュメントの対象バージョン表記を変更（直接コミット）
- [2026-10-08] ルート `README.md` の「使い方」に最短手順（設定アセット作成・Manager の配置・Load/Release の例）を追加し、`Docs/usage.md` へのリンクを追加。実パスと一致していなかった git URL は削除し、導入手順は準備中とした（直接コミット）
- [2026-10-08] 利用者向けドキュメントの置き場所をリポジトリルート `Docs/` に変更（パッケージ内 README は概要と git URL のみ）。`CLAUDE.md` の置き場所ルールを更新（直接コミット）
- [2026-10-08] 各 asmdef の Version Defines（UniTask）の Expression を `2.5` → `2.5.0` に変更（Unity が自動整形する表記に合わせた。判定条件「2.5.0 以上」は変わらない）（直接コミット）
- [2026-10-15] Editor 機能4を「読み込み計測」から「未登録の依存の確認（Dependency Check）」に差し替え、設計仕様書・機能仕様書・用語集を更新（直接コミット）
- [2026-10-24] 利用者向けドキュメントを英語化。ルート `README.md`・`Docs/usage.md`・`Docs/release-notes.md`・配布フォルダの `README.md`・`package.json` の説明文（`description`・`samples`）を英語に、`Docs/release-notes-template.md` のテンプレート本体を英語の見出しに変更。Editor ツールの UI 文言は画面どおり日本語で書き、英訳を添えた（直接コミット）
- [2026-10-24] 用語集 `Docs/glossary.md` は日本語のまま残し、冒頭に英語の読者向けの案内（「英語」列・「公開」列の読み方）を追加（直接コミット）
- [2026-10-24] `CLAUDE.md` の置き場所ルールに言語ルール（利用者向けは英語・開発者向けは日本語、`README_JP.md` は英語版と同時に更新）を追加（直接コミット）
- [2026-10-24] アセットの登録：ログの細かさが「まとめだけ」のとき、5件を超える警告を1件に要約する（件数と最初の5件。「1件ずつ」では全件）。ルールの SO の Inspector と手動一括登録の文字列の項目は、Enter を押すかフォーカスが外れたときに反映する。当て直しと手動一括登録は、一覧を作った時点から状態が変わったエントリを書き込まずにスキップし、件数を知らせる（直接コミット）
- [2026-10-24] Editor ツールの文言（ウィンドウ・ダイアログ・Editor 側のログ・出力ファイル）を、コード内の日本語の文字列から、言語ファイルのキー（`EditorText`）に移した。失敗メッセージの区切りは、全角の「：」から `: ` に変更。Runtime のログ・例外メッセージは日本語のまま（直接コミット）
- [2026-10-24] 設定 SO・ルールの SO（Runtime の `AddressableRuntimeSettingsSO` と、Loading / Initialize / Download の設定クラスを含む）から `[Header]` / `[Tooltip]` を外し、見出し・ツールチップを言語ファイルに移した。表示は専用の Inspector（`AddressableEditorSettingsEditor` / `AddressableRuntimeSettingsEditor`）が描く。ビルドの挙動・内容は変わらない（直接コミット）
- [2026-10-24] Unity Editor を 6000.3.25f1 → 6000.3.26f1 に更新し、各ドキュメントの対象バージョン表記を変更。あわせて `CLAUDE.md`・`conventions.md`・設計仕様書に、Editor の区分（`Localization/` を足した5区分）と文言の規則を追加（直接コミット）

### Fixed
- [2026-10-02] 読み込み・解放処理のコードを、レビュー指摘に基づき修正（直接コミット）
- [2026-10-05] UniTask 導入時、ポーズ中（`timeScale = 0`）にリトライの待機が終わらない問題を修正（直接コミット）
- [2026-10-05] 初期化処理のコードを、レビュー指摘に基づき修正（直接コミット）
- [2026-10-08] ダウンロード処理のコードを、レビュー指摘に基づき修正（直接コミット）
- [2026-10-14] Editor 拡張（デバッグ用 EditorWindow）のコードを、レビュー指摘に基づき修正（直接コミット）
- [2026-10-15] リーク検出一覧のコードを、レビュー指摘に基づき修正（直接コミット）
- [2026-10-15] owner Watcher の可視化のコードを、レビュー指摘に基づき修正（直接コミット）
- [2026-10-15] Dependency Check のコードを、レビュー指摘に基づき修正（直接コミット）
- [2026-10-24] アセットの登録のコードを、レビュー指摘に基づき修正。対象フォルダの名前を変えたあと、自動登録が古いパスで判定し続ける問題、スクリプトの保存などルールと関係ないインポートでも全エントリを走査していた問題、Inspector のキー入力のたびに重い処理と警告が走る問題を直した（直接コミット）
- [2026-10-24] Editor ツールの表示言語の切り替えのコードを、レビュー指摘に基づき修正。リークタブが言語の切り替えに追従しない問題、言語ファイルの読み直しが AssetDatabase の Refresh の最中だと利用者のファイルを読み飛ばす問題、手動一括登録・Dependency Check のメッセージが古い言語で残る問題などを直した（直接コミット）

### Removed

### Deprecated

### Security

---

## [1.0.0] - 2026-10-24

### Added
- [2026-10-24] パッケージのドキュメントを開発フォルダ `Assets/@MAH/MAH-AddressableManager/` に追加し、配布フォルダにも複製した。`README.md`（概要と git URL）・`LICENSE.md`（ルートの `LICENSE` と同じ MIT）・`Documentation~/MAH-AddressableManager.md`（英語の短いオフライン用要約。使い方の原本は `Docs/usage.md`）。変更履歴は `package.json` の `changelogUrl` でルートの `CHANGELOG.md` を指すため、パッケージ内には置かない。あわせて `CLAUDE.md`・`CONTRIBUTING.md` の置き場所・複製の規則を更新（直接コミット）

### Changed
- [2026-10-24] リリースのため、配布フォルダ `MAH-AddressableManager/` を開発フォルダから複製し、`package.json` を更新。`version` を `0.1.0` → `1.0.0`、`license` を `Unlicense` → `MIT`（ルートの `LICENSE` に合わせた）、`keywords` に `Unity` `Editor` を追加（直接コミット）

## [X.Y.Z] - YYYY-MM-DD

<!-- リリース時：Unreleasedの内容をここへ移動し、Unreleasedは空のセクション構成で再作成する。
     目次<details>にもリンクを1行追加する -->

### Note
-

### Added
-

### Changed

### Fixed

### Removed

### Deprecated

### Security
