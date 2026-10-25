# クラス図：Editor Dependency Check（未登録の依存の確認）

> 最終更新日：2026-10-15（直接／経由の区別・「複数グループ」への言い換えを反映）
> 仕様は [`Docs/specs/editor-dependency-check-spec.md`](../specs/editor-dependency-check-spec.md) を参照。実コードのメンバー名で描いている。
> デバッグウィンドウの共通基盤（窓口 `IAddressableDebugReader`・Snapshot・pull 更新）は使わない。窓口・`AddressableManager` には触れない（Runtime のログ出力 `AddressableLogger` だけを使う）。

## 1. クラス図

`AddressableDependencyWindow` が、走査（`DependencyCheckScanner`）→ 集約（`DependencyCheckAggregator`）→ 表示・絞り込み・出力を順に呼ぶ。Unity の API に触れるのは、`DependencyCheckScanner` とウィンドウだけで、集約と整形はパスと型名の文字列だけで動く（EditMode テストで偽の入力を使える）。

```mermaid
classDiagram
    class AddressableDependencyWindow {
        <<EditorWindow, internal>>
        -_rows : List~DependencyCheckRow~
        -_columnWidths : float[]
        +BuildVisibleRows(rows, selected_types, name_filter, group_filter, show_hidden, sort_by_size, result)$
        -Scan()
        -ExportText()
    }
    class DependencyCheckScanner {
        <<static>>
        +TryScan(out input) ScanResult
    }
    class DependencyCheckInput {
        <<Definition, Unity 非依存>>
        +AccParents
        +AccDirectDependencies
        +AccRegisteredPaths
        +AccTypeNames
        +AccSizes
        +AccPackSeparatelyGroups
    }
    class DependencyCheckAggregator {
        <<static, Unity 非依存>>
        +Aggregate(input, result)
    }
    class DependencyCheckRow {
        <<Serializable>>
        +AccPath
        +AccTypeName
        +AccSizeBytes
        +AccReferrers
        +AccGroupCount
        +AccIsMultiGroup
        +AccIsResources
        +AccIsPackSeparately
        +AccIsHiddenByDefault
    }
    class DependencyCheckReferrer {
        <<Serializable>>
        +AccPath
        +AccGroupName
        +AccViaPath
        +AccViaCount
        +AccIsVia
    }
    class DependencyCheckLogFormatter {
        <<static, Unity 非依存>>
        +Format(exported_at, rows, visible, filter_summary)
        +FormatVia(referrer)
        +FormatSize(bytes)
    }
    class EditorTextFileWriter {
        <<static>>
        +ResolveFolder(project_root, output_folder, default_folder)
        +WriteTimestamped(text, folder, prefix, now, failure_label)
    }
    class LeakLogWriter {
        <<static>>
    }
    class AddressableEditorSettingsLocator {
        <<static, InitializeOnLoad>>
        +FindDependencyCheckSettings()
    }
    class AddressableEditorSettingsSO {
        +AccDependencyCheck
    }
    class AddressableDependencyCheckSettings {
        <<Serializable>>
        +AccOutputFolder
    }

    AddressableDependencyWindow --> DependencyCheckScanner : 走査
    AddressableDependencyWindow --> DependencyCheckAggregator : 行の作成
    AddressableDependencyWindow --> DependencyCheckLogFormatter : テキスト出力・サイズ表記
    AddressableDependencyWindow --> EditorTextFileWriter : テキスト出力
    AddressableDependencyWindow --> AddressableEditorSettingsLocator : 出力先の設定を探す
    AddressableDependencyWindow o-- DependencyCheckRow : 結果を保持
    DependencyCheckScanner --> DependencyCheckInput : 作る
    DependencyCheckAggregator --> DependencyCheckInput : 読む
    DependencyCheckAggregator --> DependencyCheckRow : 作る
    DependencyCheckRow *-- DependencyCheckReferrer
    DependencyCheckLogFormatter --> DependencyCheckRow : 読む
    LeakLogWriter --> EditorTextFileWriter : 手動の出力を委譲
    AddressableEditorSettingsLocator --> AddressableEditorSettingsSO : 探索
    AddressableEditorSettingsSO *-- AddressableDependencyCheckSettings
```

## 2. Scan の流れ

```mermaid
sequenceDiagram
    actor User
    participant W as AddressableDependencyWindow
    participant S as DependencyCheckScanner
    participant AA as AddressableAssetSettings / AssetDatabase
    participant Ag as DependencyCheckAggregator

    User->>W: Scan
    W->>S: TryScan(out input)
    S->>AA: Settings 取得
    alt Settings が null
        S-->>W: NoSettings
        W-->>User: 「Addressables が未設定です」
    else 設定あり
        S->>AA: 全グループ GatherAllAssets（Built In Data 除外・Pack Separately を控える）
        S->>AA: SpriteAtlas の packables を「登録済み」に追加
        loop 親ごと
            S->>S: DisplayCancelableProgressBar
            alt キャンセル
                S-->>W: Canceled（finally で ClearProgressBar。一覧は前回のまま）
            else 続行
                loop 未読のアセット（幅優先。読み済みは飛ばす）
                    S->>AA: GetDependencies(path, recursive: false)・未登録なら型名・サイズ
                    S->>S: AccDirectDependencies に直接の依存を記録
                end
            end
        end
        S-->>W: Completed, input（finally で ClearProgressBar）
        W->>Ag: Aggregate(input, rows)
        loop 親ごと
            Ag->>Ag: 未登録のアセットだけを通って届く子（直接）と、最初に出会う登録済みのアセット（経由先）を集める
            Ag->>Ag: 経由先から届く子のうち、直接でないものを経由の参照元にする（経由先ごとの結果は使い回す）
        end
        W->>W: 走査日時を記録・絞り込みの選択肢と表示行・件数・高さを作り直す
        W-->>User: 一覧を表示
    end

    User->>W: テキスト出力
    W->>W: DependencyCheckLogFormatter.Format(表示中の行)
    W->>W: EditorTextFileWriter.WriteTimestamped（失敗時は警告・null）
```
