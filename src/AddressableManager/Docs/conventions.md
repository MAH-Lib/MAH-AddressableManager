# コーディング規則

> 最終更新日：2026-10-24 ／ 対象：C# / Unity 6000.3.26f1
> 本書はスタイル（書式・構造）を定める。設計方針・採用理由は `addressable-manager-design.md` を参照
> 用語の語彙（禁止用語・動詞の使い分け）は `Docs/glossary.md` が正。本書は命名の**パターン**のみを定める

## 本書と設計仕様書の役割分担

本書が定めるのは**命名の「パターン」**であり、今後も守り続ける規則として扱う。一方、設計仕様書（`addressable-manager-design.md`）に書かれている**個々のクラス名・どの機能にどのクラスを置くか**は、フォルダ構成を説明するための暫定案であり、実装時に本書のパターンに沿ってさえいれば変更してよい。

| 文書 | 定めるもの | 変更の重み |
|---|---|---|
| 本書（conventions.md） | 命名パターン・namespace構成方針 | 規則。変更には合意が必要 |
| addressable-manager-design.md | 現時点の設計案・具体的なクラス名 | 暫定案。実装時に都度変更してよい |

## 1. 命名規則

| 対象 | アクセス修飾子 | 命名規則 | 例 |
|---|---|---|---|
| クラス・構造体 | private / internal / public | パスカルケース | `public class PlayerHp` |
| フィールド変数 | private / protected | `_` + キャメルケース | `private int _playerHp` |
| アクセサ | internal（基本）/ public / protected | `Acc` + パスカルケース | `internal int AccPlayerHp` |
| 定数（const / static readonly） | 目的に応じて | コンスタントケース（大文字+`_`） | `public const int MAX_PLAYER_HP` |
| 関数 | 目的に応じて | パスカルケース・動詞+目的語 | `public int AddPlayerHp(int hp)` |
| ローカル変数 | — | スネークケース | `int player_hp` |

共通の注意点：
- `public`フィールドは宣言しない（クラス間の参照はアクセサを使用する）
- **利用者プロジェクトに公開する API（外部依存型のプロパティ）にも `Acc` を付ける**。例外は設けない
  - 例：`AddressableManager.AccInstance` / `AddressableManager.AccHandler` / `AssetTicket<T>.AccAsset` / `AssetKey.AccValue`
- アクセス修飾子を無条件に`public`にしない。目的に応じて使い分ける
- `a`, `b`, `c` など意味が理解できない命名をしない
- `MonoBehaviour`は必要な場合のみ継承する。目的に応じて`sealed`・`abstract`・`virtual`を適用する

## 2. アクセサの処理規則

- 基本は`internal`。別アセンブリからの参照が必要な場合のみ`public`
- 複雑なロジックは記述しない
  - Getter：生データ、コンバート処理（int→float、ディグリー角→ラジアン角）まで
  - Setter：生データ、代入データの整合性確認（`if (value < 0 || value > 360)` など）まで
- アクセサ内で関数を呼び出さない
- アクセサで加算・減算などのロジックを記述しない

## 3. 定数・マジックナンバー

- 固定値のマジックナンバーはすべて`const`・`static readonly`で定義する
- 定数名は意味の分かる命名を使用する

## 4. 継承・インターフェース

- 修飾子（`abstract` / `override` / `protected` 等）は役割に合わせて使用する。すべて`public`にしない
- 基底クラスは末尾に`Base`を付ける
- インターフェースは先頭に大文字の`I`を付け、1つのインターフェースに複数の役割を持たせない

## 5. コメント

- 細かく書く必要はないが「どのような処理をしているのか」「何に使用しているのか」は書く
- 複雑なロジックにはコメントで補足説明を入れる
- publicな関数・フィールドは`<summary>`を使用する（「どのような処理を行うのか」のみで可）
- 他人が書いたコードに修正を加える場合は「日付と変更内容」を記述する
- **Editor ツールの利用者に見える文言は、`EditorText.Get` / `Format` でキーから取る**（日本語の文字列をコードに書かない。`const string` にもしない。表示言語が変わっても追従するため）。キーは `<画面または機能>.<要素>` とし、**文の単位**で作る（語を切り出して連結しない。語順が違う言語で訳せなくなるため）。Inspector の見出し・ツールチップは `header.<SO の型名>.<propertyPath>` / `tooltip.<SO の型名>.<propertyPath>`。キーを足したら、同梱の `ja` と `en` の両方の言語ファイルに書く（欠けていると `EditorLanguageFileConsistencyTests` が失敗する）。詳細は [`specs/editor-localization-spec.md`](specs/editor-localization-spec.md) の3.5と5章。例外：Runtime の文言、利用者の操作では起きない開発者向けの例外・`Debug.Assert`

## 6. インスペクターの設定

- Hierarchy上のオブジェクト参照・変数の設定はインスペクターで行えるようにする（`GetComponent`・`Find`等は`Start`・`Awake`で予めキャッシュしておく）
- Prefab・AudioClip・Spriteなどシーン外のアセットは`Addressables`で動的に読み込む
- デモ版・開発中はヒエラルキーで設定し、リリース版ではAddressablesに変更できるように作成する

> 補足：上記は利用者側プロジェクトを含む一般ルール。本モジュール内部の `AssetLifetimeWatcher` は、任意のGameObjectに対して実行時に `AddComponent` / `TryGetComponent` する仕組みであり、シーン上で事前配線できる対象ではないため本ルールの適用外とする（設計仕様書3.8参照）。

## 7. Namespace規則

ルートは `MAH.AddressableManager`。

### 7.1 区分の判断基準

namespaceは**C#のアクセス修飾子（public / internal）ではなく、「モジュール外部（利用者プロジェクト）のコードが直接その型に依存するか」**で分岐させる。

| 区分 | 定義 | namespace |
|---|---|---|
| 外部依存型 | 利用者プロジェクトのコードが直接触れる型（ハンドラー、外部のGameObjectに付けるコンポーネント、引数・戻り値の型など） | `MAH.AddressableManager`（`Core`を含めず短くする） |
| 内部実装型 | 利用者コードからは触れず、Handler等を経由して間接的にのみ使われる型 | `MAH.AddressableManager.Core.<機能>`（どの機能にも属さない共通物は `MAH.AddressableManager.Core`。7.4参照） |

`<機能>` は `Initialize` / `Download` / `Loading` のいずれか。

### 7.2 型ごとの分類

| 区分 | 型 | namespace |
|---|---|---|
| 外部依存型 | `AddressableManager`（GameObjectにアタッチする本体） | `MAH.AddressableManager` |
| 外部依存型 | `IAddressableHandler`（初期化・ダウンロード・読み込み・解放の唯一の入口） | `MAH.AddressableManager` |
| 外部依存型 | `AssetKey` / `AssetTicket<T>` / `AssetGroupTicket` / `AssetLifetime` | `MAH.AddressableManager` |
| 外部依存型 | `AssetLoadException`（読み込み失敗の例外。利用者が `catch` する） | `MAH.AddressableManager` |
| 外部依存型 | `AddressableInitializeException`（初期化失敗の例外。利用者が `catch` する） | `MAH.AddressableManager` |
| 外部依存型 | `InitializeState`（初期化の状態。利用者が `AccInitializeState` で参照する） | `MAH.AddressableManager` |
| 外部依存型 | `AddressablePathAttribute` / `AddressableLabelAttribute` | `MAH.AddressableManager.Attribute` |
| 内部実装型 | `InitializeController` / `DownloadController` / `LoadingController` | `MAH.AddressableManager.Core.<機能>` |
| 内部実装型 | `AssetEntryModel` / `AssetRegistryModel` | `MAH.AddressableManager.Core.Loading` |
| 内部実装型 | `InitializeStateModel` | `MAH.AddressableManager.Core.Initialize` |
| 外部依存型 | `DownloadPhase` / `DownloadStatus` / `DownloadCheckResult`（ダウンロードの段階・状態・確認結果。利用者が戻り値・引数・`AccDownloadStatus` で触れる） | `MAH.AddressableManager` |
| 外部依存型 | `AddressableDownloadException`（ダウンロード失敗の例外。利用者が `catch` する） | `MAH.AddressableManager` |
| 内部実装型 | `DownloadStatusModel` | `MAH.AddressableManager.Core.Download` |
| 内部実装型 | `IAddressablesDownloadService` とその実装（`AddressablesDownloadService`）・`DownloadSizeInfo` | `MAH.AddressableManager.Core.Download` |
| 内部実装型 | `IAddressablesInitializeService` とその実装（`AddressablesInitializeService`）・`CatalogCacheBustingService` | `MAH.AddressableManager.Core.Initialize` |
| 内部実装型 | 機能ごとの設定用 enum（`InitializeMode` / `LoadBeforeInitializePolicy` / `CatalogUpdatePolicy` 等。SO の設定でしか使わない） | `MAH.AddressableManager.Core.<機能>` |
| 内部実装型 | `AssetEntryKey`（Registry の判定キー） | `MAH.AddressableManager.Core.Loading` |
| 内部実装型 | `ILabelLocationService` とその実装（`LabelLocationService`）・`LabelLocation` | `MAH.AddressableManager.Core.Loading` |
| 内部実装型 | `AddressableLoaderBase` とその派生（`AssetLoaderService<T>`） | `MAH.AddressableManager.Core.Loading` |
| 内部実装型 | `IAssetLoaderWorker` / `IAssetLoaderServiceFactory` | `MAH.AddressableManager.Core.Loading` |
| 内部実装型 | `IAssetLifetimeWatcherBinder` とその実装 | `MAH.AddressableManager.Core.Loading` |
| 内部実装型 | `AddressableLogger`（どの機能にも属さない共通物） | `MAH.AddressableManager.Core` |
| 内部実装型 | `IInitializationGate`（Initialize と Loading の橋渡し。どの機能にも属さない共通物） | `MAH.AddressableManager.Core` |
| 内部実装型 | `AddressableHandler`（実装クラス。インターフェースのみ外部依存型）。複数機能の Controller を知るため機能フォルダの外（`Runtime/Core/Handler/`）に置く | `MAH.AddressableManager.Core` |
| 内部実装型 | `AssetLifetimeWatcher` | `MAH.AddressableManager.Core.Loading` |
| 内部実装型 | 実行時設定の SO（`AddressableRuntimeSettingsSO`） | `MAH.AddressableManager.Core` |
| 内部実装型 | 機能ごとの設定クラス（`AddressableLoadingSettings` 等。SO ではない `[Serializable]` クラス） | `MAH.AddressableManager.Core.<機能>` |
| 内部実装型 | エディタ専用設定の SO（`AddressableEditorSettingsSO`） | `MAH.AddressableManager.Editor.SettingsSync` |
| 内部実装型 | エディタ専用の設定クラス（`AddressableDebugWindowSettings`・`AddressableLeakLogSettings`・`AddressableDependencyCheckSettings`・`AddressableRegistrationSettings`。SO ではない `[Serializable]` クラスで、Editor SO が保持する） | `MAH.AddressableManager.Editor.SettingsSync` |
| 内部実装型 | 自動登録のルールの SO（`AddressableAutoRegistrationRuleSO`。利用者が何個でも作るデータで、設定 SO ではない。設計仕様書 3.1.1）と、アセットの登録の部品（判定・マクロの展開・計画・書き込み・ウィンドウ） | `MAH.AddressableManager.Editor.Registration` |
| 内部実装型 | `IAddressableDebugReader` とその実装（`AddressableDebugReader`）。Editor の読み取り専用の窓口（`#if UNITY_EDITOR`）。複数機能の Model を知るため機能フォルダの外（`Runtime/Core/DebugReader/`）に置く（7.4） | `MAH.AddressableManager.Core` |
| 内部実装型 | `<対象>Snapshot`（Model の状態を Editor に渡す取得時点の値のコピー。`readonly struct`、`#if UNITY_EDITOR`）。作る Model と同じ機能フォルダに置く | `MAH.AddressableManager.Core.<機能>` |

設定 SO は Inspector 上で `CreateAssetMenu` から作成・アサインするが、実装者がコード上で型を直接参照することは基本的にないため内部実装型として扱う。設定 SO は機能ごとに分けず、Runtime／Editor の2つにまとめる（設計仕様書3.1.1）。自動登録のルールの SO も、利用者は Inspector で作って書くだけでコードから参照しないため、内部実装型とする。

### 7.3 Editor・Tests

| 対象 | namespace |
|---|---|
| EditorWindow系（`AddressableDebugWindow`・タブ `<対象>Tab`・`IDebugWindowTab`、`AddressableDependencyWindow` と走査・集約・出力の部品） | `MAH.AddressableManager.Editor.Window` |
| PropertyDrawer等の属性拡張系 | `MAH.AddressableManager.Editor.Attribute` |
| Editor 設定（`AddressableEditorSettingsSO`・機能ごとの設定クラス・`AddressableEditorSettingsLocator`） | `MAH.AddressableManager.Editor.SettingsSync` |
| アセットの登録（自動登録のルールの SO・AssetPostprocessor・ルールの一覧と手動一括登録のウィンドウ・ルールの SO の Inspector・判定・マクロの展開・計画・`AddressableAssetSettings` への書き込み） | `MAH.AddressableManager.Editor.Registration` |
| 表示言語の切り替え（`EditorText`・言語ファイルの読み込み・Preferences・雛形の書き出し・Inspector の描画部品 `LocalizedPropertyView`） | `MAH.AddressableManager.Editor.Localization` |
| EditModeテスト | `MAH.AddressableManager.Tests.Editor` |
| PlayModeテスト | `MAH.AddressableManager.Tests.Runtime` |

`Runtime/Attribute/`（属性クラス本体）と `Editor/Attribute/`（PropertyDrawer）は同名フォルダだが別namespace。属性クラスは `AddressableAssetSettings` を参照できないためランタイム側、Drawerはエディタ専用アセンブリからしか参照できないためEditor側に置く（設計仕様書3.9(1)）。

### 7.4 フォルダとの対応関係

- **機能単位（Initialize / Download / Loading）ではフォルダとnamespaceが対応する**。`Core/Loading/` のファイルに `using MAH.AddressableManager.Core.Download;` が現れたら設計違反と機械的に判定できる
- **外部依存/内部実装の単位では対応しない**。外部依存型は `Runtime/Core/` フォルダに置かれていてもnamespaceに`Core`を含めない（例：`AddressableManager.cs`）
- **どの機能にも属さない内部実装型**（`AddressableLogger` 等）は `Runtime/Core/` 直下に置き、namespace は `MAH.AddressableManager.Core` とする。機能フォルダの型からは参照してよいが、この型から機能フォルダの型を参照してはならない
- **複数機能の Model・Controller を知る窓口は、機能フォルダの外に置く**。`AddressableHandler`（`Runtime/Core/Handler/`）と `AddressableDebugReader`（`Runtime/Core/DebugReader/`）がこれにあたり、どちらも `Core/` 直下の共通物から機能フォルダの型を参照してよい例外である（機能フォルダ同士の相互参照ではなく、合成ルートである Manager が機能をつなぐのと同じ位置づけ）。この例外は上記2つに限り、窓口以外の `Core/` 直下の型が機能フォルダの型を参照してはならない
- `Runtime/` はnamespaceに含めない

## 8. Path規則（エディタ上に表示されるパス）

- ScriptableObjectの `CreateAssetMenu` パスは `MAH/Addressable/<設定名>` とする
  - 例：`MAH/Addressable/RuntimeSettings`、`MAH/Addressable/EditorSettings`、`MAH/Addressable/AutoRegistrationRule`（自動登録のルールの SO）
- Shader規則：**本モジュールには該当なし**（Shaderを保有しないため）

## 9. Addressable規則

**本モジュールには該当なし。** 本モジュールはアセットを配信する側ではなく、Addressablesの読み込み〜解放を管理するモジュールであるため、アドレス命名・グループ・ラベルの運用ルールは利用者側プロジェクトの規則に従う。

## 10. レイヤー別命名規則

プロジェクト標準の7レイヤー（Controller / Model / View / Service / Data / Definition / Logic）を軸とし、本モジュール固有のレイヤーを加える。

| レイヤー | 命名パターン | 例 |
|---|---|---|
| Controller | `<機能>Controller` | `LoadingController` |
| Model | `<対象>Model` | `AssetEntryModel`, `AssetRegistryModel`, `InitializeStateModel`, `DownloadStatusModel`, `LeakRegistryModel`・`OwnerReclaimRegistryModel`（Editor 限定。Model 全体が `#if UNITY_EDITOR`） |
| View | MonoBehaviour。`<対象>View` が基本だが既存名も許容 | `AssetLifetimeWatcher` |
| Service | `<対象>Service`（基底クラスは末尾`Base`。4章の規則に従う） | 基底：`AddressableLoaderBase` ／ 派生：`AssetLoaderService` ／ `LabelLocationService`（Label のロケーション解決） ／ `AddressablesInitializeService`・`CatalogCacheBustingService`（初期化） ／ `AddressablesDownloadService`（ダウンロード） |
| Data | 設定 SO：`Addressable<Runtime/Editor>SettingsSO`／機能ごとの設定クラス：`Addressable<機能>Settings`／利用者が何個でも作るルールの SO：`Addressable<対象>RuleSO` | `AddressableRuntimeSettingsSO`, `AddressableLoadingSettings`, `AddressableInitializeSettings`, `AddressableDownloadSettings`, `AddressableDebugWindowSettings`, `AddressableLeakLogSettings`, `AddressableDependencyCheckSettings`, `AddressableRegistrationSettings` ／ ルールの SO：`AddressableAutoRegistrationRuleSO` |
| Definition | 名詞そのまま（enum・ロジックを持たない箱系）。Editor に渡す取得時点の値のコピーは `<対象>Snapshot` | `AssetKey`, `AssetTicket<T>`, `AssetGroupTicket`, `AssetEntryKey`, `LabelLocation`, `AssetLifetime`, `LifetimeKind`, `InitializeState`, `InitializeMode`, `LoadBeforeInitializePolicy`, `CatalogUpdatePolicy`, `DownloadPhase`, `DownloadStatus`, `DownloadCheckResult`, `OwnerReclaimRoute`（Editor 限定の enum） ／ Snapshot：`AssetEntrySnapshot`, `ScopeCountSnapshot`, `IssuedTicketSnapshot`（機能1で追加）, `LeakSnapshot`（機能2で追加）, `OwnerWatchSnapshot`・`OwnerReclaimSnapshot`（機能3で追加。Play 中の状態を窓口から渡す機能を足すときも同じ命名にする） ／ Editor の静的な走査の入力・結果：`DependencyCheckInput`, `DependencyCheckRow`, `DependencyCheckReferrer`（後の2つはウィンドウで保持するため `[Serializable]` のクラス） ／ アセットの登録の入力・計画：`RegistrationPlan` など `Registration<対象>`（暫定） |
| Logic | 状態を持たない純粋ロジック・Stateパターン。Editor では、解析・組み立て・書き込み・アセットの静的な走査・集約を `<対象>Parser` / `<対象>Formatter` / `<対象>Writer` / `<対象>Scanner` / `<対象>Aggregator` とする（`static`）。アセットの登録では、当たり判定・テンプレートの展開・差分の計画の作成を `<対象>Matcher` / `<対象>Expander` / `<対象>PlanBuilder` とする（`static`） | `AsyncCompat` ／ Editor：`StackTraceParser`（スタックトレースの解析）, `LeakLogFormatter`・`DependencyCheckLogFormatter`（ログの文字列の組み立て）, `LeakLogWriter`・`EditorTextFileWriter`（ログの書き込み）, `DependencyCheckScanner`（Addressables の登録済みアセットの依存の走査）, `DependencyCheckAggregator`（走査結果から一覧の行を作る）, `EditorProjectPath`（プロジェクトのパスの解決） ／ アセットの登録（暫定）：`RegistrationRuleMatcher`（ルールの当たり判定・競合の検出）, `RegistrationMacroExpander`（マクロの展開）, `RegistrationPlanBuilder`（差分の計画の作成）, `RegistrationRuleScanner`（ルールの SO と対象のアセットを集める）, `RegistrationEntryWriter`（`AddressableAssetSettings` への書き込み。**書き込むのはここだけ**）, `RegistrationLogFormatter` |
| Handler（モジュール固有） | `<対象>Handler` | `AddressableHandler` |
| DebugReader（モジュール固有） | `<対象>DebugReader`（インターフェースは `I<対象>DebugReader`） | `AddressableDebugReader` |
| Window（Unity技術分類） | `<対象>Window` | `AddressableDebugWindow`, `AddressableDependencyWindow`, `AddressableRegistrationRuleWindow`・`AddressableManualRegistrationWindow`（暫定） |
| Postprocessor（Unity技術分類） | `<対象>Postprocessor`（`AssetPostprocessor` 派生） | `AutoRegistrationPostprocessor`（暫定） |
| CustomEditor（Unity技術分類） | `<対象>Editor`（`UnityEditor.Editor` 派生。`<対象>` は Inspector を描く型の名前から `SO` を除いたもの） | `AddressableAutoRegistrationRuleEditor`（暫定） |
| Dropdown（Unity技術分類） | `<対象>Dropdown`（`AdvancedDropdown` 派生） | `AddressablePathDropdown`, `AddressableLabelDropdown`, `RegistrationExtensionDropdown`（暫定） |
| Tab（Unity技術分類） | `<対象>Tab`（デバッグウィンドウ内のタブ。インターフェースは `IDebugWindowTab`） | `RuntimeStateTab`, `LeakListTab`, `OwnerWatcherTab` |
| Editor の描画部品 | `<対象>View`（Editor 内で IMGUI を描く部品。**MonoBehaviour ではない**。複数のタブが共通で使う） | `StackTraceView` |
| Exporter（Editor） | `<対象>Exporter`（`[InitializeOnLoad]` の `static` クラスで、Editor のイベントに応じて、状態をファイルに書き出す。Window には依存しない） | `LeakLogAutoExporter` |
| Attribute（Unity技術分類） | `<対象>Attribute` | `AddressablePathAttribute` |
| Drawer（Unity技術分類） | `<対象>Drawer` | `AddressablePathDrawer` |

各レイヤーの定義：
- **Definition**：列挙型やデータクラスなど、ロジックを持たない箱系
- **Logic**：純粋なロジック。他の参照や変数を保持しない（どのクラスでも使用する共通変数値は例外）。Stateパターン等もここ
- **Handler**：状態を持たず、Controllerへの転送に徹する薄い窓口役。7レイヤーのどれとも性質が異なるため独自レイヤーとする
- **DebugReader**：Editor（デバッグウィンドウ）が実行中の状態を**読み取り専用**で見るための窓口。Model を読み、取得時点の値のコピー（`<対象>Snapshot`）を渡すだけで、書き換えない。Handler が「操作の窓口」であるのに対し、DebugReader は読むだけの窓口であることを名前で区別する（`Handler` と付けない）。全体が `#if UNITY_EDITOR` で囲まれる
- **Tab**：`AddressableDebugWindow` の中に並ぶタブ1つ分。自分の `Snapshot` の `List` と描画だけを持ち、窓口への参照は `Refresh` の間だけ使う（保持しない）。更新間隔・未起動時の表示は Window が持つ（詳細：`specs/editor-debug-window-spec.md` 4.4）

注意点：
- **`ScriptableObject` を継承するクラスは、末尾に `SO` を付ける**（Inspector や参照渡しの際に `ScriptableObject` だとひと目でわかるようにするため）。SO ではない `[Serializable]` クラス（機能ごとの設定クラス等）には付けない。`CreateAssetMenu` の `menuName`／`fileName` はクラス名と独立しており、`SO` を含めない（例：クラス名は `AddressableRuntimeSettingsSO` だが `fileName` は `AddressableRuntimeSettings`）
- `<対象>Snapshot` は、Model の状態を取得した時点でコピーした値であることを名前で示す（Model への参照や `UnityEngine.Object` を持たない `readonly struct`）。`DownloadStatus` と同じ **Definition** に分類し、`<対象>Model` や `<対象>Info` にはしない
- `AssetLifetimeWatcher` は MonoBehaviour であるため **View** に分類する（Watcherという独立レイヤーは設けない）
- Editor の `StackTraceView` は、`View` と付くが **MonoBehaviour ではない**（IMGUI を描く、タブ共通の部品）。上の「View」（MonoBehaviour）とは別物として、「Editor の描画部品」に分類する。MonoBehaviour を継承してよいのは Manager / Editor / View のみ、という規則（CLAUDE.md）には、影響しない
- `AssetGroupTicket` は、取り出し結果のキャッシュと「解放済み」の印という状態を持つが、利用者が受け取る `AssetTicket<T>` の対となる型であるため、`AssetTicket<T>` と同じ **Definition** に分類する（`<対象>Model` にはしない）
- **本章が定めるのはレイヤー単位のファイル名パターン**であり、**機能単位（Initialize / Download / Loading）のフォルダ分けは設計仕様書4章が定める**別軸である。両者は独立しており、`LoadingController` は「機能＝Loading」×「レイヤー＝Controller」の組み合わせで決まる
- 設計仕様書側は「なぜその層が存在するか」の説明を担当し、命名パターンは本章に一本化する
