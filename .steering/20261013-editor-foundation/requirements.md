# 要求内容：AddressableManager Editor 共通基盤

> 作業ディレクトリ：`.steering/20261013-editor-foundation/` ／ 作成日：2026-10-13 ／ ステータス：承認待ち ／ 対象 Unity バージョン：6000.3.25f1
> 関連する永続的ドキュメント：`src/AddressableManager/Docs/addressable-manager-design.md`（3.1.1・3.9・4章・6章を更新。3.9 の到達手段2案を1つに確定する）／`src/AddressableManager/Docs/specs/editor-debug-window-spec.md`（新規。この共通基盤の正）／`src/AddressableManager/Docs/conventions.md`（7.2 に新しい型の namespace 分類、10章に命名パターンを追加）／`Docs/glossary.md`（「スナップショット」「デバッグウィンドウ」を追加。既存語彙と衝突しないか確認）／`src/AddressableManager/Docs/diagrams/`（`editor-foundation-class.md` を新規作成し `index.md` に登録）／`src/AddressableManager/Docs/specs/loading-spec.md`（10章の「EditorWindow での一覧表示は Editor 拡張の実装時に行う」の参照先を確認。影響は実装時に確認）

## 1. 変更・追加する機能の説明

Editor 拡張（実行時状態・リーク検出・owner Watcher・計測・サイズ概算）の全タブが共通で使う基盤を決め、機能仕様書 `editor-debug-window-spec.md` に残す。**今回の成果物はドキュメントのみで、コードは機能1（実行時状態）の steering で書く。**

決める内容は次の4点。

- 実行中の Registry への到達手段：Manager が `Awake` で Model を窓口 `AddressableDebugReader` にコンストラクタ注入し、`#if UNITY_EDITOR` の internal アクセサ `AccDebugReader` で `IAddressableDebugReader` 型として渡す。Window は `AddressableManager.AccInstance.AccDebugReader` だけを参照する
- 読み取り専用の境界：Window は `IAddressableDebugReader` しか持たない。スナップショット（readonly struct）を Window 側の `List<T>` へコピーする形で取得し、更新は Window 側の一定間隔 pull とする
- Editor 側の設定：`AddressableEditorSettingsSO`（`AddressableDebugWindowSettings` を保持）に出して Inspector から操作する。型で探索し、0件ならエディター起動時に自動作成する
- 仕様書の構成：`editor-debug-window-spec.md` を1本にし、今回は共通基盤の章だけを書く。各機能の節はその機能の実装時に追記する

今やる理由：Editor 拡張は6項目あり、機能ごとに別 steering で実装する。各機能が到達手段や読み取り専用の境界を別々に決めると、機能同士で食い違う。設計仕様書 3.9 は到達手段を2案並記したまま未決で、Model に列挙用の読み取り API が無いことも分かっているため、先に共通の決め方を残す。

## 2. ユーザーストーリー

- 実装担当者として、機能ごとの Editor タブを同じ作りで追加できるように、到達手段・スナップショット・Window の枠・設定の置き方が1つの仕様書にまとまっていてほしい
- 実装担当者として、Editor 側から Registry を壊さないために、Window が読み取り専用のインターフェイス越しにしか Model に触れない構造にしたい
- 実装担当者として、更新間隔のような Editor の挙動を Inspector から変えられるように、設定を SO にまとめたい
- レビュアーとして、本番ビルドに Editor 用のコードが残らないことを確認するために、窓口・スナップショットが `#if UNITY_EDITOR` で囲まれる前提が仕様書に書かれていてほしい
- 後から読む開発者として、なぜ Handler ではなく Reader という名前なのか、なぜ Editor フォルダに置かないのかを、設計の判断と理由から知りたい

## 3. 受け入れ条件

### 仕様書の内容（`editor-debug-window-spec.md`）
- [ ] 到達手段（コンストラクタ注入と `AccInstance.AccDebugReader`）が、Manager の `Awake`・アクセサ・Window 側の取得の流れまで書かれている。新しい static 状態を作らないことが明記されている
- [ ] 読み取り専用の境界が書かれている：Window は `IAddressableDebugReader` 型しか持たず、Model の書き込み API に届かない。Model 側は `#if UNITY_EDITOR` の internal なコピー用メソッドだけで、呼ぶのは窓口に限る。Model ごとの読み取り専用インターフェイスは作らない
- [ ] スナップショットが「取得時点の値のコピー（readonly struct）」であり、Window 側の `List<T>` へコピーする形（`ToArray()` / `ToList()` を使わない）で書かれている
- [ ] 更新方式が Window 側の pull（既定 0.5 秒・下限 0.1 秒）であり、Runtime 側にイベントを足さないこと、Window を閉じていれば Runtime 側のコストがゼロであることが書かれている
- [ ] `AccInstance == null`（未 Play・破棄済み）のときの Window の表示（「未起動」）が書かれている
- [ ] `AddressableEditorSettingsSO` と `AddressableDebugWindowSettings` の構成、型による探索（`AssetDatabase.FindAssets`）、複数件のときの警告、0件のときのエディター起動時の自動作成（`[InitializeOnLoad]`＋`EditorApplication.delayCall`、既定パス `Assets/Data/MAH/AddressableManager/Editor/`）、作成後の移動が可能であることが書かれている
- [ ] 機能ごとの設定の追加方法（`Addressable<機能>Settings` を SO に足す）、タブの追加方法、読み取り API の追加方法が「改修ガイド」にある
- [ ] 設計の判断と理由に、次が書かれている：窓口を Runtime 側（`Runtime/Core/DebugReader/`）に置く理由／名前を Handler でなく Reader にする理由／インターフェイスを `Core/Interfaces/` に置かない理由／`InternalsVisibleTo("MAH.AddressableManager.Editor")` が必要な理由
- [ ] 実装手順・フェーズが書かれていない（後から読む人向けの動作・内部構成・改修ガイドになっている）
- [ ] 各機能の節を後から追記できる構成になっている

### 設計仕様書・規約・用語集・図
- [ ] 設計仕様書 3.9 に、到達手段の2案（static 弱参照リスト／DI 経由）が残っていない。確定した方式と spec へのリンクがある
- [ ] 設計仕様書 3.1.1 に、Editor SO の探索と自動作成が一行で触れられ、詳細は spec を参照している
- [ ] 設計仕様書 4章に、`Runtime/Core/DebugReader/` と `Editor/SettingsSync/AddressableDebugWindowSettings` がフォルダ構成に追加されている
- [ ] 設計仕様書 6章に、決定事項が「（日付）**決定内容**。理由。**失うもの**：…」の書式で追記されている
- [ ] `conventions.md` 7.2 に `IAddressableDebugReader` / `AddressableDebugReader`（`MAH.AddressableManager.Core`）・`<対象>Snapshot`（`MAH.AddressableManager.Core.<機能>`）・`AddressableDebugWindowSettings`（`MAH.AddressableManager.Editor.SettingsSync`）が分類されている。窓口を `Core` に置く理由（`AddressableHandler` と同じ扱い）が備考にある
- [ ] `conventions.md` 10章に、窓口 `<対象>DebugReader`・スナップショット `<対象>Snapshot`（Definition 層）・Window `<対象>Window` の命名パターンがある
- [ ] 新しい型名が spec・design・conventions・図の間で表記揺れなく一致している
- [ ] `Docs/diagrams/index.md` に Editor 基盤のクラス図が登録されている
- [ ] 手動 Release・強制回収ボタン、SO 設定値の一覧表示は実装しない前提が、spec の対象範囲に書かれている

## 4. 制約事項

- Editor 拡張は**読み取り専用**とする。手動 Release・強制回収のボタンは作らない（CLAUDE.md「設計の絶対ルール」）
- Controller・Service・Model はピュアC#のままにする。MonoBehaviour を継承するのは Manager / Editor / View のみ
- 3つの Controller は互いを参照しない。窓口は複数機能の Model を知るため機能フォルダの外に置くが、機能フォルダ同士の相互参照は作らない（`Core/Loading/` のファイルに `using ...Core.Download;` が現れない）
- 記録・表示処理はすべて `#if UNITY_EDITOR` で囲み、本番ビルドから完全に除外する。一時コレクションを都度確保しない
- DI コンテナ（VContainer 等）・R3 に依存しない。ここでの「コンストラクタ注入」は、Manager が `new` した Model を渡すだけの手動注入である
- 本書の承認をもって、設計仕様書 3.9 の到達手段2案を不採用にする変更の承認とする
- CHANGELOG は更新しない（担当者の運用）

## 5. スコープ外（今回やらないこと）

- **コードの実装**。`IAddressableDebugReader` / `AddressableDebugReader` / `AddressableDebugWindow` / `AddressableEditorSettingsSO` / `AddressableDebugWindowSettings` / `InternalsVisibleTo` の追加はすべて機能1の steering で行う
- 各機能（実行時状態・リーク検出・owner Watcher・読み込み計測・サイズ概算）のタブと、そのスナップショット項目・読み取り API。機能ごとの steering で決め、spec に節を追記する
- Model への列挙用の読み取り API の追加（`LifetimeScopeId` の Kind / Value のアクセサを含む）
- リークの蓄積（現状はログのみ）。機能2で扱う
- 計測レベルとデファインシンボルの同期（`ProfilingSymbolUpdater`）と `LoadProfiler`。機能4で扱う
- 設計仕様書 7章の未決事項（計測レベルの実行時参照ほか）の確定
