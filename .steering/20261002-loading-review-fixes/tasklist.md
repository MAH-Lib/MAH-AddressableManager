# タスクリスト：Loading 機能のレビュー指摘修正

> 対象：`design.md`（承認済み版） ／ 最終更新：2026-10-02（全タスク完了） ／ 対象 Unity バージョン：6000.3.23f1

## タスク

状態：⚪未着手 → 🔵作業中 → ✅完了（🔴ブロック中はブロック理由を備考へ）

各タスクの完了ごとに、ユーザーが確認する。確認が済むまで、次のタスクに進まない。

| No | タスク | 状態 | 完了条件 | 備考 |
|---|---|---|---|---|
| 0 | `.steering` 3点セットの作成 | ✅ | `requirements.md`／`design.md`／`tasklist.md` の3ファイルとも承認される | 本タスク自体。3ファイルとも承認済み（2026-10-02） |
| 1 | 🔴 `AddressableLoaderBase` のロード中 Dispose 対応 | ✅ | `_isLoading`／`_isDisposed`／`ThrowIfDisposed` を追加し、`Dispose` の保留と、`LoadAsync` の3か所の確認を実装する。PlayMode テスト「ロード中に Manager を破棄」を追加する。コンパイルが通り、追加したテストと既存の全テストが成功する | 対象：`Runtime/Core/Loading/Service/AddressableLoaderBase.cs`、`Tests/Runtime/LoadingPlayModeTests.cs`。Task 版・UniTask 版の両方の分岐を触る（UniTask 版のみ確認。Task 版はユーザーが確認する）。ロードが即座に完了して再現できない場合は `Assert.Inconclusive` |
| 2 | 🟢 回復不能な例外のリトライ除外 | ✅ | `IsUnrecoverable` を追加し、`InvalidKeyException` はリトライしない。PlayMode テストの名前とコメントを改める。存在しないアドレスのテストが、リトライ待機なし（`_retryDelayMs` 未満）で `AssetLoadException` になる | `InvalidKeyException` が `OperationException` に包まれて届くかは、実機の Play で1回確認する（要件・設計のリスク）。**確認済み（2026-10-02）**：存在しないアドレスの PlayMode テストで、Console に `InvalidKeyException`（UniTask 版では `ToUniTask` から直接届く）が1回だけ記録され、続けて「キーが無効」のエラーが出た。リトライ用の警告は出ず、3件とも同じ秒（11:08:21）に記録されている（リトライ待機なし） |
| 3 | 🟡 `LoadAsync` のキー事前チェックと、`ReleaseLifetime` の自己修復 | ✅ | `LoadAsync` が `ResolveScope` の前にキーの null／空を確認する。`ReleaseLifetime` が、チケットの有無にかかわらず最後に `UnwatchOwnerIfNeeded` を呼ぶ。古いコメントを削除する。EditMode テストを追加し、「default キー＋Owner 寿命」の後に `WatcherBinder.BoundCount == 0` で、`Tick` を繰り返しても再走査されない | 対象：`LoadingController.cs`、`Tests/Editor/LoadingControllerLifetimeTests.cs` |
| 4 | 🟡 Binder の辞書化と、Watcher の `ToArray()` 除去 | ✅ | `AssetLifetimeWatcherBinder` が `Dictionary<LifetimeScopeId, AssetLifetimeWatcher>` で管理し、`Unbind` が owner の破棄済みでも先に辞書から外す。`AccBoundCount` を追加する。`AssetLifetimeWatcher.OnDestroy` が確保 0 になる。`AssetLifetimeWatcherBinderTests` を新設し、破棄済み owner への `Unbind` 後に `AccBoundCount == 0`、同じ owner への2回目の `Bind` で Component が1つのまま。既存の Owner 寿命のテストがすべて成功する | 対象：`AssetLifetimeWatcherBinder.cs`、`AssetLifetimeWatcher.cs`、`Tests/Editor/AssetLifetimeWatcherBinderTests.cs` |
| 5 | 🟡 リーク警告を受け渡し済みの分に限る | ✅ | `IssuedTicketRecord` に受け渡し済みの印（`AccIsDelivered`／`AsDelivered()`）、`IssuedTicketRegistryModel` に `MarkDelivered` を追加する。`LoadAsync`・`LoadByLabelAsync` が、結果を返す直前に `MarkDelivered` を呼ぶ。`ReleaseLifetime` が、受け渡し済みの分だけリーク警告を出し、未受け渡しの分は `Log` にする（まとめチケットの `Invalidate` は常に行う）。`LoadByLabelAsyncLifetimeEndsWhileLoadingThrowsOperationCanceled` の期待値を 0件に変え、単体の Load の受け渡し前（警告 0件）・受け渡し後（警告 1件）のテストを追加する | 対象：`IssuedTicketRecord.cs`、`IssuedTicketRegistryModel.cs`、`LoadingController.cs`、`Tests/Editor/LoadingControllerLabelTests.cs`、`Tests/Editor/LoadingControllerLifetimeTests.cs`。`IssuedTicketRecord` は readonly struct のため、複製を返して差し替える。スタックトレースは複製元を引き継ぐ |
| 6 | 🟢 `AssetGroupTicket.Fill` の引数変更 | ✅ | `Fill(string[], Type[], Object[], long[])` に変え、`using MAH.AddressableManager.Core.Loading;` を削除する。`LoadByLabelAsync` と `AssetGroupTicketTests` を追従させる。Label 関連の全テストが成功する | 対象：`AssetGroupTicket.cs`、`LoadingController.cs`、`Tests/Editor/AssetGroupTicketTests.cs` |
| 7 | 🟡🟢 `unscaledDeltaTime` と `ct.Register` の static ラムダ化 | ✅ | `AddressableManager.LateUpdate` が `Time.unscaledDeltaTime` を渡す。`AsyncCompat.WithCancellation`（`Task`・`Task<T>`）が static ラムダ＋状態引数になる。コンパイルが通り、全テストが成功する | 対象：`AddressableManager.cs`、`AsyncCompat.cs`。`timeScale = 0` 中のポーリングは、手動検証（実機の Play）で確認する |
| 8 | 仕様書の更新 | ✅ | `loading-spec.md` の3章（ロード中の Dispose・リトライしない例外）、5.4・5.7（`unscaledDeltaTime`）、10章・13.7（リーク警告の条件）、13章（`Fill` の引数）を、実装に合わせて更新する。`diagrams/loading-class.md` に `Fill` の引数が載っていれば追従する | 計画のとおり、サンプルコード（59行目ほか）も合わせて直す |
| 9 | 最終確認 | ✅ | EditMode・PlayMode の全テストが成功する。UniTask 導入時の再実行も成功する。設計書の手動検証の表（存在しないアドレス・ロード中の Manager 破棄・Owner 寿命のリーク警告・ロード中の Owner 破棄・`timeScale = 0`）が、期待どおりになる | 手動検証は、ユーザーと一緒に Unity Editor で行う |

## 完了の定義（Definition of Done）

- [x] `requirements.md` の受け入れ条件をすべて満たす
- [x] UniTask 版・Task 版の両方でコンパイルが通り、自動テスト・手動テストとも問題なし（Task 版は、2026-10-02 にユーザーが確認済み）
- [x] EditMode・PlayMode テストの修正・追加が完了し、すべて通る
- [x] 影響のある永続的ドキュメント（`src/AddressableManager/Assets/Docs/specs/loading-spec.md`）の更新が完了している
