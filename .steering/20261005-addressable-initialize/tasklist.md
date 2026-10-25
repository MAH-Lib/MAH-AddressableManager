# タスクリスト：AddressableManager 初期化（Initialize）機能

> 対象：`design.md`（承認済み版） ／ 最終更新：2026-10-05 ／ ステータス：承認済み

## タスク

状態：⚪未着手 → 🔵作業中 → ✅完了（🔴ブロック中はブロック理由を備考へ）

各タスクは1コミット単位で、完了時点でプロジェクトが動く状態を保つ。**各タスクの完了ごとに、ユーザーが確認してから次へ進む**。

**Claude の担当は実装（コードとテストコードの作成）まで**。コードレビュー・手動検証・永続的ドキュメントの最終確認は担当外で、ユーザー（担当者）が行う。

| No | タスク | 状態 | 完了条件 | 備考 |
|---|---|---|---|---|
| 1 | Data・Definition・例外・ゲートの追加（`InitializeState`・`InitializeMode`・`LoadBeforeInitializePolicy`・`AddressableInitializeSettings`・`AddressableInitializeException`・`IInitializationGate`）と、Runtime SO への `_initialize` 追加 | ✅ | コンパイルが通る。Runtime SO の Inspector に「初期化」見出しと4項目が既定値で表示される | 実装済み。ユーザーの Unity での確認待ち |
| 2 | `InitializeStateModel` の追加と EditMode テスト（`InitializeStateModelTests`） | ✅ | 遷移（`NotStarted→Running→Succeeded/Failed`）・終端からの遷移が無視される・`MarkRunning` の二重呼び出しが無視される・進捗の通知先の登録/解除のテストが全件成功 | 実装済み・ユーザー確認済み |
| 3 | `IAddressablesInitializeService`・`AddressablesInitializeService`・`InitializeController`（ゲート実装を含む）と、偽 Service（`FakeAddressablesInitializeService`）・EditMode テスト（`InitializeControllerTests`・`InitializeControllerGateTests`） | ✅ | 共有タスク（2回呼んでも初期化は1回）・呼び出し元のキャンセルが他に影響しない・失敗が `await` と `OnFailed` の両方に届く・`Failed` 後に Service を呼ばず同じ例外を返す・同期例外が `Failed` になる・`autoReleaseHandle=false` の `Dispose` で解放・進捗と完了時の 1・ゲートの4状態×2ポリシーのテストが全件成功 | 実装済み・ユーザー確認済み。結果は `Completed` の中で読む（仕様 7.5）。`AsyncCompat.ToTask` は使わない |
| 4 | `CatalogCacheBustingService` と EditMode テスト（`CatalogCacheBustingServiceTests`） | ✅ | `.hash` のみ・`http` のみ変換・連番が進む・既存クエリ時は `&`・既存関数との連結・復元（自分の関数のままなら戻す／上書きされていたら戻さず Warning）のテストが成功 | 実装済み。EditMode 24件成功を確認。テストの SetUp / TearDown で `InternalIdTransformFunc` を退避・復元する |
| 5 | `LoadingController` にゲートを注入し、`LoadAsync` / `LoadByLabelAsync` に待機を追加（`WaitForInitializationGateAsync`・`IsLifetimeAlive`）。偽ゲート（`FakeInitializationGate`）を作成し、既存テストの生成箇所を修正。Manager は Model・Controller・Service を生成して LoadingController に渡すところまで入れる | ✅ | 既存の Loading EditMode テストがすべて成功。新規 `LoadingControllerInitializationGateTests`（仕様 5章の表の各マス・待機中のキャンセル／破棄→`ObjectDisposedException`／寿命終了→`OperationCanceledException`・待機前に無効な寿命は `ArgumentException`・`Failed` 時に何も残らない）が成功 | 実装済み。EditMode 全171件成功を確認（既存＋新規）。Manager は Controller 生成・ゲート注入・Dispose 順序まで。`loading-spec.md` は 3.1・13.2 に追記済みのため更新不要 |
| 6 | `AddressableManager` の残りの配線（Awake の順序・公開 API〔`AccInitializeState`・`AccIsInitialized`・`OnInitialized`・`OnInitializeFailed`・`InitializeAsync`〕・イベント中継・`Tick` 購読・自動開始と例外観測・`OnDestroy` の順序・変換関数の Apply / Restore） | ✅ | コンパイルが通る。既存の `LoadingPlayModeTests` が成功。既定設定のサンプルが従来どおり動く | 実装済み・ユーザー確認済み（PlayMode に失敗なし）。購読 → 自動開始の順序を守る（同期完了のため）。UniTask 版のシグネチャは `#if` で出し分ける |
| 7 | PlayMode テスト `InitializePlayModeTests`（自動で `Succeeded`・手動で `InitializeAsync` 後に `Succeeded`・イベント発火・Load ゲート・URL キャッシュ対策・破棄）の作成。あわせて、テスト作成中に見つかった `InitializeController.InitializeAsync` の進捗の不具合を修正し、EditMode テストを追加 | 🔵 | `InitializePlayModeTests` が作成され、PlayMode テストが全件成功する。既存 `AddressablesInitializeBehaviorTests`・`LoadingPlayModeTests` も成功する | `InitializePlayModeTests` は作成済み。ユーザーが実行し、「初期化中の Manager の破棄」のテストだけが Inconclusive（この環境では Addressables の初期化が同期的に終わり、実行中を再現できないため）。再現できないテストは常に Inconclusive になるため PlayMode から削除し、偽 Service を使う EditMode テスト（`DisposeCancelsWaitingCallersAndKeepsState`）で確認する形にした。残りの結果はユーザー確認待ち。EditMode 全173件成功を確認（修正前は171件）。修正内容：Addressables が初期化済みで同期完了する場合、進捗の通知先を登録する前に完了して最後の 1 が届かなかったため、初期化を始める前に登録するよう変更（成功済みの呼び出しにも 1 を通知）。**2026-10-05 に PlayMode の実行中、Unity Editor がネイティブクラッシュ**（Unity Profiler の記録バッファ超過 → Addressables の `ProfilerRuntime` が LateUpdate で Profiler API を呼んで segv。本実装のコードは関係しない環境要因）。Profiler の記録を止めてから、ユーザーが PlayMode テストを実行する。`code-reviewer` によるレビューは、ユーザー指示により実施しない |
| 8 | 手動検証と、永続的ドキュメントの最終確認 | ⚪ | 実装計画書の手動検証表の項目を実施。実装と図・仕様書の食い違いが無い（あれば更新提案を出して承認後に更新）。`.hash` の実機確認ができた場合のみ仕様 10.1 を更新 | **Claude の担当外**（ユーザーが実施）。ただし Docs の食い違いの修正は、ユーザーの指示で Claude が実施した（2026-10-05。対象は design.md「4. 影響範囲の分析」に記載）。リモートカタログ環境が無ければ `.hash` の確認はスコープ外。実装中に分かった図・仕様書との差：進捗の取得を `AccPercentComplete` ではなくメソッド `GetPercentComplete()` にした（`initialize-class.md` はメンバーが概略表記のため未更新） |

## 完了の定義（Definition of Done）

- [x] requirements.md の受け入れ条件をすべて満たす
- [x] コンパイルエラー・警告が無く、Unity の Console にエラーが出ていない
- [x] 関連テスト（EditMode・PlayMode）の修正・追加が完了し、すべて成功する（EditMode は成功を確認済み。PlayMode はユーザーの実行待ち）
- [x] 影響のある永続的ドキュメント（Docs/）の確認・更新が完了している（タスク8。Claude の担当外）
- [x] `Core/Initialize/` と `Core/Loading/` が互いを `using` していない
- [x] `CHANGELOG.md` は更新していない（担当者の運用）
