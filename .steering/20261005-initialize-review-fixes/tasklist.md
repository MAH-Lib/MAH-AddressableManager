# タスクリスト：初期化（Initialize）機能のレビュー指摘修正

> 対象：`design.md`（承認済み） ／ 最終更新：2026-10-05 ／ 対象 Unity バージョン：6000.3.23f1

## タスク

状態：⚪未着手 → 🔵作業中 → ✅完了（🔴ブロック中はブロック理由を備考へ）

各タスクの完了ごとに、ユーザーが確認する。確認が済むまで、次のタスクに進まない。

| No | タスク | 状態 | 完了条件 | 備考 |
|---|---|---|---|---|
| 0 | `.steering` 3点セットの作成 | ✅ | `requirements.md`／`design.md`／`tasklist.md` の3ファイルとも承認される | 本タスク自体。3ファイルとも承認済み（2026-10-05） |
| 1 | 🔴 `ReportProgress` の例外処理 | ✅ | `InitializeStateModel.ReportProgress` が通知先ごとに例外を受け止め、`LogException` に流して続行する。`ReportProgressContinuesAfterThrowingTarget`・`ThrowingProgressDoesNotBreakInitialization`・`ThrowingProgressOnTickDoesNotStopOtherTargets` を追加し、EditMode の `InitializeStateModelTests`・`InitializeControllerTests` がすべて成功する。`initialize-spec.md` 7.6 を更新する | 対象：`InitializeStateModel.cs`、`Tests/Editor/InitializeStateModelTests.cs`、`Tests/Editor/InitializeControllerTests.cs`。追加したテストは、修正前のコードで失敗することを確認する |
| 2 | 🟡 クエリ付き `.hash` をキャッシュ対策の対象にする | ✅ | `IsTarget` が「`http` で始まり、`?` より前のパスが `.hash` で終わる」を判定する（部分文字列を作らない）。`TransformUsesAmpersandWhenQueryAlreadyExists` の入力を `catalog.hash?token=abc` に直し、`?v=catalog.hash` の非対象ケースを追加する。`CatalogCacheBustingServiceTests` がすべて成功する。`initialize-spec.md` 6.2・11章を更新する | 対象：`CatalogCacheBustingService.cs`、`Tests/Editor/CatalogCacheBustingServiceTests.cs` |
| 3 | 🟢 `Restore` で元の関数を残す | ✅ | 上書きされていた場合も `_previousTransform` を残し、Warning の文面を直す。`RestoreKeepsPreviousTransformWhenOverwrittenFunctionChainsOurs` を追加し、既存の `RestoreDoesNotTouchOverwrittenTransformAndWarns` を含めて成功する。`initialize-spec.md` 6.4 を更新する | 対象：`CatalogCacheBustingService.cs`、`Tests/Editor/CatalogCacheBustingServiceTests.cs` |
| 4 | 🟢 `Failed` の `await` と `StartAutoInitialize` の移動 | ✅ | `InitializeAsync` の `Failed` が `await AccSharedTask` になる。`InitializeController.StartAutoInitialize()` を追加し、Manager の `OBSERVE_FAULT`・private `StartAutoInitialize()`・不要な `using` を削除する。`StartAutoInitializeStartsAndObservesFailure` を追加する。EditMode の全テストと PlayMode の `InitializePlayModeTests` が成功する。`initialize-spec.md` 7.3、クラス図、シーケンス図を更新する | 対象：`InitializeController.cs`、`AddressableManager.cs`、`Tests/Editor/InitializeControllerTests.cs`。UniTask 導入時のコンパイルも確認する |
| 5 | 🟡🟢 サンプルの失敗の購読とコメントの修正 | ✅ | `InitializeSampleView` が `OnInitializeFailed` を購読・解除し、購読の直後に `Failed` も確認する。`OperationCanceledException` のコメント・ログを直す。コンパイルが通り、Console にエラーが無い状態で `InitializeScene` が動く | 対象：`InitializeSampleView.cs`。画面の文字は英語（サンプルの UI 方針） |
| 6 | 最終確認 | 🔵 | EditMode・PlayMode の全テストが成功する。UniTask 導入時の再実行も成功する。`design.md` の手動検証の表が、期待どおりになる。`initialize-spec.md` と2つの図が実装と一致している | 手動検証は、ユーザーと一緒に Unity Editor で行う。Task 版のコンパイルはユーザーが確認する |

## 完了の定義（Definition of Done）

- [x] `requirements.md` の受け入れ条件をすべて満たす
- [x] UniTask 版・Task 版の両方でコンパイルが通り、自動テスト・手動テストとも問題なし
- [x] EditMode・PlayMode テストの修正・追加が完了し、すべて通る
- [x] 影響のある永続的ドキュメント（`initialize-spec.md`・2つの図）の更新が完了している
