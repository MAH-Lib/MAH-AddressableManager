# 設計：AddressableManager Editor 機能4「未登録の依存の確認（Dependency Check）」

> 対象：`requirements.md`（承認済み版） ／ 作成日：2026-10-15 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.25f1
> 備考：実装を先に行ったため、本書は実装した内容の記録である。仕様の正は `src/AddressableManager/Docs/specs/editor-dependency-check-spec.md`。

## 1. 実装アプローチ

Editor アセンブリの中だけで完結する、独立したウィンドウ `AddressableDependencyWindow` を作る。処理は3段に分け、Unity に依存する部分を走査（`DependencyCheckScanner`）とウィンドウに閉じ込める。

```
Scan ボタン → DependencyCheckScanner（Unity を読む）→ DependencyCheckInput（文字列だけ）
            → DependencyCheckAggregator（行を作る）→ DependencyCheckRow の一覧をウィンドウが保持
            → BuildVisibleRows（絞り込み・並べ替え）→ 描画 / DependencyCheckLogFormatter（テキスト出力）
```

- **走査**：`AddressableAssetSettingsDefaultObject.Settings` の全グループを `GatherAllAssets(list, true, true, false)` で集めたものを親とし、そのパスの集合を「登録済み」にする。`PlayerDataGroupSchema` のグループは除く。SpriteAtlas の packables を「登録済み」に加えてから、親ごとに `AssetDatabase.GetDependencies(path, true)` を辿る。結果は `ScanResult`（`Completed`・`Canceled`・`NoSettings`）で返し、進捗バーは `finally` で必ず消す
- **集約は Unity 非依存**：入力（`DependencyCheckInput`）はパスと型名の文字列・`HashSet`・`Dictionary` だけ。EditMode テストで偽の入力を使える。重複は「参照元のグループ名の種類数が2以上」で判定する
- **ウィンドウの保持**：結果の行は `[Serializable]` のクラス（`DependencyCheckRow`・`DependencyCheckReferrer`）にし、ウィンドウの `[SerializeField]` で保持する（再コンパイルで消えない）。列幅・絞り込みの状態も `[SerializeField]`。展開状態と表示行の添字は保持しない
- **絞り込み・並べ替え**：ウィンドウの `internal static BuildVisibleRows` に切り出し、Scan・絞り込み・並べ替えの変更のときだけ `_visibleRows`（`List<int>`）を作り直す（`OnGUI` のたびには作らない）
- **描画**：行の高さを固定し、スクロール位置から見える範囲の行だけを `GUI.Label` で描く。列は `_columnX`（列幅から作る左端）で配置し、見出しの境界の `hotControl` ドラッグで幅を変える。列幅の合計が表示幅を超えたら横スクロール（見出しは `GUI.BeginGroup` で同じ量だけずらす）
- **Scan の実行**：ボタンの処理が終わってから `EditorApplication.delayCall` で走査する（`OnGUI` の途中で結果が変わってレイアウトが崩れるのを防ぐ）
- **出力の共通化**：`LeakLogWriter` の出力先の解決・日時付きの書き込み（連番・UTF-8 BOM なし・失敗時は警告）を `EditorTextFileWriter` に切り出し、`LeakLogWriter` は委譲する。公開シグネチャ・警告文言は変えない。固定名の `WriteLastPlay` はリーク固有なので残す
- **設定**：`AddressableDependencyCheckSettings`（出力先フォルダ）を Editor SO が保持し、`OnValidate` で既定値を補う（既存アセットのマイグレーション不要）

検討した代替案と却下理由：
- **Addressables 標準の Analyze を使う案**：ビルドレイアウトの計算を伴って重く、絞り込み・移動の操作性も乏しい（仕様書 5章）
- **デバッグウィンドウのタブにする案**：`IDebugWindowTab.Refresh(reader)` は Play 中の窓口を前提にしており、データの出所が異なる
- **`readonly struct` で行を持つ案**：Unity がシリアライズできず、再コンパイルで結果が消える
- **走査を `async`（UniTask / Task）にする案**：進捗バーのキャンセルと `AssetDatabase` の呼び出しは主スレッドの同期処理で足りる。非同期にすると `#if ADDRESSABLE_UNITASK_SUPPORT` の二重実装が増える
- **`LeakLogWriter` に Dependency Check の出力も足す案**：リーク固有の名前・文言・固定名の処理に、別機能の都合が混ざる。共通部だけを `EditorTextFileWriter` に切り出す
- **列幅を IMGUI の `MultiColumnHeader` で持つ案**：行の仮想化（見える範囲だけ描く）と、既存の IMGUI の書き方からずれるため、見出しの境界のドラッグを自前で書いた
- **パスの列を残り幅いっぱいに伸ばす案**：他の列の幅変更と干渉するため、全列を固定幅にして横スクロールで吸収した

## 2. 変更するコンポーネント

パスは `src/AddressableManager/Assets/@MAH/AddressableManager/` を起点とする（ドキュメントは `src/AddressableManager/Docs/` 起点）。

### コード（Editor）

| 対象 | 変更内容 | 新規/修正 |
|---|---|---|
| `Editor/Window/AddressableDependencyWindow.cs` | `EditorWindow`。メニュー・ツールバー（Scan／テキスト出力／型・グループの `GenericMenu`／名前検索／表示切り替え／サイズ順）・注記・状態表示・仮想化した一覧・展開・クリックで Ping・列の区切り線と列幅のドラッグ・`BuildVisibleRows` | 新規 |
| `Editor/Window/DependencyCheckScanner.cs` | static。`TryScan(out DependencyCheckInput)`。親の収集・SpriteAtlas の packables・依存の走査・型名とサイズ・進捗とキャンセル | 新規 |
| `Editor/Window/DependencyCheckInput.cs` | 集約への入力（`Parent`・登録済みの集合・型名・サイズ・`Pack Separately` のグループ名）。Unity 非依存 | 新規 |
| `Editor/Window/DependencyCheckAggregator.cs` | static。`Aggregate(input, result)`。1子1行・参照元・グループ数・印・パス順。Unity 非依存 | 新規 |
| `Editor/Window/DependencyCheckRow.cs`・`DependencyCheckReferrer.cs` | `[Serializable]` の行・参照元 | 新規 |
| `Editor/Window/DependencyCheckLogFormatter.cs` | static。`Format`（仕様書 2.5。参照元のパスの幅は出力全体の最長にそろえる）・`FormatSize`。Unity 非依存 | 新規 |
| `Editor/Window/EditorTextFileWriter.cs` | static。`ResolveFolder`・`CreateFileName`・`WriteTimestamped`・`ENCODING` | 新規 |
| `Editor/Window/LeakLogWriter.cs` | `ResolveFolder`・`CreateFileName`・`WriteManual` を `EditorTextFileWriter` へ委譲。`WriteLastPlay` は残し、`ENCODING` を共有 | 修正 |
| `Editor/SettingsSync/AddressableDependencyCheckSettings.cs` | `[Serializable]`。出力先（既定 `Logs/AddressableManager`）・`ClampValues` | 新規 |
| `Editor/SettingsSync/AddressableEditorSettingsSO.cs` | `_dependencyCheck`・`AccDependencyCheck` を追加。`OnValidate` で補完・補正 | 修正 |
| `Editor/SettingsSync/AddressableEditorSettingsLocator.cs` | `FindDependencyCheckSettings()` を追加 | 修正 |

### テスト（EditMode）

| 対象 | 変更内容 | 新規/修正 |
|---|---|---|
| `Tests/Editor/DependencyCheckAggregatorTests.cs` | 集約の規則（requirements 3章「走査・集約」） | 新規 |
| `Tests/Editor/DependencyCheckLogFormatterTests.cs` | 出力例との一致・表示中の行のみ・0件・サイズ表記の境界 | 新規 |
| `Tests/Editor/AddressableDependencyWindowTests.cs` | `BuildVisibleRows` の絞り込み・並べ替え | 新規 |
| `Tests/Editor/EditorTextFileWriterTests.cs` | 出力先の解決・ファイル名・連番・UTF-8・失敗時の警告 | 新規 |
| `Tests/Editor/AddressableDependencyCheckSettingsTests.cs` | 既定値・補正・SO の `OnValidate` | 新規 |
| `Tests/Editor/LeakLogWriterTests.cs` | 変更なし（委譲後も通ることで回帰を確認する） | 変更なし |

### ドキュメント

| 対象 | 変更内容 | 新規/修正 |
|---|---|---|
| `Docs/diagrams/editor-dependency-check-class.md` | クラス図・Scan のシーケンス図 | 新規 |
| `Docs/diagrams/index.md` | 上の図を登録 | 修正 |
| `Docs/diagrams/editor-foundation-class.md` | `EditorTextFileWriter` と `LeakLogWriter` からの委譲を追記 | 修正 |
| `Docs/specs/editor-dependency-check-spec.md` | 実装状況・2.3（列幅の変更）・4.1（`BuildVisibleRows`・`ScanResult`）・4.4（テスト）・図リンク | 修正 |
| `Docs/specs/editor-debug-window-spec.md` | `LeakLogWriter` の `EditorTextFileWriter` への委譲を追記 | 修正 |

## 3. データ構造の変更

- `AddressableEditorSettingsSO` に `AddressableDependencyCheckSettings _dependencyCheck`（出力先フォルダ1項目）を追加
- マイグレーション：不要。既存の SO アセットは、`OnValidate` で `_dependencyCheck` が `new` され既定値が補われる
- 走査結果・列幅はウィンドウのシリアライズ（Editor のレイアウト）に入る。ファイルへの保存はしない

## 4. 影響範囲の分析

- 既存機能への影響：「リーク」タブの手動出力・Play 終了時の自動出力が、`EditorTextFileWriter` を経由するようになる（出力先の解決・ファイル名・連番・文言は同じ）。Runtime・本番ビルドへの影響はなし
- 永続的ドキュメント（Docs/）への影響：2章「ドキュメント」の表のとおり。設計仕様書 3.9(4)・6章は、前回の steering（`20261015-editor-dependency-check-docs`）で記載済みのため変更なし
- テストへの影響：新規5ファイルを追加。既存の `LeakLogWriterTests` は無修正で通ること

## 5. 図表

クラス図・シーケンス図は恒久的な価値があるため、`src/AddressableManager/Docs/diagrams/editor-dependency-check-class.md` に置き、インデックスへ登録済み。
