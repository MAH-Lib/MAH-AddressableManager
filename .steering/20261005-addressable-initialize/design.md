# 設計：AddressableManager 初期化（Initialize）機能

> 対象：`requirements.md`（承認済み版） ／ 作成日：2026-10-05 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.23f1

## 1. 実装アプローチ

仕様は `src/AddressableManager/Docs/specs/initialize-spec.md` で確定済みのため、本書は仕様を実装に落とす順序・配線・実装時の判断だけを書く。手順・例外の表・サンプルコードは仕様書を正とする。

方針：
- 初期化の実行と状態遷移は `InitializeController`（ピュア C#）に集める。状態データは `InitializeStateModel`、`Addressables.InitializeAsync` の呼び出しは `AddressablesInitializeService` に分ける。Manager は生成・配線・公開 API・イベントの発火・`LateUpdate` の中継だけを行う
- Loading との橋渡しは `IInitializationGate`（`Core/Interfaces/`）だけ。`InitializeController` が実装し、Manager が `LoadingController` のコンストラクタに注入する。Loading は `Core.Initialize` を知らない
- 共有タスク・ゲート・Service の戻り値は、UniTask 使用時も常に `Task`。UniTask は `AddressableManager.InitializeAsync` のシグネチャだけで出し分ける
- DI コンテナは使わず、`AddressableManager.Awake` での手動 `new` で配線する（LifetimeScope・`IScopeInstaller` への登録なし）

実装時の判断（仕様書に書かれていない部分）：
- **イベントの購読を、自動開始より前に行う**：Addressables が初期化済みだと `Awake` 内で同期完了する（仕様 10.1 P4）。購読が後だと `OnInitialized` が発火しないため、`Awake` の順序を「Model・Controller 生成 → Loading 生成 → 購読 → 自動開始」にする
- **公開イベントの発火元は Manager**：Controller は `internal event`（`OnSucceeded` / `OnFailed`）を持ち、Manager が購読して公開イベント（`OnInitialized` / `OnInitializeFailed`）へ中継する。公開イベントを Controller に持たせると、利用者が外部依存型でない型に触れてしまうため
- **利用者のイベントハンドラが投げた例外**：Service の完了コールバック内で同期的に呼ばれるため、`InitializeController` がイベントを発火する箇所（`RaiseSucceeded` / `RaiseFailed`）で `try/catch` し `AddressableLogger.LogException` に流す（状態遷移・共有タスクの完了を止めないため。Controller 側に置くのは、Manager を介さない EditMode テストでも保証できるようにするため）
- **共有タスクは `TaskCompletionSource` で作る**：`MarkRunning` に渡す共有タスクを先に確定させてから Service を呼ぶ（Addressables が初期化済みで同期完了しても、`NotStarted → Running → Succeeded` の順序を守るため）。状態の遷移とイベントの発火を先に行い、その後で共有タスクを完了させる。`Dispose` は待機中の呼び出し元を `TrySetCanceled` で直接終わらせる
- **進捗の通知先は、初期化を始める前に登録する**：Addressables が初期化済みで同期完了すると、開始と同時に「完了時の 1」を通知するため、登録が後だと 1 が届かない。成功済みの呼び出しにも 1 を通知する（実装中に発見して修正。EditMode テストで確認）
- **自動開始の例外観測**：`InitializeAsync` は Controller の内部で `LogError` するが、待つ人がいなければ共有タスクの例外が未観測になる。Manager が `ContinueWith`（`OnlyOnFaulted`、static にキャッシュした delegate で `Exception` を読むだけ）を付ける
- **Service の進捗**：`autoReleaseHandle=true` では完了後に handle が解放されて読めなくなるため、`GetPercentComplete()` は未完了の間だけ handle から読み、完了後は 1 を返す。handle の状態確認を伴うため、アクセサ（`Acc`）ではなくメソッドにした（`conventions.md` 2章：アクセサ内で関数を呼ばない）。`Tick` は `Running` のときだけ働くので、解放後に読まれることはない
- **ゲートの待機は共有タスクを待つ**：`WaitUntilOpenAsync` は共有タスクに `AsyncCompat.WithCancellation(ct)` を付けて返すだけ。判定は `AccState` と設定の組み合わせで行い、Loading 側へ設定を渡さない
- **Loading 側の待機は共通メソッドに集約する**：`LoadAsync` / `LoadByLabelAsync` で同じ「寿命の事前確認 → 待機 → 破棄確認 → 寿命の再確認」が要るため、private メソッド `WaitForInitializationGateAsync(lifetime, ct)` に切り出す。寿命の有効判定は `IsLifetimeAlive(lifetime)` として切り出す（既存の `ResolveOwnerScope` / `ResolveSceneScope` は、引数名つきの `ArgumentException` を投げる既存の検証のため変更せず、待機の前後の確認だけに使う）

検討した代替案と却下理由：
- **Controller が公開イベントを直接持つ案**：上記のとおり外部依存型の境界が崩れるため却下
- **ゲートにも Model を直接参照させる案**：`Core/Initialize/` の Model を Loading から参照すると設計違反になるため却下（仕様 7.2）
- **ゲート待機を `UniTask` で書く案**：共有タスクは複数の呼び出し元が待つため `Task` である必要があり、変換が増えるだけなので却下（仕様 7.3）
- **`Addressables.InitializeAsync` の handle を `AsyncCompat.ToTask` で変換する案**：UniTask 版では未定義、かつ `await` 後は handle が解放済みで結果を読めない場合があるため却下。Service が `Completed` の中で結果を読んで `TaskCompletionSource` に渡す（仕様 7.5）
- **ステップ5（ゲート注入）で Manager を後回しにする案**：LoadingController のコンストラクタ変更で Manager がコンパイルエラーになり、各ステップで動く状態を保てないため却下。同じコミットで Manager の配線の一部まで入れる

## 2. 変更するコンポーネント

配置先は `src/AddressableManager/Assets/@MAH/AddressableManager/` を起点とする。

| 対象（クラス/ファイル） | 変更内容 | 新規/修正 |
|---|---|---|
| `Runtime/Core/Initialize/Data/InitializeState.cs` | 初期化の状態 enum（外部依存型・`MAH.AddressableManager`） | 新規（ステップ1で作成済み） |
| `Runtime/Core/Initialize/Data/InitializeMode.cs`・`LoadBeforeInitializePolicy.cs` | 設定用 enum（内部実装型） | 新規（同上） |
| `Runtime/Core/Initialize/Data/AddressableInitializeSettings.cs` | 機能ごとの設定クラス（`[Serializable]`） | 新規（同上） |
| `Runtime/Core/AddressableInitializeException.cs` | 初期化失敗の例外（外部依存型） | 新規（同上） |
| `Runtime/Core/Interfaces/IInitializationGate.cs` | Loading への窓口（`AccIsOpen`／`WaitUntilOpenAsync`） | 新規（同上） |
| `Runtime/Core/Initialize/Model/InitializeStateModel.cs` | 状態・失敗の例外・共有タスク・進捗の通知先。遷移メソッドで終端を保証する Model | 新規 |
| `Runtime/Core/Initialize/Service/IAddressablesInitializeService.cs`・`AddressablesInitializeService.cs` | `Addressables.InitializeAsync` の1回分の呼び出し・結果の読み取り（`Completed` 内）・同期例外の失敗化・handle の保持と解放 | 新規 |
| `Runtime/Core/Initialize/Service/CatalogCacheBustingService.cs` | `InternalIdTransformFunc` の連結・復元と `.hash` の URL 変換 | 新規 |
| `Runtime/Core/Initialize/Controller/InitializeController.cs` | 初期化の実行・状態遷移・進捗通知・ゲートの実装・`Dispose` | 新規 |
| `Runtime/Core/AddressableRuntimeSettingsSO.cs` | `_initialize` と `AccInitialize` の追加 | 修正（ステップ1で完了済み） |
| `Runtime/Core/Loading/Controller/LoadingController.cs` | コンストラクタに `IInitializationGate` を追加。`LoadAsync` / `LoadByLabelAsync` の先頭に待機を追加（`WaitForInitializationGateAsync`・`IsLifetimeAlive`） | 修正 |
| `Runtime/Core/AddressableManager.cs` | Model・Controller・Service の生成と配線、公開 API・イベント、`Tick` 購読、自動開始、`OnDestroy` の順序、変換関数の Apply / Restore | 修正 |
| `Tests/Editor/Fakes/FakeAddressablesInitializeService.cs`・`FakeInitializationGate.cs` | 偽の Service（完了・失敗・同期例外・進捗を手動で操作）・偽のゲート（常に開く／状態を操作できる） | 新規 |
| `Tests/Editor/InitializeStateModelTests.cs`・`InitializeControllerTests.cs`・`InitializeControllerGateTests.cs`・`CatalogCacheBustingServiceTests.cs`・`LoadingControllerInitializationGateTests.cs` | 仕様 11章の EditMode テスト | 新規 |
| `Tests/Editor/LoadingControllerTestBase.cs`・`LoadingControllerPollingTests.cs` | 常に開いている偽のゲートを渡す | 修正 |
| `Tests/Runtime/InitializePlayModeTests.cs` | 自動・手動の初期化とイベント（実際の Addressables） | 新規 |

各クラスの層：Model＝`InitializeStateModel`、Controller＝`InitializeController`、Service＝`AddressablesInitializeService`・`CatalogCacheBustingService`、Data／Definition＝設定クラス・enum・例外、Manager＝合成ルート。namespace は `conventions.md` 7章のとおり（外部依存型は `MAH.AddressableManager`、内部実装型は `MAH.AddressableManager.Core.Initialize`、ゲートは `MAH.AddressableManager.Core`）。

## 3. データ構造の変更

- `AddressableRuntimeSettingsSO` に `_initialize`（`AddressableInitializeSettings`）を追加する（ステップ1で完了）
- マイグレーション：不要。既存の SO アセットは新しいフィールドが既定値（Auto・PassThrough・autoReleaseHandle ON・キャッシュ対策 OFF）で補われ、`OnValidate` でも null を補完する。既定値では Load の挙動は変わらない
- セーブデータ・CSV・API スキーマの変更：なし

## 4. 影響範囲の分析

- 既存機能への影響：
  - `LoadingController` のコンストラクタ変更（呼び出し元は Manager・`LoadingControllerTestBase`・`LoadingControllerPollingTests` の3か所）
  - `LoadAsync` / `LoadByLabelAsync` の先頭に待機が入る。ゲートが開いているとき（既定の `PassThrough` で初期化に失敗していない場合）は `await`・確保なしで、従来と同じ動作
  - 初期化に失敗した後だけ、Load が `AddressableInitializeException` で止まる（従来は Addressables のエラー）。`Release` は影響しない
  - Manager の `Awake` / `OnDestroy` の順序変更。既定設定では、`Awake` で初期化が始まる点が追加される
  - サンプル（AddressableLoad）：既定設定のままなら動作は変わらない
- 永続的ドキュメント（Docs/）への影響：
  - 実装後に突き合わせ、ユーザーの指示で次のとおり更新した（2026-10-05）
    - `src/AddressableManager/Docs/specs/initialize-spec.md`：実装上の判断を追記（2.1・2.2 の購読の順序、4章の進捗、7.1 の責務、7.3 の共有タスクと `Dispose`、7.6 の通知先の登録時機、7.7「イベントの発火」を新設、11章の PlayMode の行）。10.1 最終行（`.hash` の実機確認）は未確認のまま
    - `src/AddressableManager/Docs/specs/loading-spec.md`：2章（Manager の箇条書き・コード例を現在の実装に差し替え、`OnDestroy` の順序）、3.1 のコンストラクタと `LoadAsync` のコード例、13.2 の `LoadByLabelAsync` のコード例にゲートの待機を追加、12章のテスト方針に偽のゲートを追加
    - `src/AddressableManager/Docs/addressable-manager-design.md`：4章のフォルダ図に `Initialize/Model/` を追加し、「Initialize には Model が実質存在しない」の記述を修正。更新履歴に1行
    - `src/AddressableManager/Docs/conventions.md`：7.2 の型分類表と10章のレイヤー例に、初期化機能の型を追加
    - `src/AddressableManager/Docs/diagrams/initialize-class.md`・`initialize-sequence.md`・`loading-class.md`・`loading-sequence.md`：メンバー名（`GetPercentComplete()`・内部イベント等）、購読と状態遷移の順序、Loading のゲートへの依存と待機を反映
    - `Docs/glossary.md`：「URL キャッシュ対策」の説明を確定仕様（`.hash` のみ・要求ごとに異なる値）に修正
    - `CHANGELOG.md` は担当者の運用のため更新しない。利用者向けドキュメント（`Documentation~/`・モジュールの README）はまだ存在しない
- テストへの影響：
  - 既存の Loading の EditMode テスト：コンストラクタの引数追加のみ（2ファイル）
  - 新規：EditMode 5ファイル＋偽物2ファイル、PlayMode 1ファイル
  - 既存の `LoadingPlayModeTests`・`AddressablesInitializeBehaviorTests` は回帰として再実行

## 5. 図表（必要な場合）

クラス図・シーケンス図は恒久の図として作成済み。ここにはパスだけを記載する。
- クラス図：`src/AddressableManager/Docs/diagrams/initialize-class.md`
- シーケンス図：`src/AddressableManager/Docs/diagrams/initialize-sequence.md`（自動・手動・WaitForInitialize・状態遷移）

実装計画書で検討した、配線順序（同期完了時のイベント）と、待機中の破棄・寿命終了の流れは、実装後に図と食い違いが無いかを確認し、図に載っていない場合は `initialize-sequence.md` への追記を提案する。
