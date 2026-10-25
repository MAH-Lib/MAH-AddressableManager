# タスクリスト：AddressableManager Editor 共通基盤

> 対象：`design.md`（承認済み版） ／ 最終更新：2026-10-13 ／ ステータス：承認済み（タスク1〜4 実装完了。残りは、ユーザーによる最終確認）

## タスク

状態：⚪未着手 → 🔵作業中 → ✅完了（🔴ブロック中はブロック理由を備考へ）

各タスクは1コミット単位で、**各タスクの完了ごとに、ユーザーが確認してから次へ進む**。今回の成果物はドキュメントのみで、コードは書かない（コードは機能1の steering）。

**Claude の担当は実装（ドキュメントの作成・更新）まで**。ドキュメントの最終確認はユーザー（担当者）が行う。

| No | タスク | 状態 | 完了条件 | 備考 |
|---|---|---|---|---|
| 1 | 機能仕様書 `specs/editor-debug-window-spec.md` の作成 | ✅ | requirements.md 3章「仕様書の内容」の全項目を満たす。章立ては design.md 2章のとおり（1 概要／2 動作の全体像／3 設定項目／4 内部構成／5 設計の判断と理由／6 改修ガイド／7 注意点・既知の制約）。各機能の節を後から追記できる構成になっている | 書式は `download-spec.md` に合わせる（冒頭の引用で「この仕様はこのファイルを正とする」と宣言し、後から読む人向けと明記する）。実装手順・フェーズは書かない。対象範囲に「手動 Release・強制回収ボタン、SO 設定値の一覧表示は作らない」を書く。設計の判断と理由に、窓口を Runtime 側に置く理由・Reader という名前にする理由・`Core/Interfaces/` に置かない理由・`InternalsVisibleTo` が必要な理由を書く |
| 2 | 設計仕様書 `addressable-manager-design.md` の更新（3.1.1・3.9・4章・6章） | ✅ | 3.9 に到達手段の2案が残っていない。確定した方式と spec へのリンクがある。3.1.1 に Editor SO の探索・自動作成が一行ある。4章のフォルダ構成に `Runtime/Core/DebugReader/` と `AddressableDebugWindowSettings` がある。6章に決定事項が書式どおりに追記されている | 3.9 の記述は概要に留め、詳細は spec へのリンクにする。6章の書式は「（2026-10-13）**決定内容**。理由。**失うもの**：…（参照章）」。5.3「採用しない設計」に、Model の直接公開・push 通知・Model ごとのインターフェイスを足すかは、ユーザーに確認する |
| 3 | `conventions.md` の更新（7.2・10章） | ✅ | 7.2 に `IAddressableDebugReader` / `AddressableDebugReader`（`MAH.AddressableManager.Core`）・`<対象>Snapshot`（`MAH.AddressableManager.Core.<機能>`）・`AddressableDebugWindowSettings`（`MAH.AddressableManager.Editor.SettingsSync`）が分類され、窓口を `Core` に置く理由（`AddressableHandler` と同じ扱い）が備考にある。10章に `<対象>DebugReader`・`<対象>Snapshot`（Definition 層）・`<対象>Window` の命名パターンがある | 7.4「Core 直下の共通物は機能フォルダを参照しない」と矛盾しないよう、例外の理由を書く。`AddressableEditorSettingsSO` は既に 7.2 にあるので、重複させない |
| 4 | 用語集 `Docs/glossary.md` と図（`Docs/diagrams/editor-foundation-class.md`・`index.md`）の更新 | ✅ | glossary に「スナップショット」「デバッグウィンドウ」があり、既存の語彙（解放／破棄／回収・禁止用語）と衝突していない。クラス図が design.md 5章と一致し、`index.md` に登録されている | glossary はリポジトリルートの `Docs/`（`src/AddressableManager/Docs/` ではない）。図のメンバー名は、まだコードが無いため design.md の名前を正とし、機能1の実装時に実コードと照合する旨を図に書く。図と index のリンク先が存在することをシェルで確認する |

## 完了の定義（Definition of Done）

- [x] requirements.md の受け入れ条件をすべて満たす
- [x] 新しい型名（`IAddressableDebugReader` / `AddressableDebugReader` / `AddressableDebugWindowSettings` / `<対象>Snapshot`）が spec・design・conventions・図で表記揺れなく一致している（`grep` で確認）
- [x] 設計仕様書 3.9 に「弱参照リスト」「DIコンテナ経由」の記述が残っていない（`grep` で確認。6章の決定事項には不採用の理由として残る）
- [x] `Docs/diagrams/index.md` のリンク先がすべて存在する（シェルで確認）
- [x] コード（`Assets/` 配下）を変更していない（`git status` で確認）
- [x] 影響のある永続的ドキュメント（Docs/）の最終確認が完了している（Claude の担当外。ユーザーが実施）
- [x] `CHANGELOG.md` は更新していない（担当者の運用）
