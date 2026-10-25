# タスクリスト：AddressableManager Editor 機能1「実行時の読み込み/解放状態の確認」

> 対象：`design.md`（承認済み版） ／ 最終更新：2026-10-14 ／ ステータス：承認済み（タスク1〜5 実装完了。残りは、ユーザーによる最終確認）

## タスク

状態：⚪未着手 → 🔵作業中 → ✅完了（🔴ブロック中はブロック理由を備考へ）

各タスクは1コミット単位で、**各タスクの完了ごとに、ユーザーが確認してから次へ進む**。コミットはユーザーの指示があるまで行わない。

**Claude の担当は実装（コードとテストコード、ドキュメントの作成・更新）まで**。コードレビュー・手動検証・ドキュメントの最終確認・CHANGELOG の更新は担当外（ユーザーが実施）。

| No | タスク | 状態 | 完了条件 | 備考 |
|---|---|---|---|---|
| 1 | Runtime 側：`LifetimeScopeId` のアクセサ追加、Snapshot 3種（`AssetEntrySnapshot`・`ScopeCountSnapshot`・`IssuedTicketSnapshot`）、3つの Model へのコピー用メソッド、窓口（`IAddressableDebugReader`・`AddressableDebugReader`）、Manager への注入と `AccDebugReader`、`AssemblyInfo.cs` の `InternalsVisibleTo` | ✅ | コンパイルが通る（警告なし）。追加分がすべて `#if UNITY_EDITOR` で囲まれている。`Core/Loading/` から `Core/DebugReader/` を `using` していない（grep で確認）。既存の EditMode テストがすべて通る | Manager は Model をローカル変数に出すだけで、注入先・順序を変えない。コピーは `List` の `Clear()` と再利用で行い、`ToArray()` / `ToList()` を使わない。`Dictionary` は具体型のまま `foreach` する |
| 2 | `AddressableDebugReaderTests` の追加 | ✅ | 次を検証するテストが通る：Key・型・RefCount・ロード状態のコピー／複数スコープ（Manual・Scene・Owner）の内訳と、エントリの開始位置・件数の対応／チケットのスコープ・受け渡し済み・Label 一括・スタックトレース／ゴミの入った `List` が `Clear()` される／取得後に Model を変更しても取得済みの値が変わらない／空の Registry で空の `List` | 既存の `FakeAssetLoaderWorker`・`AssetRegistryModelTests` の書式に合わせる。実行結果を報告する |
| 3 | Editor 設定：`AddressableDebugWindowSettings`・`AddressableEditorSettingsSO`・`AddressableEditorSettingsLocator`、`AddressableDebugWindowSettingsTests` | ✅ | コンパイルが通る。更新間隔の下限補正のテストが通る。エディター再読込で、SO が 0件のとき既定パス `Assets/MAH.Data/AddressableManager/Editor/AddressableEditorSettings.asset` に作成されてログが出る（自分で確認できる範囲） | 探索は `AssetDatabase.FindAssets("t:AddressableEditorSettingsSO")`。複数件は警告して1件目。作成は `EditorApplication.delayCall`。`AddressableLoadingSettings` の `ClampValues()` と同じ形にする。asmdef に参照が足りなければ Tests の asmdef を修正する |
| 4 | Window：`IDebugWindowTab`・`AddressableDebugWindow`（メニュー `MAH/Addressable/Debug Window`・ツールバー・pull 更新・「未起動」表示）、`RuntimeStateTab`（エントリ → スコープ内訳 → チケット、スコープ名の解決、開閉状態） | ✅ | コンパイルが通る（警告なし。`InstanceIDToObject` が非推奨なら、非推奨でない API に替える）。Window から窓口と `Snapshot` 以外の internal 型を使っていない（grep で確認）。ウィンドウ上に Registry を書き換える操作がない | 描画は IMGUI。毎フレームのラムダ・文字列の確保を避け、開閉は `HashSet<(AssetKey, Type)>`。窓口への参照を保持せず、毎回 `AccInstance` から取り直す。動作確認（Play 中の表示）はユーザーが実施 |
| 5 | ドキュメント更新：spec（実装状況・2.4・4章・6.1・7章・8章）、`conventions.md` 10章、`diagrams/editor-foundation-class.md`、`loading-spec.md` 10章 | ✅ | 新しい型名（`AssetEntrySnapshot`・`ScopeCountSnapshot`・`IssuedTicketSnapshot`・`IDebugWindowTab`・`RuntimeStateTab` ほか）が spec・conventions・図で表記揺れなく一致している（grep で確認）。spec の 8章の状態表が更新され、機能1の節がある。図・リンク先が存在する（シェルで確認） | spec には実装手順・フェーズを書かない（動作・内部構成・改修ガイド）。8章の節の構成は、後続の機能がそのまま真似られる形にする |

## 完了の定義（Definition of Done）

- [x] requirements.md の受け入れ条件をすべて満たす
- [x] 本番ビルドに含まれる側（`#if UNITY_EDITOR` の外）に、窓口・Snapshot・コピー用メソッドがない
- [x] 既存テストと追加テストがすべて通る（Claude が実行して結果を報告する）
- [x] 影響のある永続的ドキュメント（Docs/）の更新が完了している
- [x] コードレビュー・手動検証（Play 中の表示の確認）・ドキュメントの最終確認が完了している（Claude の担当外。ユーザーが実施）
- [x] `CHANGELOG.md` は更新していない（担当者の運用）
