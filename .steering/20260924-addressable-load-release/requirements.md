# 要求内容：AddressableManager 読み込み・解放機能

> 作業ディレクトリ：`.steering/20260924-addressable-load-release/` ／ 作成日：2026-09-24 ／ ステータス：承認待ち
> 関連する永続的ドキュメント：`src/AddressableManager/Assets/Docs/addressable-manager-design.md`（3.1・3.4〜3.8 を概要化し、詳細を機能仕様書へ切り出し済み）／`src/AddressableManager/Assets/Docs/specs/loading-spec.md`（新規。今回の機能の正）／`src/AddressableManager/Assets/Docs/specs/addressable-path-attribute-spec.md`（新規）／`CLAUDE.md`（ドキュメント参照・置き場所ルールを更新済み）

## 1. 変更・追加する機能の説明

モジュール利用者（実装担当者）が Addressables の handle や参照カウントを意識せずにアセットを読み込み・解放できるようにする。`AddressableManager` に読み込み（`LoadAsync<T>`）と解放（`Release`）を実装し、Load 時に指定した寿命（Manual / Scene / Owner）の終了時点で未解放のものを自動回収するセーフティネットも備える。現状 `AddressableManager.cs` はテンプレートのままで、移植元の Service 実装（`AddressableLoaderBase<T>` 等）は依存先が無くコンパイルが通っていない状態のため、今回これらを実装する。

## 2. ユーザーストーリー

- 実装担当者として、Addressables の handle や参照カウントを意識せずに済むように、`LoadAsync<T>` を呼ぶだけでアセットとチケットを受け取りたい
- 実装担当者として、解放を呼び忘れても致命的なリークにならないように、Load 時に指定した寿命の終了時に自動で回収してほしい
- 実装担当者として、自動回収に頼った実装に気づけるように、明示解放されずに回収された箇所をエディタ上で警告してほしい
- レビュアーとして、意図しない二重ロード・二重解放が起きないように、同じキーへの型違いの要求やチケットの使い回しを構造的に防いでほしい

## 3. 受け入れ条件

- [x] `LoadAsync<T>(key, lifetime, ct)` を呼ぶと `AssetTicket<T>` が返り、`ticket.AccAsset` で読み込んだアセットを取得できる
- [x] 同じ `AssetKey` を2回 `LoadAsync` すると、実ロードは1回だけ走り、RefCount が2になる
- [x] 同じ `AssetKey` を異なる型で `LoadAsync` すると `InvalidOperationException` になる
- [x] `Release(ticket)` を呼ぶと該当分の RefCount が減り、0になった時点で Addressables 側が解放される
- [x] 同じチケットを2回 `Release` しても、2回目は警告のみで他の確保分は解放されない（`default` のチケットも同様）
- [x] `AssetLifetime.Manual` で確保したアセットは、`Release` を呼ぶまで・または Manager 破棄まで解放されない
- [x] `AssetLifetime.Scene(...)` で確保したアセットは、そのシーンがアンロードされた時点で未解放分が自動回収される（EditModeでの`HandleSceneUnloaded`呼び出しは自動テスト済み。実際のシーンのアンロードは、サンプルの3シーン間の遷移で手動確認済み・2026-09-30）
- [x] `AssetLifetime.Owner(go)` で確保したアセットは、その GameObject が破棄された時点で未解放分が自動回収される（Watcher の `OnDestroy`、または既定3秒間隔のポーリングのどちらかで）
- [x] ロード中に呼び出し元が全員キャンセルしても、実ロードは中断されず、ロード完了後に Worker が解放される
- [x] リトライを使い切って失敗した場合、待機中の呼び出し元全員に `AssetLoadException` が届き、次の `LoadAsync` は新しく実ロードを開始する
- [x] エディタ実行時、自動回収されたチケットについて、確保時のスタックトレース付きの警告ログが出る（本番ビルドではこの記録処理がコードごと除外される）
- [x] 2つ目の `AddressableManager` を配置すると、2つ目は破棄され `AccInstance` は最初の Manager のまま
- [ ] `AddressablePathAttribute` を付けた string フィールドが、Inspector 上でプルダウンから選択でき、存在しないアドレスには警告が出る（Inspector上での手動確認待ち）
- [x] EditMode テスト・Editor 上で実行する PlayMode テストの両方が、Task 版・UniTask 版どちらのシンボル設定でも通る

## 4. 制約事項

- 非同期処理の既定は `System.Threading.Tasks.Task`。UniTask は `#if ADDRESSABLE_UNITASK_SUPPORT` の出し分けの中でのみ使用し、直接 `using` しない（CLAUDE.md「設計の絶対ルール」）
- VContainer・R3・その他 DI/Rx コンテナに依存させない
- MonoBehaviour を継承するのは `AddressableManager`（Manager）と `AssetLifetimeWatcher`（View）のみ。Controller・Service・Model はピュア C#
- `Instantiate` / `ReleaseInstance` 相当の API を追加しない（`LoadAsync<GameObject>` は Prefab アセット本体を返す）
- asmdef の作成はユーザー側で行う（Claude は作成しない）。Task/UniTask 両対応のコードのみ用意する
- Editor 拡張は読み取り専用の原則を崩さない（今回はリーク警告の記録・ログ出力までとし、EditorWindow での一覧表示は対象外）
- 命名・namespace は `src/AddressableManager/Assets/Docs/conventions.md`、用語は `Docs/glossary.md`（リポジトリルート）に従う

## 5. スコープ外（今回やらないこと）

- InitializeController / DownloadController / `IDownloadHandler`（Download 導入時に実装。読み込み・解放は `InitializeAsync` に依存しない設計とする）
- Editor 拡張のうち、デバッグウィンドウ・リーク検出一覧の可視化・EditMode でのサイズ概算
- Profiling（計測・オーバーレイ）
- 利用者向けのキー定義 SO（`AddressablePathAttribute` 自体は今回実装するが、それを使った利用者向けキー管理 SO は別途）
- `LoadingController` を利用者に公開する低レベル API（今回は internal のまま）
- `Tick` 購読の動的な付け外し（先頭での早期リターンのみ）
- `package.json`・利用者向けドキュメント（`Documentation~/` 等）・`CHANGELOG.md` の更新
