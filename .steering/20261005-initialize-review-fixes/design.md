# 設計：初期化（Initialize）機能のレビュー指摘修正

> 対象：`requirements.md`（承認済み） ／ 作成日：2026-10-05 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.23f1

## 1. 実装アプローチ

新しい仕組みは足さず、既存の経路の「抜け」を塞ぐ形で直す。層の構成（Model・Controller・Service はピュア C#、Manager / View のみ MonoBehaviour）は変えない。

詳細な手順・例外の表は、仕様書を正とする（`src/AddressableManager/Docs/specs/initialize-spec.md`。更新は各タスクの中で行う）。

主な設計判断：
- **進捗の通知先の例外は、Model の `ReportProgress` で受け止める（🔴）**
  - 通知先ごとに `try/catch` し、例外は `AddressableLogger.LogException` に流して、次の通知先へ続ける
  - Model で受け止める理由：成功時の `ReportProgress(1f)` が `RaiseSucceeded`・`TrySetResult` より前にあり、`Tick` も `OnLateUpdate` の中で呼ばれる。呼び出し元（Controller）が2か所あるため、通知する側で守れば1か所で済む
  - 仕様書 7.7（イベントの購読側の例外は Controller が受け止める）と同じ考え方で、利用者のコードが状態遷移と共有タスクの完了を止めないようにする
  - リリースビルドでは `LogException` が `UNITY_ASSERTIONS` で消え、例外は黙って捨てられる。これはイベントの購読側と同じ扱い
- **キャッシュ対策の判定は「`?` より前のパス部分」で行う（🟡）**
  - `http` で始まることを先に判定し、そのあと `IndexOf('?')` でパスの終端を求め、`string.CompareOrdinal` で `.hash` との部分比較を行う（部分文字列を作らない）
  - `http` を先に見る理由：ローカルの InternalId が大半で、走査を対象になり得るものだけに絞るため
  - カタログ本体の URL は `ContentCatalogProvider` が `.Replace(".hash", ".bin")` で作る。クエリが付いていても本体の URL は正しく作られる
- **`Restore` で上書きされていた場合も `_previousTransform` を残す（🟢）**
  - 利用者の関数がこちらの関数を内部で呼ぶ作りだと、`Restore` の後もこちらの関数は呼ばれ続ける。`_previousTransform` を `null` にすると、`Awake` より前に設定されていた元の関数が通らなくなる
  - Warning の文面は、上書きした関数がこの関数を呼んでいない場合に限った内容にする
- **`Failed` の `InitializeAsync` は、失敗済みの共有タスクを `await` する（🟢）**
  - `throw _stateModel.AccFailure` は、投げるたびに同じインスタンスのスタックトレースを上書きする。`await` は保存済みのスタックトレースを保ったまま投げ直すため、「毎回同じ例外」という仕様は変わらない
  - `WaitUntilOpenAsync` の `Task.FromException` は投げ直しではないため変えない
- **自動モードの開始と例外の観測は Controller に移す（🟢）**
  - `InitializeController.StartAutoInitialize()` が `ObserveFault(InitializeAsync(null, None))` を呼ぶ。Manager は呼ぶだけにして、`OBSERVE_FAULT` と完全修飾の `System.Threading.Tasks.` を無くす（Manager は「ロジックを持たない」方針）
- **サンプルは失敗を購読し、購読前に失敗していた場合も確認する（🟡）**
  - 存在しないローカルのパスによる失敗は、`Awake` の中で同期的に終わる（仕様書 10.1）。購読前に失敗していた場合は例外を受け取れないため、画面には「購読前に失敗済み」と出す
  - delegate は `Awake` で一度だけ作る（既存の `_onInitialized` と同じ作り）。購読と解除は対で書く

## 2. 変更するコンポーネント

| ファイル | 層 | 変更 |
|---|---|---|
| `Runtime/Core/Initialize/Model/InitializeStateModel.cs` | Model | `ReportProgress` の `try/catch` |
| `Runtime/Core/Initialize/Controller/InitializeController.cs` | Controller | `Failed` の `await AccSharedTask`、`StartAutoInitialize()` 追加 |
| `Runtime/Core/Initialize/Service/CatalogCacheBustingService.cs` | Service | `IsTarget` の判定、`Restore` で `_previousTransform` を残す・Warning の文面 |
| `Runtime/Core/AddressableManager.cs` | Manager | `OBSERVE_FAULT`・private `StartAutoInitialize()` を削除し、Controller の呼び出しに置き換え |
| `Samples/AddressableInitialize/Scripts/InitializeSampleView.cs` | View | 失敗の購読・解除・確認、キャンセル時のコメント |
| `Tests/Editor/`（`InitializeStateModelTests`・`InitializeControllerTests`・`CatalogCacheBustingServiceTests`） | テスト | 追加と入力の修正 |
| `src/AddressableManager/Docs/specs/initialize-spec.md`、`diagrams/initialize-class.md`・`initialize-sequence.md` | ドキュメント | 該当章の更新 |

`<pkg>` = `src/AddressableManager/Assets/@MAH/AddressableManager`（上表の `Runtime/`・`Samples/`・`Tests/` は `<pkg>` の下）。

## 3. 検証方法

- **EditMode**：追加するテスト
  - `InitializeStateModelTests.ReportProgressContinuesAfterThrowingTarget`
  - `InitializeControllerTests.ThrowingProgressDoesNotBreakInitialization`・`ThrowingProgressOnTickDoesNotStopOtherTargets`・`StartAutoInitializeStartsAndObservesFailure`
  - `CatalogCacheBustingServiceTests`：クエリ付きの `.hash`、`?v=catalog.hash`、上書きした関数がこちらを呼ぶ形の `Restore`
  - 既存のテストがすべて通ること（特に `InitializeAsyncAfterFailureReturnsSameExceptionWithoutCallingService`・`LoadingControllerInitializationGateTests`）
- **PlayMode**：`InitializePlayModeTests` が通ること（Manager の自動開始を移した後の回帰確認）
- **手動検証**（`Samples/AddressableInitialize/Scenes/InitializeScene.unity` で Play）

| 手順 | 期待結果 |
|---|---|
| Play 開始直後に「Spawn Auto」を押す | ログ欄に `OnInitialized (count: 1)`、状態が `Succeeded`。Console にエラー・例外が無い |
| 「Destroy Manager」→ Play を止める | Console に購読解除漏れ・`MissingReferenceException` が出ない |
| Play し直して「Spawn Manual」→「Initialize」 | progress が最後に `1.00`、状態が `Succeeded` |
| 「Initialize」を押した直後（完了前）に「Destroy Manager」を押す | 「待機中に View または Manager が破棄された」旨のログが出て、例外は出ない |
| （失敗の確認）Play Mode Script を「Use Existing Build」にし、ビルドを消して（`Library/com.unity.addressables` を削除）Play →「Spawn Auto」 | 画面に `Initialize failed` の表示が出る（購読前に失敗していた場合も表示される）。状態が `Failed` |

## 4. リスク

- `IsTarget` に `IndexOf('?')` が加わる。`http` の判定を先に行うため、走査が増えるのはリモートの InternalId だけで、文字列は作らない
- `Failed` の `await` で、例外のスタックトレースは元の `TrySetException` の位置を指すようになる。挙動（毎回同じ例外）は変わらない
- キャッシュ対策の対象が広がる（これまで対象外だったクエリ付きの `.hash` が対象になる）。意図した変更
- Loading・公開 API・Prefab・シーンへの影響なし

## 5. 検討した代替案と却下理由

- **`InitializeController` 側で `ReportProgress` の呼び出しごとに `try/catch` する案**：呼び出し元が `RunAsync` と `Tick` の2か所あり、書き漏らしやすい。通知する側（Model）で守るほうが確実なため却下
- **進捗の例外を握りつぶさず、`InitializeAsync` の呼び出し元に伝える案**：通知先は待機中の複数の呼び出し元で、1人の `IProgress` の不具合が他の全員の初期化を止めてはいけない（仕様書 4章の「`ct` は呼び出し元の待機だけを打ち切る」と同じ考え方）ため却下
- **`IsTarget` を正規表現で判定する案**：変換関数は全ロケーションで呼ばれ、確保が増えるため却下
- **`Failed` の例外を呼び出しごとに新しい例外で包み直す案**：「毎回同じ例外」という仕様（4章）が変わり、`InnerException` の連鎖が深くなるため却下
- **Manager の `OBSERVE_FAULT` を残して重複を許容する案**：Manager が Task の扱いを持つことになり、「ロジックを持たない」方針に反するため却下
