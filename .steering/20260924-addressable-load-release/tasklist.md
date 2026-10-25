# タスクリスト：AddressableManager 読み込み・解放機能

> 対象：`design.md`（承認済み版） ／ 最終更新：2026-09-30

## タスク

状態：⚪未着手 → 🔵作業中 → ✅完了（🔴ブロック中はブロック理由を備考へ）

| No | タスク | 状態 | 完了条件 | 備考 |
|---|---|---|---|---|
| 1 | 機能仕様書・クラス図・シーケンス図の作成 | ✅ | `loading-spec.md`／`addressable-path-attribute-spec.md`／`diagrams/` 一式・`CLAUDE.md` 更新が完了し、承認済み | `src/AddressableManager/Assets/Docs/` へ配置済み |
| 2 | `.steering` 3点セットの作成 | ✅ | `requirements.md`／`design.md`／`tasklist.md` の3ファイルとも承認される | 本タスク自体 |
| 3 | 共通の土台と Service の移植 | ✅ | `AsyncCompat`／`AddressableLogger`／`AssetKey`／`AssetLoadException`／`AddressableRuntimeSettingsSO`／`AddressableLoadingSettings`／`IAssetLoaderWorker`／`IAssetLoaderServiceFactory`／`AssetLoaderServiceFactory`／`AddressableLoaderBase<T>`／`AssetLoaderService<T>` を作成し、Samples の移植元2ファイルを削除。シンボル定義あり・なし両方でコンパイルが通る | |
| 4 | Manual 寿命での読み込み・明示解放と Manager | ✅ | `AssetTicket<T>`／`LifetimeKind`／`AssetLifetime`（Manual のみ）／`LifetimeScopeId`／`AssetEntryModel`／`AssetRegistryModel`／`LoadingController`／`IAssetHandler`／`AssetHandler` を作成し、`AddressableManager` を書き換える。コンパイルが通り、該当範囲の EditMode テストが通る | 実装・コンパイル確認済み。**テストコードは asmdef 完成後に作成**（`NUnit.Framework`／`UnityEngine.TestRunner` は Test Assembly の asmdef が無いと参照できない）。`LoadingController` の3引数コンストラクタ（`IAssetLifetimeWatcherBinder` を含む）は step 6 で追加するまで、現状は2引数（設定・Serviceファクトリのみ）で実装している |
| 5 | Scene 寿命 | ✅ | `AssetLifetime.Scene()`／`Scene(Scene)`、`HandleSceneUnloaded`、Manager での `sceneUnloaded` 中継、DontDestroyOnLoad／未ロードシーンの扱いを実装。Scene 寿命の EditMode テストが通る | `Manager` の `sceneUnloaded` 中継は step 4 で実装済み。実装・コンパイル確認済み。テストは asmdef 完成後 |
| 6 | Owner 寿命 | ✅ | `AssetLifetime.Owner(...)`、`AssetLifetimeWatcher`／`IAssetLifetimeWatcherBinder`／`AssetLifetimeWatcherBinder`、`Tick` によるポーリングを実装。Owner 寿命とポーリングの EditMode テストが通る | `LoadingController` のコンストラクタが3引数（watcherBinder を含む）。実装・コンパイル確認済み。テストは asmdef 完成後 |
| 7 | リーク警告 | ✅ | `#if UNITY_EDITOR` で `IssuedTicketRecord` にスタックトレースを記録し、`ReleaseLifetime` で警告を出す。自動回収のたびに警告が1件ずつ出る | `AddressableLogger.LogLeakWarning` にスタックトレースの任意引数を追加。実装・コンパイル確認待ち。手動確認は tasklist の該当ステップ完了後に実施 |
| 8 | AddressablePath プルダウン属性 | ✅ | `AddressablePathAttribute`／`AddressablePathDrawer`／`AddressablePathDropdown`／`LoadingTestKeysSO` を作成。Inspector でプルダウン選択でき、存在しないアドレスに警告が出る。シンボル定義あり・なし両方でコンパイルが通る | 実装・コンパイル確認待ち。`Editor/` は Unity 標準の magic folder のため、asmdef が無い現状でも `Assembly-CSharp-Editor` として自動コンパイルされ `UnityEditor`／Addressables Editor API を参照できる（asmdef 作成後は明示的な参照設定に置き換わる） |
| 9 | PlayMode テストとサンプル | ✅ | PlayMode テスト一式・動作確認用サンプル（3シーン構成）を作成。手動検証の表をすべて満たし、本タスクリストのチェックがすべて埋まる | サンプルは当初の `AddressableLoadSample.cs` 1本から、画像・音（BGM / SE）・GameObject の3シーン構成（`AssetLoadSampleViewBase`／`SpriteLoadSampleView`／`AudioLoadSampleView`／`PrefabLoadSampleView`／`SampleSceneNavigatorView`）に作り直した（2026-09-29〜30。寿命ごとに別のアセットを読み込む形にし、`LoadingTestKeys.asset` のアドレスも `manual_sprite_load_test`／`manual_object_load_test` に更新）。EditMode/PlayMode テストとも作成済み。EditMode テスト（`AssetKeyTests`／`LoadingControllerLoadTests`／`LoadingControllerLifetimeTests`／`LoadingControllerPollingTests`）・PlayMode テスト（`LoadingPlayModeTests`）とも **Test Runner で実行し、Task 版・UniTask 版の両方でクリアを確認済み**。実装中に見つかった実バグ（`AssetLifetimeWatcher.OnDestroy` の列挙中コレクション変更、UniTask の複数await非対応による共有ロードタスクの破綻）を修正し、`LoadingController` の内部状態を `IssuedTicketRegistryModel`／`OwnerWatchRegistryModel` に切り出してコンストラクタ注入する形にリファクタリング済み（`loading-spec.md`・`diagrams/loading-class.md` に反映済み）。`AddressablePathAttribute` の Inspector プルダウン動作は手動検証待ち。3シーンのサンプルでの手動検証（Load / Release / 二重解放 / Scene 寿命の遷移回収 / Owner 寿命）と、EditMode/PlayMode テストの再実行は、2026-09-30 に問題なしを確認済み。なお Editor で Game ビューのフォーカス復帰後の最初のクリックが二重に発火することがあり、検証時は復帰後に一度クリックしてから始める（Editor の入力の癖として対応しない）。サンプルのため `AddressableLogger` を使う目的で `AssemblyInfo.cs` に `InternalsVisibleTo("MAH.AddressableManager.AddressableLoad")` を追加、`AddressableLogger` に `title` 引数（既定 `AddressableManager`、サンプルは `AddressableSample`）を追加した |

## 完了の定義（Definition of Done）

- [x] `requirements.md` の受け入れ条件をすべて満たす（`AddressablePathAttribute` の Inspector 手動確認の1件が残り。Scene 寿命の実アンロード確認は2026-09-30 に完了）
- [x] シンボル定義あり（UniTask 版）・なし（Task 版）の両方でコンパイルが通る（リント・型チェックに相当）
- [x] EditMode テスト・Editor 上で実行する PlayMode テストの追加が完了し、両方が Task 版・UniTask 版で通る
- [x] `src/AddressableManager/Assets/Docs/addressable-manager-design.md`／`specs/loading-spec.md`／`specs/addressable-path-attribute-spec.md`／`diagrams/` の更新が完了している（実装中にずれが生じた場合）
