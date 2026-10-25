# 機能仕様書：Editor デバッグウィンドウの共通基盤

> 最終更新日：2026-10-15 ／ 対象 Unity バージョン：6000.3.26f1 ／ 実装状況：**共通基盤と機能1「実行時状態」・機能2「リーク検出一覧」・機能3「owner Watcher の可視化」を実装済み。読み込み計測は作らない（機能4は別ウィンドウの未登録の依存の確認に差し替え）**
> **Editor 拡張のうち、デバッグウィンドウ（`AddressableDebugWindow`）の共通基盤（実行中の状態への到達手段・読み取り専用の境界・Editor 用 SO・ウィンドウの枠）の仕様はこのファイルを正とする**。全体の概要は [`addressable-manager-design.md`](../addressable-manager-design.md) 3.9、命名パターンは [`conventions.md`](../conventions.md)、用語は `Docs/glossary.md`（リポジトリルート）が正。
> 図は [`diagrams/editor-foundation-class.md`](../diagrams/editor-foundation-class.md) を参照。
>
> 本書は、デバッグウィンドウの共通基盤が**どう動き、どう作られているか**、そして**機能（タブ）を追加するときにどこを触ればよいか**を、後から読む開発者が把握するための資料である。**各機能（実行時状態・リーク検出・owner Watcher）の節は、その機能の実装時に、本書の末尾へ追記する**。Play せずにアセットを静的に走査する機能（未登録の依存の確認）は、データの出所が違うため別のウィンドウで、[`editor-dependency-check-spec.md`](editor-dependency-check-spec.md) が正。AddressablePath / AddressableLabel のプルダウンは別の仕様書（[`addressable-path-attribute-spec.md`](addressable-path-attribute-spec.md)・[`addressable-label-attribute-spec.md`](addressable-label-attribute-spec.md)）が正で、本書の対象外。

---

## 1. 概要

Play 中の `AddressableManager` の状態（Registry・寿命・リーク・Owner の回収）を、EditorWindow 1つのタブ切り替えで見られるようにする。**どのタブも、同じ仕組みで状態を受け取る**ため、その共通部分を本書で定める。

| 部品 | 役割 |
|---|---|
| `IAddressableDebugReader` / `AddressableDebugReader` | Window が状態を読むための**読み取り専用の窓口**。Manager が生成して渡す |
| `<対象>Snapshot` | 取得した時点の値のコピー（`readonly struct`）。Model の状態を Window に渡す |
| `AddressableDebugWindow` | EditorWindow。タブの枠と、一定間隔の更新（pull）を持つ |
| `AddressableEditorSettingsSO` / `AddressableDebugWindowSettings` | Editor 専用の設定。Inspector から操作する |

**この基盤が持たないもの**：Registry や寿命の書き換え（手動 Release・強制回収のボタンは作らない。設計仕様書 3.9・5.3）、SO 設定値の一覧・検証ウィンドウ、各機能のタブの中身（機能ごとに追記する）、Runtime SO の設定。

### 1.1 全体の中での位置づけ

```
AddressableManager（合成ルート。Model を作り、Controller と窓口の両方へ注入する）
├─ AddressableHandler       … 利用者の入口（IAddressableHandler）。操作できる
├─ XxxController（3つ）     … 機能の実行。Model を書き換える
├─ XxxModel                 … 状態。窓口に読まれる
└─ AddressableDebugReader   … 本書。Model を読み取り専用で見せる（#if UNITY_EDITOR）
        ▲
        │ IAddressableDebugReader（AccInstance.AccDebugReader）
AddressableDebugWindow（Editor アセンブリ）
```

- Window は Runtime 側の型のうち、`AddressableManager.AccInstance` と `IAddressableDebugReader`・各 `Snapshot` しか知らない
- 窓口は複数機能の Model を知るため、機能フォルダの外（`Runtime/Core/DebugReader/`）に置く。`AddressableHandler` と同じ扱いで、機能フォルダ同士の相互参照にはあたらない

---

## 2. 動作の全体像

### 2.1 状態への到達

```csharp
// Manager.Awake（Registry 系の Model をローカル変数に出して、Controller と窓口の両方へ注入する）
var asset_registry = new AssetRegistryModel();
var issued_ticket_registry = new IssuedTicketRegistryModel();
var owner_watch_registry = new OwnerWatchRegistryModel();
#if UNITY_EDITOR
var leak_registry = new LeakRegistryModel(); // Editor でしか使わない Model（8.2.3）
var owner_reclaim_registry = new OwnerReclaimRegistryModel(); // 同上（8.3.3）
#endif
_loadingController = new LoadingController(..., asset_registry, issued_ticket_registry, owner_watch_registry
#if UNITY_EDITOR
    , leak_registry, owner_reclaim_registry
#endif
    );
#if UNITY_EDITOR
_debugReader = new AddressableDebugReader(asset_registry, issued_ticket_registry, leak_registry, owner_watch_registry, owner_reclaim_registry);
#endif

#if UNITY_EDITOR
internal IAddressableDebugReader AccDebugReader => _debugReader;
#endif
```

- Window は `AddressableManager.AccInstance` から `AccDebugReader` を取得する。**新しい static 状態は作らない**（`_instance` は既に Play の開始時にリセットされ、Manager の破棄で `null` に戻る）
- `AccInstance == null`（Play していない・破棄済み）のときは、Window に「未起動」と表示する。窓口への参照を Window が保持し続けない（毎回 `AccInstance` から取り直す）
- Initialize と Download の状態は、既に public の `IAddressableHandler`（`AccInitializeState`・`AccDownloadStatus`）から読めるため、窓口を介さなくてもよい
- 窓口に注入する Model は、読ませる機能が増えるたびに足す（現在は `AssetRegistryModel`・`IssuedTicketRegistryModel`・`LeakRegistryModel`・`OwnerWatchRegistryModel`・`OwnerReclaimRegistryModel`）

### 2.2 読み取り専用の保ち方

- Window は `IAddressableDebugReader` 型しか持たない。Model の書き込み API（`AddEntry` など）は、窓口のインターフェイスに出ないため、Window からは構造上呼べない
- Model 側には、`#if UNITY_EDITOR` で囲んだ internal の**コピー用メソッド**（`Snapshot` を `List<T>` へ書き出す）だけを足す。呼ぶのは `AddressableDebugReader` に限る
- Model ごとの読み取り専用インターフェイスは作らない。Window の依存を `IAddressableDebugReader` の1つにするため
- 例外として、リークの `LeakRegistryModel`（機能2）は、Editor でしか使わない状態のため、Model 全体を `#if UNITY_EDITOR` で囲む。書き込み（記録）は `LoadingController` が行い、窓口はコピー用メソッド経由で読むだけ（8.2.3）

### 2.3 スナップショットと更新（pull）

- Window は、Window 側が持つ `List<XxxSnapshot>` を窓口に渡し、窓口が Model から**コピーして**書き込む（`ToArray()` / `ToList()` で毎回確保しない。設計仕様書 5.4）
- 更新は **Window 側の一定間隔 pull** とする。Window が開いていて、Play 中のときだけ、`OnInspectorUpdate` で前回の取得からの経過時間を見て、更新間隔を超えていれば取得して `Repaint` する
- 更新間隔は `AddressableDebugWindowSettings` で設定する（既定 0.5 秒・下限 0.1 秒。下限は `OnInspectorUpdate` の約 10Hz に合わせる）
- Runtime 側にイベントを足さない。Window を閉じていれば、Runtime 側のコストはゼロ

```
Window.OnInspectorUpdate
  ├─ Play 中でない／AccInstance == null → 「未起動」を表示して終わる
  └─ 更新間隔を超えている（または、起動直後・タブの切り替え直後）
        → reader = AccInstance.AccDebugReader
        → 表示中のタブ.Refresh(reader)
              → reader.CopyXxxTo(list)  … Model → List<XxxSnapshot>（#if UNITY_EDITOR）
        → Repaint
```

- 取り直すのは**表示中のタブだけ**。別のタブへ切り替えた直後は、次の更新間隔を待たずに取り直す
- Play の終了（`ExitingPlayMode`）と未起動への遷移では、全タブの `Clear()` を呼び、保持している `Snapshot` と表示の状態を空にする（Manager の破棄後に古い値を表示し続けない）
- 同じ Play 中に Manager が作り直されたときも、全タブの `Clear()` を呼ぶ。Window は、直近に読んだ Manager の `InstanceID`（参照は持たない）を覚え、取り直しのたびに比べる。未起動を観測できないまま入れ替わると、タブが持つ Model ごとの基準（「リーク」タブのクリアの通し番号・行の ID）が、新しい Model の値とずれるため

### 2.4 ウィンドウの枠とタブ

- `AddressableDebugWindow`（`Editor/Window/`、`MAH.AddressableManager.Editor.Window`）は、ツールバーでタブを切り替える1つのウィンドウ
- 開き方は、メニュー **`MAH/Addressable/Debug Window`**。描画は IMGUI（既存の Editor コード〔Drawer・AdvancedDropdown〕と揃える）
- タブの並びは、`AddressableDebugWindow.OnEnable` の `_tabs` 配列の順。現在は「実行時状態」（`RuntimeStateTab`）・「リーク」（`LeakListTab`）・「Owner」（`OwnerWatcherTab`）の3つで、機能4以降はこの配列に足す
- 同じデータソース（Registry と寿命スコープ）を見る項目（実行時状態・リーク・owner Watcher）は、ウィンドウを分けずにタブにする（設計仕様書 3.9）。Play せずに静的に走査する機能は、別のウィンドウ（`AddressableDependencyWindow`。`editor-dependency-check-spec.md`）にする
- 各タブは `IDebugWindowTab` を実装し、「自分の `Snapshot` の `List`」と「描画」だけを持つ。到達手段・更新間隔・未起動時の表示は枠が持つ（タブごとに書かない）
- Window は、更新間隔を SO から毎回読む（SO が編集されても追従する）。SO が無いときは既定値の設定で動く。Window を開いた後に SO が作られた場合は、次の取り直しで探し直して反映する（Window の `_defaultSettings` が、既定値を決める唯一の場所）

---

## 3. 設定項目

### 3.1 構成

| 型 | 置き場所 | 役割 |
|---|---|---|
| `AddressableEditorSettingsSO` | `Editor/SettingsSync/` | Editor 専用設定の SO（`[CreateAssetMenu(menuName = "MAH/Addressable/EditorSettings")]`）。`MAH.AddressableManager.Editor.SettingsSync` |
| `AddressableDebugWindowSettings` | `Editor/SettingsSync/` | デバッグウィンドウの設定（`[Serializable]`）。SO が保持する |
| `AddressableLeakLogSettings` | `Editor/SettingsSync/` | 「リーク」タブのログ出力の設定（`[Serializable]`。機能2で追加）。SO が保持する |

- 機能ごとの設定は、Runtime SO と同じく `[Serializable]` の `Addressable<機能>Settings` として SO が保持する。**共通基盤で持つのは `AddressableDebugWindowSettings` だけ**で、機能の項目は、その機能の実装時に足す（機能2：`AddressableLeakLogSettings`。Dependency Check の出力先は `AddressableDependencyCheckSettings`〔`editor-dependency-check-spec.md` 3章〕）
- SO は Editor アセンブリにあるため、ビルドに含まれない。Runtime 側から参照しない

| 項目 | 既定 | 範囲 | 内容 |
|---|---|---|---|
| 更新間隔（秒） | 0.5 | 0.1 以上 | Window が窓口から状態を取り直す間隔 |
| ログの出力先フォルダ（`AddressableLeakLogSettings`） | `Logs/AddressableManager` | 空・空白は既定値に戻る | 「リーク」タブのログの出力先。相対パスはプロジェクトのルート（`Assets/` の親）から、絶対パスはそのまま使う（8.2.7） |
| Play 終了時に自動で出力する（`AddressableLeakLogSettings`） | OFF | ― | ON のとき、Play の終了時にリークを `AddressableLeaks_LastPlay.txt` へ出力する（8.2.7） |

### 3.2 探索と自動作成

- **探索**：配置場所はプロジェクトごとに変わり得るため、パスではなく型で `AssetDatabase.FindAssets("t:AddressableEditorSettingsSO")` を使って探す。複数件あれば、警告を出して1件目を使う
- **自動作成**：エディター起動時（`[InitializeOnLoad]`）に探索し、0件なら既定パス `Assets/MAH.Data/AddressableManager/Editor/AddressableEditorSettings.asset` に作成して、ログで知らせる。作成後は、任意の場所へ移動してよい（探索は型で行うため）
- 起動直後はインポート中で `AssetDatabase` が使えないことがあるため、作成は `EditorApplication.delayCall` で遅らせる

---

## 4. 内部構成

### 4.1 クラスと配置

| 型 | 層 | 配置 | namespace |
|---|---|---|---|
| `IAddressableDebugReader`（internal） | 窓口 | `Runtime/Core/DebugReader/` | `MAH.AddressableManager.Core` |
| `AddressableDebugReader`（internal sealed） | 窓口 | `Runtime/Core/DebugReader/` | `MAH.AddressableManager.Core` |
| `<対象>Snapshot`（readonly struct） | Definition | 作る Model と同じ機能フォルダ（例：`Core/Loading/Model/`） | `MAH.AddressableManager.Core.<機能>` |
| `AddressableDebugWindow`（internal sealed。EditorWindow） | Editor | `Editor/Window/` | `MAH.AddressableManager.Editor.Window` |
| `IDebugWindowTab`（internal interface） | Editor | `Editor/Window/` | `MAH.AddressableManager.Editor.Window` |
| `<対象>Tab`（internal sealed。例：`RuntimeStateTab`） | Editor | `Editor/Window/` | `MAH.AddressableManager.Editor.Window` |
| `AddressableEditorSettingsSO` / `AddressableDebugWindowSettings` | Data | `Editor/SettingsSync/` | `MAH.AddressableManager.Editor.SettingsSync` |
| `AddressableEditorSettingsLocator`（internal static。`[InitializeOnLoad]`） | Editor | `Editor/SettingsSync/` | `MAH.AddressableManager.Editor.SettingsSync` |
| `LeakRegistryModel`（internal sealed。`#if UNITY_EDITOR`） | Model | `Runtime/Core/Loading/Model/` | `MAH.AddressableManager.Core.Loading` |
| `LeakSnapshot`（internal readonly struct） | Definition | `Runtime/Core/Loading/Model/` | `MAH.AddressableManager.Core.Loading` |
| `LeakListTab`（internal sealed） | Editor | `Editor/Window/` | `MAH.AddressableManager.Editor.Window` |
| `StackTraceParser`（internal static）・`StackTraceView`（internal sealed）・`TraceFrame`（internal readonly struct） | Editor（2つのタブの共通部品） | `Editor/Window/` | `MAH.AddressableManager.Editor.Window` |
| `LeakLogFormatter`（internal static）・`LeakLogWriter`（internal static）・`LeakLogAutoExporter`（internal static。`[InitializeOnLoad]`） | Editor | `Editor/Window/` | `MAH.AddressableManager.Editor.Window` |
| `AddressableLeakLogSettings`（internal sealed。`[Serializable]`） | Data | `Editor/SettingsSync/` | `MAH.AddressableManager.Editor.SettingsSync` |
| `OwnerReclaimRegistryModel`（internal sealed。`#if UNITY_EDITOR`） | Model | `Runtime/Core/Loading/Model/` | `MAH.AddressableManager.Core.Loading` |
| `OwnerReclaimSnapshot`・`OwnerWatchSnapshot`（internal readonly struct） | Definition | `Runtime/Core/Loading/Model/` | `MAH.AddressableManager.Core.Loading` |
| `OwnerReclaimRoute`（internal enum。`#if UNITY_EDITOR`） | Definition | `Runtime/Core/Loading/Model/` | `MAH.AddressableManager.Core.Loading` |
| `OwnerWatcherTab`（internal sealed） | Editor | `Editor/Window/` | `MAH.AddressableManager.Editor.Window` |

- 窓口・`Snapshot`・Model のコピー用メソッドは、すべて `#if UNITY_EDITOR` で囲み、本番ビルドから除外する
- Editor アセンブリ（`MAH.AddressableManager.Editor`）から internal 型を見せるため、`Runtime/AssemblyInfo.cs` の `InternalsVisibleTo` に `MAH.AddressableManager.Editor` を追加する
- Editor の設定クラス（internal）を EditMode テストから検証できるよう、`Editor/AssemblyInfo.cs` の `InternalsVisibleTo` に `MAH.AddressableManager.Editor.Tests` を追加している（Runtime 側の `InternalsVisibleTo` とは別）

### 4.2 `<対象>Snapshot` の作り方

- `readonly struct`。取得した時点の値のコピーで、取得後に Model が変わっても値は変わらない（`DownloadStatus` と同じ考え方）
- Model の参照（`AssetEntryModel` など）や `UnityEngine.Object` を持ち続けない。表示に必要な値（キー・型名・RefCount など）を値で持つ
- アクセサは `Acc` + パスカルケース（conventions 2章）。生成時に値を計算して持たせ、アクセサの中で関数を呼ばない

### 4.3 窓口のメソッドの作り方

- 機能ごとに `Copy<対象>To(List<XxxSnapshot> list)` の形で足す。引数の `List` を `Clear()` してから書き出す（複数の `List` を連結して渡す場合は、すべて `Clear()` する。例：`CopyAssetEntriesTo(entries, scope_counts)`）
- 戻り値でコレクションを新しく作らない。Window が `List` を持ち回って再利用する
- Model を書き換えるメソッドは足さない

### 4.4 `IDebugWindowTab` の作り方

| メンバー | 役割 |
|---|---|
| `AccTitle` | ツールバーに表示するタブ名 |
| `Refresh(IAddressableDebugReader reader)` | 窓口から状態を取り直す。Window が、更新間隔ごとに**表示中のタブにだけ**呼ぶ。自分の `List` に `Snapshot` を書き出し、描画用の文字列・件数もここで作る |
| `Clear()` | 保持している `Snapshot` と表示の状態（開閉・スクロール位置など）を空にする。未起動になったときに Window が呼ぶ |
| `InvalidateLabels()` | 表示言語が変わったときに Window が**すべてのタブに**呼ぶ。`Refresh` の作り直しの省略（変化が無いときの早期 return）を無効にし、次の `Refresh` で表示用の文字列を新しい言語で作り直させる。`Snapshot`・開閉・クリアの基準は消さない（`Clear()` とは別）。毎回作り直すタブは空でよい |
| `Draw()` | `OnGUI` の中から呼ばれる。Registry を書き換える操作（ボタン等）は置かない |

- タブは、窓口への参照を**保持しない**（`Refresh` の引数で受け取って使い切る）
- `OnGUI` は1フレームに複数回呼ばれるため、`Draw` の中で文字列・`GUIContent`・`GUILayoutOption` を毎回作らない。`Refresh` で作るか、フィールドに保持して使い回す
- `EditorStyles` は `OnGUI` の中でしか使えないため、`GUIStyle` は最初の `Draw` で作る

---

## 5. 設計の判断と理由

- **窓口を Runtime 側に置く**：Editor フォルダに置くと、Runtime の Manager から生成・注入できない。Manager が Model を Editor へ公開する必要が出て、読み取り専用が運用ルール頼みになる
- **名前を Handler ではなく Reader にする**：本モジュールの Handler は「操作の窓口」を意味する。読み取り専用の窓口に付けると、操作できるように見える。揃えるのは渡し方（インターフェイス型を `Acc〜` で渡す）だけ
- **インターフェイスを `Core/Interfaces/` に置かない**：`Core` 直下の共通物から、機能フォルダのスナップショット型を参照することになるため
- **コンストラクタ注入と `AccInstance`**：設計仕様書 3.9 に並記されていた2案（static の弱参照リスト／DI コンテナ経由）は採らない。Manager がシングルトンのため「リスト」は過剰で、`AccInstance` との二重の経路になる。DI コンテナには依存しない方針のため、手動の `new` と注入で足りる
- **Model に直接読み取り API を足して Editor が Model を持つ案は採らない**：`InternalsVisibleTo` で書き込み API も見えるため
- **pull を採り、push は採らない**：push は Model・Controller に発火箇所が散らばり、購読解除の漏れも増える。Window を閉じていても Runtime のコストが残る
- **SO を型で探し、自動作成する**：配置場所がプロジェクトごとに違うため。自動作成の既定パスは `Assets/MAH.Data/AddressableManager/Editor/`
- 採用しない設計の一覧は、設計仕様書 5.3 を参照

---

## 6. 改修ガイド

### 6.1 タブ（機能）を追加したい

1. 見せたい Model の値を、`readonly struct` の `<対象>Snapshot` として、その Model と同じ機能フォルダに作る（`#if UNITY_EDITOR`）
2. Model に、`Snapshot` を `List` へ書き出す internal のコピー用メソッドを足す（`#if UNITY_EDITOR`）
3. `IAddressableDebugReader` と `AddressableDebugReader` に `Copy<対象>To` を足す
4. `IDebugWindowTab` を実装した `<対象>Tab` を `Editor/Window/` に作る（`List<XxxSnapshot>` と描画。4.4）
5. `AddressableDebugWindow.OnEnable` の `_tabs` 配列に、そのタブを足す
6. 本書の8章に、その機能の節を追記し、状態表を更新する

`RuntimeStateTab` の絞り込み・スコープの色は、`LifetimeKind` の数と名前から作る。`LifetimeKind` に種類を足したときは、`KIND_COLORS` に色を足す（足さなくても動くが、灰色で表示される）。値は 0 からの連番にすること（添字として使うため。崩れると `Debug.Assert` が出る）。

### 6.2 設定項目を追加したい

`Addressable<機能>Settings`（`[Serializable]`）を `Editor/SettingsSync/` に作り、`AddressableEditorSettingsSO` に保持させる。値の範囲は `OnValidate` で補正する（`AddressableLoadingSettings` と同じ形）。

### 6.3 更新間隔や取得の仕組みを変えたい

更新は枠（`AddressableDebugWindow`）に集約されている。タブ側に更新処理を持たせない。

### 6.4 改修時に守ること

- Model を書き換えるメソッドを窓口に足さない（手動 Release・強制回収のボタンを作らない）
- 窓口・`Snapshot`・コピー用メソッドを `#if UNITY_EDITOR` の外に出さない
- `Core/Loading/` などの機能フォルダから、他の機能フォルダや `Core/DebugReader/` を `using` しない
- 一時コレクションを都度確保しない（`List` の再利用）

---

## 7. 注意点・既知の制約

- 共通基盤の記述は、機能1の実装時に実コードと照合済み。窓口のメソッド名（`Copy<対象>To`）・タブの並び・メニューは、本書の記述が実コードと一致する。機能を足すときは、4.3・4.4・6.1 に従い、8章に節を追記する
- `InternalsVisibleTo` に Editor アセンブリを足すと、Runtime の internal 型が Editor から見えるようになる。**Editor 側のコードは、窓口と `Snapshot` 以外の internal 型（Model・Controller）を使わない**。構造では防げないため、レビューの観点にする
- `AddressableManager.AccInstance` は Play 中のみ有効で、Manager の破棄後は `null` になる。Window は窓口への参照を保持しない
- 自動作成される SO は、利用者のプロジェクトの `Assets/MAH.Data/AddressableManager/Editor/` に作られる。移動しても探索は型で行うため問題ない。ただし**複数作ると警告が出て1件目が使われる**
- 「スナップショット」は取得時点の値のコピーで、表示中に Model が変わっても追従しない。次の更新間隔で取り直される
- Unity 6000.3 では `EditorUtility.InstanceIDToObject(int)` が非推奨（`EntityId` への移行）。Runtime の寿命スコープは `InstanceID`（int）で持つため、Editor 側は `EntityId` へ暗黙変換して `EditorUtility.EntityIdToObject` で引く（8.1.5）。Runtime 側の `GetInstanceID()` は、この版では非推奨ではない
- エディタ上の算出値と実機の乖離など、各機能に固有の制約は、その機能の節に書く

---

## 8. 各機能の節（追記先）

機能ごとに、次の順で追記する（このセクションは、機能の実装時に増える）。各節は、機能1（8.1）と同じ構成（概要・画面・Snapshot と窓口・設計の判断と理由・改修ガイド・注意点・テスト）で書く。

| 機能 | 状態 |
|---|---|
| 実行時の読み込み/解放状態の確認 | 実装済み（8.1。steering：`.steering/20261014-editor-runtime-state/`） |
| リーク検出一覧 | 実装済み（8.2。steering：`.steering/20261014-editor-leak-list/`） |
| owner Watcher の可視化 | 実装済み（8.3。steering：`.steering/20261015-editor-owner-watcher/`） |
| 読み込み計測 | 作らない（2026-10-15。設計仕様書 6章）。機能4は、別ウィンドウの「未登録の依存の確認」に差し替えた（`editor-dependency-check-spec.md`） |
| EditMode でのサイズ概算 | 作らない（2026-10-15。設計仕様書 6章） |

---

### 8.1 実行時の読み込み/解放状態の確認（「実行時状態」タブ）

#### 8.1.1 概要

Registry の Entry（読み込み済みのアセット）と、発行済みで未解放のチケットを一覧し、「どのアセットが、どの寿命スコープから、何件確保されたまま残っているか」を見られるようにする。タブは `RuntimeStateTab`（`Editor/Window/`）。

| 表示項目 | 出どころ |
|---|---|
| アセットの名称（ロード完了後） | `AssetEntrySnapshot.AccAssetName`（`UnityEngine.Object.name` のコピー） |
| `AssetKey`（種別＋値）・アセットの型 | `AccKey`・`AccAssetType` |
| `RefCount` | `AccRefCount` |
| ロード状態 | `AccStatus`（8.1.3） |
| 寿命スコープごとの確保数（`CountByScope`） | `ScopeCountSnapshot` |
| チケットごとの状態・確保元のスタックトレース（表示とジャンプは、「リーク」タブと共通の部品。8.2.5） | `IssuedTicketSnapshot` |

#### 8.1.2 画面

```
エントリ 3 件 ｜ チケット 5 件                                ← 絞り込みに合わせて件数が切り替わる
[ All (3) ][ Manual (2) ][ Scene (1) ][ Owner (1) ]           ← 寿命の種類で絞り込む
▸ [型アイコン] icon_sword ｜ Path:Sprites/icon_sword <Sprite>     Ref 3   ● ロード完了
    ■ Manual ×1
        ▸ チケット #1                                          ● ロード完了
            （確保元のスタックトレース。フレームごとのリンクで該当行へジャンプでき、全文のコピーもできる。8.2.5）
    ■ Scene：GameScene ×1
    ■ Owner：Enemy_01（ID 12345） ×1
```

- **絞り込み**：All / Manual / Scene / Owner。各ボタンに、該当する Entry の数を付ける。1つの Entry が複数の寿命から確保されているときは、該当する絞り込みのすべてに現れる。絞り込み中は、選んだ寿命のスコープとチケットだけを開いたときに表示する。先頭の件数（エントリ・チケット）も、絞り込みに合わせて切り替わる
- **マーク**：型アイコン（`AssetPreview.GetMiniTypeThumbnail`。Sprite・Prefab など Unity 標準のもの）／ロード状態の ●（ロード中＝黄・ロード完了＝緑・失敗＝赤）／寿命の ■（Manual＝青・Scene＝緑・Owner＝橙）／Label 一括ロード由来のチケットの「Label 一括」バッジ。色は Dark / Light どちらのスキンでも読める中間の明るさにしている
- **名称**：ロード完了後は `名称 ｜ キー <型>` の順に出す。ロード中（名前がまだ無い）は、キーと型だけを出す。`LoadAsync<GameObject>` で読んだものは、Prefab アセットの名前（インスタンスではない）
- **並び**：Entry は、キーの種別 → 値 → 型名の順に並べる（`Dictionary` の順序が変わっても、行が入れ替わらないため）
- **開閉の状態**：Entry とチケットごとに保持する。未起動に戻ると破棄する
- 同じアセットを複数の寿命で読むと、Entry は1件で、寿命ごとの確保が内訳に並ぶ（`AssetKey` と型で同一判定するため。設計仕様書 3.6）

#### 8.1.3 「ロード中／ロード完了」の意味

Entry の行とチケットの行は、同じ言葉で表示するが、**見ているものが違う**。

| 行 | 元の値 | 表示 |
|---|---|---|
| Entry | `AsyncOperationStatus`（アセット自体のロード状態） | `None` → ロード中／`Succeeded` → ロード完了／`Failed` → 失敗 |
| チケット | `AccIsDelivered`（呼び出し元に、結果を返し終えたか） | `false` → ロード中／`true` → ロード完了 |

- 単体の `LoadAsync` では、ほぼ同時に変わる。アセットのロードが終わると、呼び出しも終わる
- **Label 一括ロードでは、ずれる**。まとめチケットは全アセットの完了後に返すため、先に読み込み終えたアセットのチケットも、全体が終わるまでは「ロード中」になる
- チケットの「ロード中」は、リーク検出（機能2）で使う値と同じ。ロード中に寿命が終わった分は、呼び出し元が `Release` を呼びようがないため、リーク（呼び忘れ）として警告しない（loading-spec 10章）
- ロードに失敗した Entry は、Registry から取り除かれるため、通常は「失敗」の行は見えない

#### 8.1.4 `Snapshot` と窓口のメソッド

窓口 `IAddressableDebugReader` に、次の2つを足している。

| メソッド | 書き出す先 | 内容 |
|---|---|---|
| `CopyAssetEntriesTo(List<AssetEntrySnapshot> entries, List<ScopeCountSnapshot> scope_counts)` | 両方を `Clear()` してから書き出す | Registry の全 Entry と、寿命スコープごとの確保内訳 |
| `CopyIssuedTicketsTo(List<IssuedTicketSnapshot> tickets)` | `Clear()` してから書き出す | 発行済みで、まだ解放されていないチケット |

| `Snapshot`（`Core/Loading/Model/`） | 値 |
|---|---|
| `AssetEntrySnapshot` | `AccKey`・`AccAssetType`・`AccAssetName`（ロード完了前・破棄済みは空文字）・`AccRefCount`・`AccStatus`・`AccScopeCountStart`・`AccScopeCountLength` |
| `ScopeCountSnapshot` | `AccScopeKind`（`LifetimeKind`）・`AccScopeValue`（Owner は InstanceID、Scene は handle、Manual は 0）・`AccCount` |
| `IssuedTicketSnapshot` | `AccSerial`・`AccKey`・`AccAssetType`・`AccScopeKind`・`AccScopeValue`・`AccIsDelivered`・`AccIsGroup`（Label 一括由来）・`AccStackTrace` |

- **寿命スコープごとの内訳（`CountByScope`）は、全 Entry 分を1つの `List<ScopeCountSnapshot>` に連結して書き出す**。各 `AssetEntrySnapshot` は、その中の開始位置（`AccScopeCountStart`）と件数（`AccScopeCountLength`）だけを持つ。`readonly struct` に `Dictionary` や配列を持たせず、Entry ごとの配列確保も避けるため
- **Entry とチケットの対応は、`AssetKey`・型・スコープ（種別と値）の一致で、Editor 側が付ける**。internal の `AssetEntryKey` は Editor に見せない
- Model 側のコピー用メソッド（すべて `#if UNITY_EDITOR`）：`AssetRegistryModel.CopyEntriesTo`・`AssetEntryModel.CopyScopeCountsTo`（追記のみ）・`IssuedTicketRegistryModel.CopyTicketsTo`。`LifetimeScopeId` に `AccKind`・`AccValue` を足している（等価判定は変えていない）
- アセットの名前は、`AssetRegistryModel.CopyEntriesTo` が Worker の `AccAsset` から文字列でコピーする。`UnityEngine.Object` は `Snapshot` に持たせない

#### 8.1.5 スコープ名の解決（Editor 側）

寿命スコープの値を、人が読める名前にする処理は、Runtime ではなくタブ（Editor 側）で行う。Runtime の `Snapshot` に `UnityEngine.Object` を持たせないため。解決は、取得時（`Refresh`）に1回だけ行い、結果の文字列を描画で使い回す。

| 種別 | 表示 | 解決の方法 |
|---|---|---|
| Manual | `Manual` | 固定 |
| Scene | `Scene：<シーン名>` | 読み込み中のシーンのうち、`handle` が一致するもの。無ければ `Scene：破棄済み（handle n）` |
| Owner | `Owner：<GameObject名>（ID n）` | `InstanceID` を `EntityId` に暗黙変換し、`EditorUtility.EntityIdToObject` で引く。無ければ `Owner：破棄済み（ID n）` |

#### 8.1.6 設計の判断と理由

- **表示用の文字列・件数を、取得時に作る**：`OnGUI` は1フレームに複数回呼ばれ、描画のたびに文字列を作ると GC が増える。取得は更新間隔（既定 0.5 秒）ごとなので、そこで作る。`GUIContent`・`GUILayoutOption` も使い回す
- **取り直すのは表示中のタブだけ**：見えていないタブの取得とラベル生成を省くため。タブを切り替えた直後は、その場で取り直す
- **絞り込みの件数を、取得時に集計する**：Entry ごとに「持っている寿命の種類」をビット和で持ち、描画時の判定と件数の集計を、整数の演算だけで済ませる
- **ロード中／ロード完了の言葉を、Entry とチケットで揃える**：画面上の見分けやすさを優先した。意味の違いは 8.1.3 に残す
- **`InstanceIDToObject` を使わない**：Unity 6000.3 で非推奨のため、`EntityId` へ変換して `EntityIdToObject` を使う（7章）

#### 8.1.7 改修ガイド

- **表示項目を足したい**：`Snapshot` に値を足す → Model のコピー用メソッドで書き出す → タブの `BuildEntryLabels` / `Draw` に反映する。窓口のメソッドは、`Snapshot` の型が増えるときだけ足す
- **絞り込みを足したい**：`RuntimeStateTab` の `FILTER_*` 定数・`FILTER_NAMES` と、Entry の判定（`_entryKindMasks`）を揃えて変える。添字は `LifetimeKind` の値＋1に対応している
- **色・マークを変えたい**：`RuntimeStateTab` の `KIND_COLORS`・`LOADING_COLOR` などの定数と `EnsureStyles` を変える
- **ロード状態の言葉を変えたい**：`LOADING_LABEL` などの定数と、8.1.3 の表を揃えて変える

#### 8.1.8 注意点・既知の制約

- 表示は取得時点の値で、更新間隔（既定 0.5 秒）ごとに取り直される。解放やシーン遷移の直後は、次の更新で反映される
- Owner の GameObject が破棄されてから、Watcher が回収するまでの間（`OnDestroy` の通知、またはポーリング間隔以内）は、そのスコープが Registry に残ったまま、名前が「破棄済み」と表示される。回収されると、次の更新でスコープごと消える
- Label 一括ロードでは、チケットが件数分並ぶ。チケットの行は、Entry とスコープを開いたときだけ描画する
- 名称は、ロード完了までは空。アセットが破棄済み（偽 null）のときも空になる
- `AssetKey` は「入口の種別＋値」の一致で同一判定するため、同じアセットを Path と AssetReference の両方で読むと、別の Entry として並ぶ（仕様。設計仕様書 3.6）
- 見た目（アイコン・色・折り返し）は、手動で確認する。Window は IMGUI のため、自動テストの対象にしていない

#### 8.1.9 テストと動作確認

- **EditMode テスト**（`Tests/Editor/`）
  - `AddressableDebugReaderTests`：`Key`・型・名称・`RefCount`・ロード状態のコピー／同じキーで型違いが別 Entry になる／複数 Entry・複数スコープの内訳の連結と、開始位置・件数の対応／チケットのスコープ・受け渡し済み・Label 一括・スタックトレース／ゴミの入った `List` が `Clear()` される／取得後に Model が変わっても取得済みの値が変わらない／空の Registry／名称（ロード完了前・破棄済みは空文字）
  - `AddressableDebugWindowSettingsTests`：更新間隔の既定値と下限補正
- **動作確認用のサンプル**：`Samples/AddressableLoad/Scenes/` の各シーン。同じアセットを、寿命だけ変えて読んだときの表示は、`PrefabSameAddressLoadScene`（3つの寿命のアドレスを、すべて同じ `manual_object_load_test` にしたシーン）で確認できる

---

### 8.2 リーク検出一覧（「リーク」タブ）

#### 8.2.1 概要

リーク（明示 `Release` されずに寿命の終了で自動回収された、呼び出し元にチケットを返し終えたチケット）を、ログに流すだけでなく、**確保元ごとに集約して溜め**、「リーク」タブで一覧する。呼び出し元のコードへジャンプでき、テキストファイルにも出力できる。ログは流れて見逃されるため、自動回収に頼った実装を是正させる方針（設計仕様書 3.8・3.9(3)）を機能させる目的で作った。

| 部品 | 役割 |
|---|---|
| `LeakRegistryModel` / `LeakSnapshot` | リークを確保元ごとに集約して溜める Model と、取得時点の値のコピー（Runtime。Editor 限定。8.2.3・8.2.4） |
| `LeakListTab` | 「リーク」タブ。一覧・表示のクリア・ログ出力ボタン（8.2.2・8.2.6） |
| `StackTraceParser` / `StackTraceView` | スタックトレースの解析と、リンク・コピー・ジャンプの描画。「実行時状態」タブと共通（8.2.5） |
| `LeakLogFormatter` / `LeakLogWriter` / `LeakLogAutoExporter` | ログファイルの中身の組み立て・書き込み・Play 終了時の自動出力（8.2.7）。日時付きファイルの書き込み・出力先の解決は `EditorTextFileWriter`（Dependency Check と共有）へ委譲 |
| `AddressableLeakLogSettings` | ログ出力の設定（出力先・自動出力）。`AddressableEditorSettingsSO` が保持する（3.1） |

**リークの範囲**（記録の対象は、リーク警告のログを出す対象と同じ）：

| 状況 | 記録 |
|---|---|
| Scene / Owner 寿命が終わり、呼び出し元にチケットを返し終えた分が、`Release` されないまま回収された | する |
| ロード中（返す前）に寿命が終わった分 | しない（呼び出し元が `Release` を呼びようがない。loading-spec 10章） |
| 明示 `Release` 済み | しない |
| `Manager` の破棄（最終防衛線の強制解放） | しない |

#### 8.2.2 画面

```
リーク 12 回（確保元 3 件）                              [ログ出力][表示をクリア]
▸ Path:Prefabs/Enemy  <GameObject>          ×10  15:18:41  [EnemySpawner.cs:42]
    ■ Owner：Enemy_07（ID 12345）
▸ Label「ui」（8件）  [Label 一括]        ×1   15:03:10  [UiLoader.cs:18]
    ■ Scene：Stage1
        ▶ Foo.Bar  (File.cs:42)        ← 開くと、スタックトレースがフレームごとのリンクになる
          （コピー）
```

- **並び**：最終発生の新しい順（`LeakSnapshot.AccSequence` の降順）
- **行**：キーと型（Label 一括は Label と件数。「Label 一括」バッジ付き）／回数（×N）／最終発生時刻／呼び出し元（最初の利用者のフレーム）へのジャンプボタン／寿命の色マーク付きのスコープ名（Scene：緑・Owner：橙。「実行時状態」タブと同じ色）
- **スコープ名**：発生時に記録した名前。取れなかったものは「名前なし（ID n）」「名前なし（handle n）」（8.2.3）
- **行を開く**：スタックトレース全文を、フレームごとのリンクで表示する。最初の利用者のフレームには ▶ が付く（8.2.5）
- **ボタン**：「ログ出力」は表示中の行を出力する（8.2.7）。表示中の行が0件のときは押せない。「表示をクリア」は8.2.6
- Play していないときは「未起動」の表示になり、一覧・開閉・クリアの基準は破棄される（共通基盤 2.3）

#### 8.2.3 蓄積（Runtime・Editor 限定）

- **記録の場所**：`LoadingController.ReleaseLifetime` の、リーク警告（`AddressableLogger.LogWarning(CreateLeakMessage(...))`）を呼ぶ箇所の隣（`#if UNITY_EDITOR`）。判定（`AccIsDelivered` の分だけ・まとめチケットは1枚1件）は、ログを出す条件と同じものを使い、新しい判定は持たない。解放経路（`ReleaseInternal`）は変えない
- **集約の単位（`LeakKey`）**：

  | 種類 | 同じ確保元とみなす条件 |
  |---|---|
  | 単体の Load | `AssetKey`・型・寿命の種類（Scene / Owner）・確保時のスタックトレースが同じ |
  | Label 一括 | Label・寿命の種類・確保時のスタックトレースが同じ |

  **スコープの値（Owner の ID・Scene の handle）は含めない**。別の Owner・別のシーンで、同じ呼び出し元のリークが起きたときも、1行にまとめて回数を数えるため。行に表示するスコープは、最後に発生したもの
- **行が持つ値**：行の ID（最初の発生で採番）・最終発生の通し番号（Model 全体で単調に増える）・回数・最終発生時刻・最終スコープ（種類・値・名前）・確保時のスタックトレース。件数は確保元の数で頭打ちになるため、上限は設けていない
- **スコープ名を発生時に記録する理由**：リークは寿命の終了時に起きるため、後から Editor 側で引くと、ほぼ必ず「破棄済み」になる。`UnityEngine.Object` を Runtime の Snapshot に持たせない方針（4.2）とも両立するよう、文字列で記録する
  - **Owner**：ループの前に、Watcher に登録中の `GameObject` の参照を取る（最後のチケットを取り除くと Watcher の登録が外れるため）。名前の読み出し（文字列を確保する）は、リークを記録するときだけ行う。`OnDestroy` 経由なら名前が取れる。ポーリングで破棄後に見つかった Owner は偽 null のため、名前なしで記録する
  - **Scene**：`HandleSceneUnloaded` が、アンロードされたシーンの名前を `_unloadingSceneName`（`#if UNITY_EDITOR` のフィールド）に、回収の間だけ設定する。`ReleaseLifetime` は同期のため、他の呼び出しと交差しない。公開の `ReleaseLifetime(LifetimeScopeId)` のシグネチャは変えていない
- **Model の扱い**：`LeakRegistryModel` は Manager が `Awake` で生成し、`LoadingController`（書き込む）と `AddressableDebugReader`（読むだけ）へコンストラクタで注入する（2.1）。Model 全体が `#if UNITY_EDITOR` で、本番ビルドには含まれない

#### 8.2.4 `Snapshot` と窓口のメソッド

窓口 `IAddressableDebugReader` に、`void CopyLeaksTo(List<LeakSnapshot> leaks)` を足している（`leaks` を `Clear()` してから書き出す。並びは発生順とは限らない）。

| `LeakSnapshot`（`Core/Loading/Model/`） | 値 |
|---|---|
| `AccId` | 集約した行の ID。行が増えるまで変わらない（クリア時点の回数を覚えるための識別） |
| `AccSequence` | 最後に発生したときの通し番号 |
| `AccIsGroup` | Label 一括のまとめチケットか |
| `AccKey`・`AccAssetType` | 単体のキーと型（Label 一括では `default`・`null`） |
| `AccLabel`・`AccGroupCount` | Label 一括の Label と件数（単体では空文字・0） |
| `AccScopeKind`・`AccScopeValue`・`AccScopeName` | 最後に発生したスコープの種類・値（Owner は InstanceID、Scene は handle）・名前（無いときは空文字） |
| `AccCount`・`AccLastTime` | 発生回数・最終発生時刻 |
| `AccStackTrace` | 確保時のスタックトレース（文字列の参照のコピー） |

#### 8.2.5 スタックトレースの解析とジャンプ（共通部品）

- **`StackTraceParser`**（Unity 非依存）：`Environment.StackTrace`（Mono 形式。`at メソッド (引数) [0x..] in <パス>:<行>`）を、フレーム（`TraceFrame`）に分ける。
  - パスと行は、最後の `:` で分ける（Windows のドライブレターを壊さない）。区切りは `/` にそろえる
  - ファイル情報の無い行（`<ハッシュ>:0`・`<filename unknown>`・行が0以下・場所なし）は、例外にせず、ジャンプ不可のフレームとして残す
  - `Assets/`・`Packages/`・`./Library/` などの相対パスは、プロジェクトのルートからの絶対パスにする
  - **最初の利用者のフレーム**：ジャンプできて、モジュール内でない最初のフレーム。呼び出し元の行として、ジャンプボタンとログに使う
    - **モジュール内の判定**：メソッド名が `MAH.AddressableManager.` で始まるもの（ただし `MAH.AddressableManager.Samples.`・`MAH.AddressableManager.Tests.` は利用者側）。導入場所（`Assets/`・`Packages/` の embedded・`file:` 参照・PackageCache）に左右されないよう、パスではなく名前空間で判定する。非同期のステートマシン（`LoadingController+<LoadAsync>d__5`1[T].MoveNext`）も同じ名前空間で始まるため、そのまま判定できる
    - ファイルのパスに `/Library/PackageCache/` を含むものも、モジュール内として扱う（UniTask など、モジュール以外のパッケージのフレームを飛ばすため）
- **`StackTraceView`**：解析結果をトレース文字列ごとにキャッシュし、リンク・薄い表示・▶・全文コピーのボタンを描く。ジャンプは `InternalEditorUtility.OpenFileAtLineExternal(path, 行)` で、外部エディタで開く。ファイルが無いときは警告を出す
  - 解析は、初めて見たトレースの1回だけ確保する（`Draw` の中でも起きるが、以降はキャッシュを使う）。`Clear()` で破棄する
  - 「実行時状態」タブのチケットのスタックトレースも、同じ部品で表示する（8.1）

#### 8.2.6 表示のクリア（既読扱い）

「表示をクリア」は、Runtime の記録を消さない（窓口に書き換えのメソッドを足さないため）。タブが「クリア時点の基準」を持ち、以降の発生分だけを表示する。

- **基準**：クリア時点の通し番号の最大値と、行 ID → その時点の回数
- **表示する行**：`AccSequence` が基準の通し番号より大きい行。**行の回数は `AccCount` から基準の回数を引く**（クリア以降の分だけを数える）。先頭の「リーク N 回（確保元 M 件）」も、これに合わせる
- **反映のタイミング**：ボタンを押した**次の `Layout` イベントの先頭**で反映する。描画の途中で行の数が変わると、IMGUI のレイアウトが崩れるため
- Play の終了（未起動への遷移）で、基準は破棄される。最後の更新からクリアまでの間に起きたリークは、基準に含まれないため、クリア後に表示される（隠さない側に倒している）

#### 8.2.7 ログ出力

「ログ出力」ボタンで、表示中のリークをテキストファイルに出力する。「ログ出力 → 表示をクリア」の流れで、クリアした後でも、区間ごとの記録を後から確認できる。Play の終了時に自動で出力する設定もある。

| | 手動 | Play 終了時の自動 |
|---|---|---|
| 契機 | 「ログ出力」ボタン | `ExitingPlayMode`（`LeakLogAutoExporter`。Window を開いていなくても動く） |
| 対象 | 表示中の行（クリア以降の分。回数もクリア以降の回数） | そのセッションの全リーク（タブのクリアとは無関係） |
| ファイル | `AddressableLeaks_yyyyMMdd_HHmmss.txt`（日時付き。**溜める**）。同名があれば `_2`・`_3`… を付ける | `AddressableLeaks_LastPlay.txt` に**上書き**し、前回分を `AddressableLeaks_LastPlay-prev.txt` に移す（1世代だけ残す） |
| 0件のとき | ボタンが押せない | 「リークはありませんでした」を書いて上書きする（前の Play のファイルが残って取り違えるのを防ぐ） |
| 条件 | 表示中の行が1件以上 | 設定の自動出力が ON（既定 OFF）で、Manager が生きている |

- **出力先**：`AddressableLeakLogSettings` の出力先フォルダ（既定 `Logs/AddressableManager`。相対パスはプロジェクトのルートから）。フォルダが無ければ作る。`Logs/` は Unity がインポートせず、git の管理外。Unity 自身のログと混ざらないよう、1階層かませている。押したとき・Play の終了時に、`AddressableEditorSettingsLocator.FindLeakLogSettings()` で SO から読む（SO が無いときは既定値：自動出力 OFF・`Logs/AddressableManager`）
- **書き込み**：UTF-8（BOM なし）。書き出したパスを Console に出す（`AddressableLogger.LogInfo`。既定のログレベル〔Warning〕で表示され、Error 以上にすると出ない。失敗の警告も `AddressableLogger.LogWarning` で、同じくログレベルに従う）。**自動出力が削除・移動するのは、固定名の2ファイルだけ**（パターンでの一括削除をしないため、手動のログは自動では消えない）。書き込み・移動に失敗したときは、例外を握って警告を出し、Editor・Play の終了を止めない
- **ファイルの形式**：

  ```
  AddressableManager リークログ
  出力日時：2026-10-14 15:20:03
  リーク 12 回（確保元 2 件）

  [1] ×10  Path:Prefabs/Enemy  <GameObject>
      寿命：Owner：Enemy_07（ID 12345）
      最終発生：15:18:41
      呼び出し元：/Users/me/proj/Assets/Scripts/EnemySpawner.cs:74
      スタックトレース：
        at ...（フレームごとに1行。全文）
  ```

  Label 一括の行は `Label「ui」（8件）`。呼び出し元が分からないときは「不明」
- **組み立てと書き込みを分けている**：`LeakLogFormatter`（Unity 非依存。出力する行と回数は、呼び出し側が渡す）と `LeakLogWriter`（出力先の解決・書き込み・固定名の入れ替え）。前者は IO を持たず、テストから直接検証できる

#### 8.2.8 設計の判断と理由

- **確保元で集約する**：同じ Prefab を大量に生成して全部 `Release` し忘れると、同じ確保元のリークが何百件も発生する。集約なら、行が確保元の数で頭打ちになり、是正すべき箇所が一目で分かる
- **「表示をクリア」を既読扱いにする**：窓口に Model を書き換えるメソッドを足すと、読み取り専用の原則（6.4）の例外になる
- **自動出力を Play 終了時に Editor 側が読む形にする**：発生のたびに Runtime がファイルへ追記すると、Runtime にファイル書き込みが入り、pull 方針（設計仕様書 3.9）と合わない。Window を閉じていても動くよう、Window とは独立した `[InitializeOnLoad]` のクラスにした
- **自動出力は固定名の上書き＋1世代**：Play のたびにファイルが増え続けるのを避ける。0件でも書くのは、前の Play のファイルが残って取り違えるのを防ぐため。この動きは設定にしていない（上書きしても直前の分は `-prev` に残る）
- **手動出力は日時付きで溜める**：出力 → クリアを繰り返して、区間ごとの記録を残せるように
- **ログの形式はテキストのみ**：スタックトレース（複数行）が、CSV・JSON では読みにくくなるため
- **保存ダイアログを出さない**：自動出力と手動出力で出力先の扱いが分かれる。設定の出力先に揃え、押したらすぐ書けるようにした
- **スタックトレースの解析を Runtime で行わない**：解析は表示のための処理で、Runtime の責務ではない。`LeakSnapshot` は文字列のまま渡し、Editor 側で解析する
- 採用しない設計の一覧は、設計仕様書 5.3 と `.steering/20261014-editor-leak-list/design.md` を参照

#### 8.2.9 改修ガイド

- **表示項目を足したい**：`LeakSnapshot` に値を足す → `LeakRegistryModel` の行（`LeakRecord`）と `CopyLeaksTo` で書き出す → `LeakListTab`（`BuildRows`・`DrawRow`）と `LeakLogFormatter` に反映する
- **集約の単位を変えたい**：`LeakRegistryModel` の `LeakKey`（等価判定とハッシュ）を変える。スコープの値を含めるかどうかで、行の分かれ方が変わる（8.2.3）
- **リークを記録する経路を足したい**：記録は `LoadingController` の、ログを出す箇所の隣に置く。回収の経路を増やすときは、明示解放・自動回収が同じ経路（`ReleaseInternal`）を通る原則を守り、記録の呼び出しも同じ条件（`AccIsDelivered`）に揃える
- **ジャンプで飛ばす場所を変えたい**：`StackTraceParser` の `MODULE_RUNTIME_PATH`・`PACKAGE_CACHE_PATH`（最初の利用者のフレームの判定）
- **ログの中身を変えたい**：`LeakLogFormatter`。ファイル名・出力先の解決・固定名の入れ替えは `LeakLogWriter`（出力先の解決と日時付きの書き込みの本体は、Dependency Check と共有の `EditorTextFileWriter`）。設定項目は `AddressableLeakLogSettings`（3.1・6.2）
- **別の形式（CSV など）で出したい**：`LeakLogFormatter` に並べず、別のクラスで組み立てる（書き込みは `LeakLogWriter`（日時付きの書き込みは `EditorTextFileWriter`）を使い回せる）

#### 8.2.10 注意点・既知の制約

- 一覧は Play の終了で消える（Manager の破棄とともに記録も捨てられる）。残したいときは、「ログ出力」か、Play 終了時の自動出力を使う
- **Play の停止そのものが原因の回収は、自動出力に含まれない**。`ExitingPlayMode` は、シーンの破棄（`sceneUnloaded`・Owner の `OnDestroy`）より前に呼ばれるため。Manager の破棄による強制解放は、もともとリークとして記録しない
- 手動出力は、最後の更新（既定 0.5 秒間隔）の時点のスナップショットを書く。画面に見えている内容と同じで、直後に起きたリークは次の更新まで入らない
- スタックトレースは、`Environment.StackTrace` の書式（Mono 形式）に依存する。ファイル情報が付かない環境では、ジャンプできない（フレームは表示される）
- スコープ名は、記録時に取れた文字列。ポーリングで破棄後に見つかった Owner は、名前なし（ID 表示）になる
- 同じ確保元で、別のスコープから起きたリークは1行にまとまり、表示されるスコープは最後に発生した分だけ
- `DontDestroyOnLoad` シーンを Scene 寿命に指定した分は、実質 Manual 寿命として扱われるため、リークとして記録されない（loading-spec 5.3）
- スタックトレースを記録するのは Editor だけ（`#if UNITY_EDITOR`）。ビルド後のアプリ・実機では、リークの記録も、ログ出力もできない
- 描画（リンク・色・ボタン）は、手動で確認する。Window は IMGUI のため、自動テストの対象にしていない

#### 8.2.11 テストと動作確認

- **EditMode テスト**（`Tests/Editor/`）
  - `LeakRegistryModelTests`：集約（同じ確保元で1行・トレースやキー・型が違えば別行・スコープの値だけが違えば同じ行・寿命の種類が違えば別行）／最終発生の情報での上書き／通し番号と行 ID／Label 一括と単体の区別／`Clear()`／取得後に記録が増えても取得済みの値が変わらない
  - `LoadingControllerLeakTests`：Owner 破棄・Scene アンロード・ポーリングでの回収の記録（スコープ名・名前なし）／ログの警告が従来どおり出る／明示 Release 済み・ロード中の回収・`Dispose` は記録しない／同じ呼び出し元の確保は1行に集約・行が違えば別行／Label 一括は1枚1行
  - `AddressableDebugReaderTests`：`CopyLeaksTo`
  - `StackTraceParserTests`：Mono 形式の行・ジェネリック名・ファイル情報の無い行・Windows パス・相対パス・ファイル名・モジュール内の判定（名前空間・Samples/Tests は利用者側・PackageCache・embedded パッケージ・非同期のステートマシン）・実環境のトレースでの最初の利用者のフレーム
  - `AddressableLeakLogSettingsTests`：既定値・補正・SO の `OnValidate`
  - `LeakLogFormatterTests`：見出し・並び・回数・単体と Label 一括の表記・スコープ名・呼び出し元・スタックトレース全文・0件
  - `LeakLogWriterTests`（`EditorTextFileWriter` への委譲後も同じ結果）：出力先の解決・ファイル名・連番・UTF-8（BOM なし）・`WriteLastPlay` の1世代の入れ替え・手動ログが消えないこと・書き込み失敗で null
- **動作確認用のサンプル**：`Samples/AddressableLoad/Scenes/` の各シーンで、Owner・Scene 寿命のアセットを `Release` せずに破棄・遷移すると、「リーク」タブに行が出る。同じ操作の繰り返しで回数が加算され、「表示をクリア」の後は新しい発生だけが表示される

---

### 8.3 owner Watcher の可視化（「Owner」タブ）

#### 8.3.1 概要

Owner 寿命の破棄検知（Watcher の `OnDestroy` → 低頻度ポーリング → `Manager.Dispose` の多重の安全網。設計仕様書 3.8）が期待通り働いているかを、Play 中に確かめられるようにする。**監視中の Owner と Watcher の貼り付け状態**を一覧し、**Owner の回収が `OnDestroy` 経由か、ポーリング経由か**を区別して記録する。ポーリング経由の回収が多いことは、Watcher が取りこぼしている（例：一度も Active にならず `OnDestroy` が呼ばれない GameObject）ことを示す。タブは `OwnerWatcherTab`（`Editor/Window/`）。

| 部品 | 役割 |
|---|---|
| `OwnerWatchRegistryModel` / `OwnerWatchSnapshot` | 監視中の Owner。既存の Model に、Editor 限定で名前の保持とコピー用メソッド（`CopyOwnersTo`）を足した（8.3.4） |
| `OwnerReclaimRegistryModel` / `OwnerReclaimSnapshot` / `OwnerReclaimRoute` | 回収を（経路・Owner 名）ごとに集約して溜める Model と、取得時点の値のコピー、回収経路の enum（Runtime。Editor 限定。8.3.3） |
| `OwnerWatcherTab` | 「Owner」タブ。監視中の一覧・回収の記録・合計の表示（8.3.2） |

**記録の範囲**：

| 状況 | 記録 |
|---|---|
| Watcher の `OnDestroy` の通知で、監視中の Owner が回収された | する（経路 `OnDestroy`） |
| ポーリングで、破棄済みの監視中 Owner が見つかり、回収された | する（経路 `Polling`） |
| 全チケットを明示 `Release` して監視が外れた Owner の破棄 | しない（回収ではない） |
| 回収済みの Owner への、遅れて届く通知・その後のポーリング | しない（監視が外れているため。二重記録の防止） |
| `Manager` の破棄（最終防衛線の強制解放）・破棄後の通知 | しない |

#### 8.3.2 画面

```
監視中の Owner 3 件                                              ← ツールバー
Enemy_01（ID 12345）                       チケット 2   ● Watcher ✓           （緑）
Boss（ID 12400）                           チケット 1   ● Watcher 未登録      （赤）
Bullet（ID 12500）                         チケット 1   ● 破棄済み・未回収    （黄）

回収 OnDestroy 120 回 ｜ ポーリング 3 回（2.4%）
[ポーリング] Bullet                  ×3   チケット 3   15:20:11               ← ポーリングの行が先
[OnDestroy]  Enemy(Clone)            ×120 チケット 240 15:20:09
```

- **監視中の Owner**：`名前（ID n）`・確保中のチケット数・状態。並びは名前 → InstanceID の順（`Dictionary` の順序が変わっても、行が入れ替わらないため）
- **状態**（Dark / Light どちらのスキンでも読める中間の明るさ。「実行時状態」タブのロード状態の色に合わせる）：

  | 表示 | 色 | 意味 |
  |---|---|---|
  | `● Watcher ✓` | 緑 | GameObject が生きていて、Watcher が貼られ、このスコープが登録されている（正常） |
  | `● Watcher なし` | 赤 | 生きているのに、Watcher が貼られていない（`OnDestroy` で回収できない） |
  | `● Watcher 未登録` | 赤 | Watcher はあるが、このスコープが登録されていない |
  | `● 破棄済み・未回収` | 黄 | GameObject が破棄済み（偽 null）だが、まだ回収されていない（ポーリングの待ち。既定 3 秒間隔） |

- **回収の記録**：経路（`[ポーリング]`・`[OnDestroy]`）・Owner 名・回数（×N）・回収したチケット数の累計・最終時刻。**ポーリングの行を先に**、その中で最終発生の新しい順（`AccSequence` の降順）。名前が無いものは「名前なし」。見出しは、`OnDestroy` の回数・ポーリングの回数・ポーリングの割合
- **チケット数**：Model には持たせず、タブが発行済みチケット（`CopyIssuedTicketsTo`）のうち Owner 寿命のものを、InstanceID ごとに数える（8.1 で Entry とチケットの対応を Editor 側で付けたのと同じ方針）。明示 `Release` で全チケットが尽きた Owner は、監視が外れ、次の更新で一覧から消える
- Play していないときは「未起動」の表示になり、一覧・記録は破棄される（共通基盤 2.3）。「表示をクリア」・ログ出力は持たない

#### 8.3.3 回収の記録（Runtime・Editor 限定）

- **記録の場所**：Owner の回収が起きる2つの入口で、**解放（`ReleaseLifetime`）の直前に**記録する。解放の経路は変えず、`ReleaseLifetime` → `ReleaseInternal` の1つのまま
  - **Watcher の通知**：Watcher に渡す delegate（`_onOwnerDestroyed`）を、Editor では `HandleOwnerDestroyed`（経路 `OnDestroy` を記録 → `ReleaseLifetime`）にする。Dispose 後に届いた通知は、監視が外れている（`IsWatching` が false）ため記録されず、`ReleaseLifetime` も空振りする。本番ビルドでは、従来どおり `ReleaseLifetime` を直接渡す。delegate はコンストラクタで1回だけ代入するフィールドで、毎回生成しない
  - **ポーリング**：`PollDestroyedOwners` のループで、各スコープに経路 `Polling` を記録してから `ReleaseLifetime` を呼ぶ
- **記録の条件**：`_ownerWatch.IsWatching(scope_id)` のときだけ記録する（`RecordOwnerReclaim`）。明示 `Release` で監視が外れた Owner の破棄や、回収済みの Owner への通知は、`IsWatching` が false になるため、新しい状態を持たずに二重記録を防げる
- **チケット数は回収前に読む**：`ReleaseLifetime` がチケットを取り除くため、`_tickets.CountForScope(scope_id)`（Editor 限定。索引の件数を返すだけで、`SnapshotSerialsForScope` の再利用バッファに触れない）を呼び出しの前に読む
- **Owner 名は登録時に文字列で保持する**：`OwnerWatchRegistryModel.Watch` が、`#if UNITY_EDITOR` で `owner.name` を保存する（`TryGetOwnerName`。`Unwatch`・`Clear` で外す）。ポーリングで見つかる Owner は偽 null で名前が取れず、取りこぼしの調査に名前が必要なため
- **集約の単位（`ReclaimKey`）**：回収経路・Owner 名が同じものを1行にまとめる。**InstanceID は含めない**（同じ Prefab を生成・破棄するたびに ID が変わるため。最後の ID だけ持つ）。件数は Owner 名の種類で頭打ちになるため、上限は設けていない。`LeakRegistryModel` と同じ構成
- **行が持つ値**：行の ID（最初の発生で採番）・最終発生の通し番号（Model 全体で単調に増える）・経路・Owner 名・最終 InstanceID・回数・チケット数の累計・最終時刻
- **Model の扱い**：`OwnerReclaimRegistryModel` は Manager が `Awake` で生成し、`LoadingController`（書き込む）と `AddressableDebugReader`（読むだけ）へコンストラクタで注入する（2.1）。Model 全体が `#if UNITY_EDITOR`。`Dispose` で `Clear()` する

#### 8.3.4 `Snapshot` と窓口のメソッド

窓口 `IAddressableDebugReader` に、次の2つを足している（どちらも渡された `List` を `Clear()` してから書き出す）。

| メソッド | 書き出す先 | 内容 |
|---|---|---|
| `CopyWatchedOwnersTo(List<OwnerWatchSnapshot> owners)` | `owners` | Owner 寿命で監視中の GameObject。並びは登録の順とは限らない |
| `CopyOwnerReclaimsTo(List<OwnerReclaimSnapshot> reclaims)` | `reclaims` | 回収の記録の集約した行。並びは発生順とは限らない |

| `Snapshot`（`Core/Loading/Model/`） | 値 |
|---|---|
| `OwnerWatchSnapshot` | `AccInstanceId`・`AccOwnerName`（登録時の名前）・`AccIsAlive`（偽 null でない）・`AccHasWatcher`（`AssetLifetimeWatcher` が貼られている）・`AccIsRegistered`（その Watcher にこのスコープが登録されている） |
| `OwnerReclaimSnapshot` | `AccId`・`AccSequence`・`AccRoute`（`OwnerReclaimRoute`）・`AccOwnerName`・`AccLastInstanceId`・`AccCount`・`AccTicketCount`・`AccLastTime` |

- **`OwnerWatchRegistryModel.CopyOwnersTo`**：生存判定（偽 null）・`TryGetComponent<AssetLifetimeWatcher>`・`AssetLifetimeWatcher.IsRegistered(scope)`（`#if UNITY_EDITOR` で追加）を、**取得時に**調べて `bool` で詰める。`GameObject` や Watcher の参照は `Snapshot` に持たせない。`_watchedOwners` は具体型のまま `foreach` する
- `OwnerWatchRegistryModel` は既に `GameObject` を保持する Model で、判定は Editor 限定のコピー用メソッド内に限る。窓口が Service 層（`IAssetLifetimeWatcherBinder`）を知る形にはしない（「Registry と Service の両方を知ってよいのは Controller だけ」）
- 窓口に注入する Model は、`AssetRegistryModel`・`IssuedTicketRegistryModel`・`LeakRegistryModel`・`OwnerWatchRegistryModel`・`OwnerReclaimRegistryModel` の5つ（2.1）

#### 8.3.5 設計の判断と理由

- **入口側で記録する**：`ReleaseLifetime` に回収経路の引数を足すと、本番ビルドの呼び出しにも引数が残り、Watcher の delegate（`Action<LifetimeScopeId>`）のシグネチャも変えられない。入口（Watcher の通知・ポーリング）で記録すれば、解放経路は1つのまま。`Watcher` 自体に記録を持たせると、Controller の `IsWatching` の判定を迂回してしまうため、記録は Controller に集約する
- **本番ビルドの delegate を変えない**：`_onOwnerDestroyed` の代入だけを `#if UNITY_EDITOR` で分け、本番では従来どおり `ReleaseLifetime` を渡す
- **経路・名前で集約する**：回収を発生順に全件並べると、Owner の生成・破棄を繰り返す間に行が増え続ける（リークと同じ判断。8.2.8）。InstanceID を含めると、生成のたびに別の行になり、集約の意味がなくなる
- **Owner 名を登録時に持つ**：ポーリングで見つかる Owner は偽 null で、名前が取れない。取りこぼしがどの Owner に集中しているかを調べるために必要
- **Watcher の状態を Model の `GameObject` から調べる**：Binder から取る案は、窓口が Service 層を知ることになり採らない
- **チケット数を Editor 側で数える**：`IssuedTicketRegistryModel` と二重管理になるのを避ける。窓口のメソッドも増やさずに済む
- **`LoadingController` のコンストラクタ引数を必須にする**：任意（`= null`）にすると、記録の抜けに気づけなくなる（8.2 と同じ）

#### 8.3.6 改修ガイド

- **表示項目を足したい**：`OwnerWatchSnapshot` / `OwnerReclaimSnapshot` に値を足す → Model のコピー用メソッド（`CopyOwnersTo`・`CopyReclaimsTo`）で書き出す → `OwnerWatcherTab` の `BuildOwnerRows`・`BuildReclaimRows`・`Draw` に反映する
- **状態の判定・色を変えたい**：`OwnerWatcherTab.BuildOwnerRows` の状態の分岐と、`STATUS_*` の定数・色（`OK_COLOR` など）・`EnsureStyles`。8.3.2 の状態表も揃える
- **集約の単位を変えたい**：`OwnerReclaimRegistryModel` の `ReclaimKey`（等価判定とハッシュ）
- **回収の入口を足したい**：記録は、入口の `ReleaseLifetime` の直前に `RecordOwnerReclaim` を呼ぶ形で足す。解放経路（`ReleaseInternal`）は増やさない。経路が増えるときは `OwnerReclaimRoute` に足し、タブのラベル・並びを揃える
- **「表示をクリア」・ログ出力を足したい**：`LeakListTab` と同じ方式（タブが基準を持つ既読扱い。窓口に書き換えのメソッドを足さない。8.2.6・8.2.7）

#### 8.3.7 注意点・既知の制約

- 回収の一覧は Play の終了で消える（Manager の破棄とともに記録も捨てられる）
- **Owner 名は登録時のもの**。登録後にリネームされても、表示は古い名前のまま
- **同じ名前の Owner は1行にまとまる**（`Enemy(Clone)` など。経路が同じ場合）。どの個体かは、最後に回収された ID（行のツールチップ）で見る
- `CopyOwnersTo` は、取得のたびに、監視中の Owner の数だけ `TryGetComponent` を呼ぶ（更新間隔〔既定 0.5 秒〕ごと・「Owner」タブの表示中だけ）。大量の Owner を同時に監視していると、取得の負荷が増える
- 表示は取得時点の値。ポーリング待ちの Owner（黄）は、次の回収で消える
- 一度も Active にならなかった GameObject は、`AssetLifetimeWatcher.OnDestroy` が呼ばれないため、ポーリングでしか回収されない（ポーリングの行に集まる）
- 明示 `Release` で全チケットが尽きた Owner、`Manager` の破棄による強制解放は、回収として記録しない
- Watcher の状態が赤（なし／未登録）になるのは、Binder を経由せず Watcher が外された場合など、通常は起きない。出たときは、破棄検知の不具合として調べる対象
- 見た目（色・並び・見出し）は、手動で確認する。Window は IMGUI のため、自動テストの対象にしていない

#### 8.3.8 テストと動作確認

- **EditMode テスト**（`Tests/Editor/`）
  - `OwnerReclaimRegistryModelTests`：同じ経路・名前で1行に集約／経路が違えば別行／名前が違えば別行／InstanceID は集約に含めず最後の ID を持つ／回数・チケット累計・最終時刻・通し番号と行 ID／null の名前は空文字／`Clear()`／ゴミの入った `List` が `Clear()` される／取得後に記録が増えても取得済みの値が変わらない
  - `LoadingControllerOwnerReclaimTests`：`OnDestroy` 経路の記録／ポーリング経路の記録（登録時の名前）／チケット数が回収前の件数／同名の Owner の集約／全チケットを明示 Release した後の破棄は記録しない／`OnDestroy` 回収後のポーリングで二重記録しない／ポーリング回収後に遅れて届く `OnDestroy` で二重記録しない／`Dispose` 後の通知は記録しない／Manual・Scene 寿命は監視中の一覧に入らない／回収の動作（アセットの解放・リークの記録）が従来どおり。遅れて届く通知は、`Unbind` 後も通知を呼べるテスト内の偽 Binder（`CapturingBinder`）で再現する
  - `AddressableDebugReaderTests`：`CopyWatchedOwnersTo`（生存・登録時の名前・本物の `AssetLifetimeWatcherBinder` での貼り付け／登録の状態・Watcher はあるが未登録・破棄済み・監視が外れた Owner を含まない・`Clear()`・取得後の不変）・`CopyOwnerReclaimsTo`・コンストラクタの null 引数
- **動作確認用のサンプル**：`Samples/AddressableLoad/Scenes/` の Owner 寿命を使うシーンで、アセットを読んだ GameObject を `Destroy` すると、`[OnDestroy]` の行が出る。非アクティブで生成して一度も Active にしないまま読み、`Destroy` すると、ポーリング間隔（既定 3 秒）の後に `[ポーリング]` の行が出る
