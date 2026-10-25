# 設計：AddressableManager 読み込み・解放機能

> 対象：`requirements.md`（承認済み版） ／ 作成日：2026-09-24 ／ ステータス：承認待ち

## 1. 実装アプローチ

`AddressableManager`（Manager）→`AssetHandler`（Handler）→`LoadingController`（Controller）→`AssetLoaderService<T>`（Service）の一方向構成とし、Controller だけが Registry（Model）と Service の両方を知る。詳細な手順・サンプルコードは仕様書側を正とし、本書には設計判断の要点のみ書く（詳細：`src/AddressableManager/Assets/Docs/specs/loading-spec.md`）。

主な設計判断：
- **戻り値は `AssetTicket<T>`**：チケットにアセット本体を持たせ、1回の `LoadAsync` でアセットとチケットの両方を返す（読み込んだアセットを別途取得する API を作らない）
- **チケットは通し番号で一意にする**：key・寿命スコープだけでは同じ組み合わせのチケットが複数枚できてしまい、二重解放を防げないため
- **同じキーへの型違いの要求は例外にする**：`AssetKey` に型を持たせる案は仕様変更が大きいため採用せず、Controller 側の検証で防ぐ
- **Service は string キーのみを受け取る**：`AssetReference` は `AssetKey` を作る時点で文字列化し、Service に入口の違いを持ち込まない。派生 Service を複数作らずに済む
- **Manager は `InitializeAsync` を待たずに `AccAssets` を公開する**：Addressables は最初のロード時に自動初期化されるため。未初期化判定は Download 側実装時に追加する `AccDownloads` にのみ適用する
- **Controller は Unity API を直接呼ばない**：`SceneManager.sceneUnloaded` の購読と `AddComponent` は Manager／`IAssetLifetimeWatcherBinder` に委譲し、Controller をテスト可能なピュア C# に保つ

検討した代替案と却下理由（詳細は `loading-spec.md` および `addressable-manager-design.md` 5.3参照）：
- `AssetKey` に型を含める案 → 仕様変更が大きく、誤用の早期検出という目的には Controller 側の型チェックで十分なため却下
- `AddressableMultiLoaderBase<T>` の移植 → 複数キーのロード中／ロード済み管理が Controller の重複判定・RefCount と役割が重なるため却下（一括ロードは `LoadAsync` を複数回呼ぶ形で代替）
- Manager に static `Instance` を持たせない案 → 利用者の取得手段を用意する必要があり、DI を導入しない汎用モジュール方針と合わせて `AccInstance` を採用

## 2. 変更するコンポーネント

配置先は `src/AddressableManager/Assets/@MAH/AddressableManager/` を起点とする。

| 対象（クラス/ファイル） | 変更内容 | 新規/修正 |
|---|---|---|
| `Runtime/Core/AssetKey.cs` | 入口の種別＋値で同一判定する複合キー | 新規 |
| `Runtime/Core/AssetTicket.cs`（`AssetTicket<T>`） | 通し番号で一意になる解放用チケット。読み込んだアセットを持つ | 新規 |
| `Runtime/Core/AssetLoadException.cs` | リトライを使い切った際の例外 | 新規 |
| `Runtime/Core/Logger/AddressableLogger.cs` / `AddressableLogLevel.cs` | 内部用ロガー（`Debug.Log` を包む）。デバッグ用途のため `Core/` 直下ではなく `Logger/` サブフォルダに置く | 新規 |
| `Runtime/Core/AsyncCompat.cs` | Task/UniTask の差を吸収するヘルパー（`WithCancellation`／`ToTask`／`Delay`） | 新規 |
| `Runtime/Core/Interfaces/IAssetHandler.cs` | 利用者向け読み込み・解放インターフェース | 新規 |
| `Runtime/Core/Loading/Controller/LoadingController.cs` | 重複ロード判定・RefCount・チケット発行・寿命回収の中核 | 新規 |
| `Runtime/Core/Loading/Model/AssetEntryModel.cs`／`AssetRegistryModel.cs` | 1件分の状態とその集合 | 新規 |
| `Runtime/Core/Loading/Service/IAssetLoaderWorker.cs`／`IAssetLoaderServiceFactory.cs`／`AssetLoaderServiceFactory.cs` | 実ロード担当のインターフェースと生成ファクトリ | 新規 |
| `Runtime/Core/Loading/Service/AddressableLoaderBase.cs`／`AssetLoaderService.cs` | 移植元 `AddressableLoaderBase<T>` を改修したもの。string キー・リトライ・ロギングを担当 | 新規（移植・改修） |
| `Runtime/Core/Loading/Handler/AssetHandler.cs` | `IAssetHandler` の実装。Controller への転送のみ | 新規 |
| `Runtime/Core/Loading/Lifetime/AssetLifetime.cs`／`LifetimeScopeId.cs` | 寿命の指定と内部解決用 ID | 新規 |
| `Runtime/Core/Loading/Lifetime/AssetLifetimeWatcher.cs`／`IAssetLifetimeWatcherBinder.cs`／`AssetLifetimeWatcherBinder.cs` | Owner 破棄検知の Watcher とその貼り付け窓口 | 新規 |
| `Runtime/Core/AddressableRuntimeSettingsSO.cs` | 実行時設定の SO（Manager に割り当てる唯一の設定アセット）。ログレベルと機能ごとの設定クラスを束ねる | 新規 |
| `Runtime/Core/Loading/Data/AddressableLoadingSettings.cs` | Loading 機能の設定（ポーリング・リトライ）。SO ではない `[Serializable]` クラスで、Runtime SO が保持する | 新規 |
| `Runtime/Attribute/AddressablePathAttribute.cs` | Inspector プルダウン用の属性（ランタイム側） | 新規 |
| `Editor/Attribute/AddressablePathDrawer.cs`／`AddressablePathDropdown.cs` | 属性の `PropertyDrawer` と `AdvancedDropdown` | 新規 |
| `Tests/Runtime/Data/LoadingTestKeysSO.cs` | PlayMode テスト用のアドレス定義 SO | 新規 |
| `Runtime/Core/AddressableManager.cs` | テンプレートから、生成・寿命管理・ライフサイクル中継を行う本実装へ全面書き換え | 修正 |
| `Samples/AddressableLoad/Scripts/AddressableLoaderBase.cs`／`AddressableMultiLoaderBase.cs` | 移植・改修が済み次第削除（外部依存でコンパイル不可のため） | 削除 |
| `Samples/AddressableLoad/Scripts/AssetLoadSampleViewBase.cs`／`SpriteLoadSampleView.cs`／`AudioLoadSampleView.cs`／`PrefabLoadSampleView.cs`／`SampleSceneNavigatorView.cs`／`SampleSceneNames.cs` | 手動検証用サンプル。画像・音（BGM / SE）・GameObject の3シーンで Load / Release / Scene 寿命 / Owner 寿命を試し、シーン間を遷移する（当初の `AddressableLoadSample.cs` 1本構成から変更）。寿命ごとに別のアセット（`manual_*` / `scene_*` / `owner_object_load_test`）を読み込み、見た目・音の違いでどの寿命のものが残っているか見分けられるようにした。Owner 寿命は Prefab のみ | 新規 |
| `Samples/AddressableLoad/Scenes/`（`SpriteLoadScene`／`AudioLoadScene`／`PrefabLoadScene`）・`Prefabs/AddressableManager.prefab` | 上記サンプルのシーンと、3シーンに置く Manager の Prefab。Build Settings に3シーンを登録 | 新規 |
| `Tests/Editor/`配下 | `LoadingController` の EditMode テスト一式 | 新規 |
| `Tests/Runtime/`配下 | 実際の Addressables を使う PlayMode テスト一式 | 新規 |

内部構造の詳細（`LoadingController` のフィールド、`_issuedTickets`/`_ticketsByScope` の役割など）は `loading-spec.md` 3章を参照。

## 3. データ構造の変更

新規追加のみで、既存データのマイグレーションは無い。

- `AddressableRuntimeSettingsSO`（SO）：`_logLevel`（Warning）／`_loading`（`AddressableLoadingSettings`）
- `AddressableLoadingSettings`（`[Serializable]`）：`_enableOwnerPolling`（既定 true）／`_ownerPollingIntervalSec`（3f）／`_maxRetry`（3）／`_retryDelayMs`（1000）
- 設定 SO は機能ごとに分けず Runtime／Editor の2つにまとめる（`addressable-manager-design.md` 3.1.1）。Editor SO は設定項目が必要になった時点で作る
- サンプルの既存アセット `Samples/AddressableLoad/ScriptableObjects/AddressableLoadingSettings.asset` は、`AddressableRuntimeSettings.asset` にリネームし（GUID は維持）、現在の `AddressableRuntimeSettingsSO` の入れ子レイアウト（`_loading` 配下）で書き直した。`_logLevel` は Verbose（サンプルで解放ログも見えるようにするため）、Loading の項目は既定値
- `LoadingTestKeysSO`（SO、テスト専用）：`AddressablePath` 属性付きの Sprite 用・Prefab 用アドレス

## 4. 影響範囲の分析

- **既存機能への影響**：なし。`Assembly-CSharp` に既存の実装コードは無く、`AddressableManager.cs` はテンプレートのまま、Service 移植元2ファイルはコンパイルが通っていない状態のため、既存動作を壊す変更はない
- **永続的ドキュメントへの影響**：
  - `src/AddressableManager/Assets/Docs/addressable-manager-design.md` 3.1・3.4〜3.8 を概要化し、詳細を `specs/loading-spec.md` へ切り出し済み（対応済み）
  - `src/AddressableManager/Assets/Docs/specs/loading-spec.md`／`addressable-path-attribute-spec.md` を新規作成済み（対応済み）
  - `CLAUDE.md` のドキュメント参照・置き場所ルールを更新済み（対応済み）
  - リポジトリルート `Docs/` と `Assets/Docs/` の役割分担に伴い、`README.md`／`CONTRIBUTING.md`／`Docs/glossary.md` のリンクパスを修正済み（対応済み）
- **テストへの影響**：既存テストは無い（新規作成のみ）。ユーザーが作成する asmdef に依存するため、asmdef 完成後に EditMode／PlayMode 双方のテストが実行可能になる

## 5. 図表（必要な場合）

機能個別の詳細図は `src/AddressableManager/Assets/Docs/diagrams/` に配置済み。

- クラス図：[`diagrams/loading-class.md`](../../src/AddressableManager/Assets/Docs/diagrams/loading-class.md)
- シーケンス図：[`diagrams/loading-sequence.md`](../../src/AddressableManager/Assets/Docs/diagrams/loading-sequence.md)（正常系の重複ロード共有／キャンセル・失敗／Owner 破棄による自動回収の3枚）
