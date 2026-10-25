# 要求内容：AddressableManager ダウンロード（Download）機能

> 作業ディレクトリ：`.steering/20261008-addressable-download/` ／ 作成日：2026-10-08 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.23f1
> 関連する永続的ドキュメント：`src/AddressableManager/Docs/addressable-manager-design.md`（3.3・4章・6章・7章を更新。進捗の型を `IProgress<float>` から `IProgress<DownloadStatus>` に変える）／`src/AddressableManager/Docs/specs/download-spec.md`（新規。この機能の正）／`src/AddressableManager/Docs/conventions.md`（7.2 に新しい型の namespace 分類を追加）／`Docs/glossary.md`（用語の追加・`CheckAndUpdateCatalogAsync` の改名）／`src/AddressableManager/Docs/diagrams/`（`download-sequence.md`・`download-class.md` を新規作成し `index.md` に登録）／`src/AddressableManager/Docs/specs/initialize-spec.md`（`IInitializationGate` にメソッドを足すため、7.4・9.3 の記述を更新）／`src/AddressableManager/Docs/specs/loading-spec.md`（影響なし。実装時に確認）

## 1. 変更・追加する機能の説明

`AddressableManager` に、リモートのカタログの更新確認と、更新分のアセットのダウンロードを行う機能（`DownloadController`）を追加する。利用者は `IAddressableHandler` の次のメソッドだけで、「更新があるかの判定 → 件数と容量の確認 → ダウンロードと進捗の表示」を行える。

- `HasUpdateAsync`：更新データがあるかどうかだけを返す（カタログは更新しない）
- `CheckUpdateAsync`：カタログを確認・更新し、更新されたカタログ数・ダウンロードが必要なバンドル数・合計バイト数（`DownloadCheckResult`）を返す
- `DownloadAsync`：対象 Label のアセットをダウンロードする。段階・バイト数・割合・リトライ回数（`DownloadStatus`）を `IProgress<DownloadStatus>` で通知する
- `AccDownloadStatus`：現在の `DownloadStatus`（取得した時点の値のコピー）を取得する

対象 Label・カタログ更新の方針・リトライ回数・待機時間は Runtime SO（`AddressableRuntimeSettingsSO`）の `AddressableDownloadSettings` で設定でき、Label は引数で上書きもできる。

今やる理由：今は、ダウンロードの流れ（カタログ確認 → 更新 → サイズ取得 → ダウンロード → handle 解放）を、利用者が Addressables の API から毎回書く必要がある。handle の解放漏れ・進捗の取得・リトライが各プロジェクトに散らばり、設計仕様書 3.3 でも Download 実装時に決めると保留されている。

## 2. ユーザーストーリー

- 実装担当者として、確認ダイアログに「N 件 / xx MB」を出してからダウンロードするかどうかを選ばせるために、更新されたカタログ数・ダウンロードが必要なバンドル数・合計バイト数を受け取りたい
- 実装担当者として、進捗バーとダウンロード中の表示を出すために、段階（確認中・更新中・サイズ計算中・ダウンロード中・完了・失敗・キャンセル）とバイト数・割合・リトライ回数を、数値でリアルタイムに受け取りたい
- 実装担当者として、更新があるかどうかだけで画面の表示を切り替えるために、カタログを更新せずに、更新データの有無を `bool` で知りたい
- 実装担当者として、成功・失敗・キャンセルで表示を変えるために、最後の通知から、どの結果で終わったかを区別したい
- 実装担当者として、Addressables の API（handle の解放・進捗の取得・リトライ）を意識せずに済むように、対象 Label・カタログ更新の方針・リトライ回数を SO の設定だけで決めたい
- 実装担当者として、ゲーム内の追加ダウンロード（イベント配信等）に使えるように、対象の Label を引数で渡して SO の設定を上書きしたい
- レビュアーとして、Download と Initialize・Loading が互いを参照しないように、初期化の待機が `Core/` 直下の `IInitializationGate` だけを通っていてほしい

## 3. 受け入れ条件

### 更新の確認（`CheckUpdateAsync`）
- [ ] 戻り値の `DownloadCheckResult` が、更新されたカタログ数・ダウンロードが必要なバンドル数・合計バイト数・`AccIsCatalogUpdatePending`・`AccHasDownload`（バンドル数 > 0）を持つ
- [ ] 方針 `None` では、カタログの確認も更新も行わず、今のカタログで件数と容量だけを返す
- [ ] 方針 `CheckOnly` では、カタログの更新（`UpdateCatalogs`）を行わず、`AccIsCatalogUpdatePending` が更新の有無を示す
- [ ] 方針 `CheckAndUpdate`（既定）では、更新のあるカタログがあれば更新してから件数と容量を返す。更新に渡すカタログ ID は確認で得たものを明示し、`null` を渡さない
- [ ] 件数の数え方が `Addressables.GetDownloadSizeAsync` と同じ判定（サイズが 0 より大きいバンドルを重複なく数える）になる
- [ ] 引数の Label が SO の Label より優先される。引数なしで SO の Label が空なら `InvalidOperationException`

### 更新の有無の判定（`HasUpdateAsync`）
- [ ] カタログに更新があれば、サイズ計算をせずに `true`。更新が無く、ダウンロードが必要なバンドルがあれば `true`。どちらも無ければ `false`
- [ ] 方針に関係なく `UpdateCatalogs` を呼ばない。方針 `None` ではカタログを確認しない
- [ ] 確認だけで更新しなくても、読み込み（Load）は古いカタログのまま動く（確認はメモリ上の「更新あり」フラグを立てるだけ）

### ダウンロードと進捗（`DownloadAsync`・`DownloadStatus`）
- [ ] 段階が `CalculatingSize → Downloading → Completed` の順に進み、`Downloading` の間は毎フレーム（`Tick`）バイト数と割合が更新される。割合は完了時に 1 になる
- [ ] ダウンロードが必要なバンドルが 0 件なら、`StartDownloadAsync` を呼ばずに `Completed` で終わる
- [ ] `DownloadStatus` は `readonly struct` で、取得・通知した時点の値のコピーである。取得後に処理が進んでも、取得した値は変わらない（`System.Progress<T>` のように後で処理されても、通知した時点の値のまま読める）
- [ ] 何も処理していない間は `AccPhase == None`・`AccIsDownloading == false`。処理中は `AccIsBusy`、ダウンロード中は `AccIsDownloading` で判定できる
- [ ] `Completed` / `Failed` / `Canceled` は次の処理が始まるまで残り、次の処理の開始時にバイト数・リトライ回数が 0 に戻る
- [ ] 利用者が `AccDownloadStatus` を毎回取得し直すだけで（UI の `Update` 等）、取得し直すたびに最新の値が得られる。GC アロケーションは起きない
- [ ] `IProgress<DownloadStatus>` の通知先は、完了・失敗・キャンセル・`Dispose` のすべてで外れる。**外す前に最後の通知が1回だけ届き**（成功なら `Completed`、失敗なら `Failed`、キャンセルなら `Canceled`）、外れた後や次の処理では呼ばれない。前の `DownloadAsync` に渡した `IProgress` が、次の処理の通知を受けることはない
- [ ] 進捗の通知先が例外を投げても、ダウンロードは止まらず、例外はログに出る

### 失敗・リトライ・キャンセル
- [ ] 失敗したら `maxRetry` 回までリトライし、リトライのたびに `AccRetryCount` が増えて通知される。待機は `AsyncCompat.Delay`（実時間）で行う
- [ ] リトライを使い切ったら `AddressableDownloadException`（失敗した段階・リトライ回数・元の例外を持つ）が呼び出し元の `await` に返り、段階は `Failed` になる。失敗の例外は `DownloadStatus` には持たせない
- [ ] `ct` のキャンセルで `OperationCanceledException`、段階は `Canceled` になり、次の呼び出しができる
- [ ] 成功・失敗・キャンセル・`Dispose` のすべての経路で、ダウンロードの handle を解放する
- [ ] 確認・判定・ダウンロードの実行中にもう一度呼ぶと `InvalidOperationException`（実行中の処理は影響を受けない）
- [ ] Manager の破棄（`Dispose`）で、実行中なら `Canceled` を通知してから通知先を外し、待機中の呼び出し元が `OperationCanceledException` で終わる。破棄後の呼び出しは `ObjectDisposedException`

### 初期化との関係
- [ ] 確認・判定・ダウンロードは、初期化が成功するまで待ってから始まる（`LoadBeforeInitializePolicy` の設定に関係なく待つ）。手動モードで初期化が始まっていなければ `InvalidOperationException`、初期化に失敗していれば `AddressableInitializeException`
- [ ] 初期化の待機中に `ct` がキャンセルされると `OperationCanceledException`
- [ ] `IInitializationGate` への `WaitUntilInitializedAsync` の追加が、既存の Loading の挙動を変えない

### 設定とその他
- [ ] Runtime SO の Inspector に「ダウンロード」の見出しの下に、カタログ更新の方針（既定 `CheckAndUpdate`）・対象 Label（`[AddressableLabel]` のプルダウン。既定は空）・リトライ回数（既定 3。0〜10）・リトライ待機（既定 1000ms。0〜60000）・古いバンドルのキャッシュを消すか（既定 false）が表示される。範囲外の値は `OnValidate` で補正される
- [ ] 既存の SO アセットは、追加した設定が既定値で補われる（マイグレーション不要）
- [ ] UniTask あり・なしの両方でコンパイルが通り、同じ結果になる（公開 API のシグネチャは `#if ADDRESSABLE_UNITASK_SUPPORT` で出し分ける）
- [ ] `OnLateUpdate` の購読は `Awake` で `+=`、`OnDestroy` で `-=` と対になり、`OnDestroy` の順序が「Loading の `Dispose` → Download の `Dispose` → Initialize の `Dispose`」になっている
- [ ] 既存の EditMode テスト（Initialize・Loading）と PlayMode テストが、Handler のコンストラクタ変更と偽ゲートへのメソッド追加への追従だけで通る
- [ ] 仕様書のテスト（EditMode：`DownloadController`・`DownloadStatusModel`・`WaitUntilInitializedAsync`／PlayMode：Use Asset Database での確認）が追加され、すべて通る
- [ ] サンプルシーン（`Samples/AddressableDownload/`）で、Has Update / Check / Download / Cancel と進捗・結果の表示ができる

## 4. 制約事項

- 非同期処理は UniTask を正とし、UniTask が無い場合だけ `Task` を使う。UniTask は `#if ADDRESSABLE_UNITASK_SUPPORT` の中でのみ使い、`#if` の外で `using` しない。ゲート（`IInitializationGate`）の待機は共有タスクのため、シンボルに関係なく常に `Task`（CLAUDE.md「設計の絶対ルール」・設計仕様書 3.5.1）
- ダウンロードは同時に1つしか実行しない。そのため、複数の呼び出し元が待つ共有タスクは作らない
- 時間待ちは `AsyncCompat.Delay`、キャンセル付き待機は `AsyncCompat.WithCancellation` を使う。`UniTask.Delay` を直接呼ばない
- VContainer・R3・その他の DI/Rx コンテナに依存させない。配線は `AddressableManager.Awake` での手動 `new` のみ
- MonoBehaviour を継承するのは Manager とサンプルの View のみ。`DownloadController`・`DownloadStatusModel`・Service はピュア C#
- `DownloadController` は他の Controller を参照しない。初期化の待機は `Core/Interfaces/IInitializationGate` だけを通す。`Core/Download/` に `using MAH.AddressableManager.Core.Initialize;` / `…Core.Loading;` を書かない（逆も同様）。Controller に渡す設定は `AddressableDownloadSettings` だけ
- `DownloadStatusModel` は Controller の中で `new` せず、Manager から注入する。Addressables の API を呼ぶのは Service だけ
- Download から Loading の Registry・RefCount を触らない。`InstantiateAsync` 相当の API・Editor 拡張の操作ボタンは作らない
- ライフサイクルの中継は `LateUpdate`（`OnLateUpdate` → `Tick`）。進捗の通知のために毎フレームの `await` ループを書かない
- `await` の前に状態を記録し、キャンセル・失敗では必ず巻き戻す。ダウンロードの handle は `autoReleaseHandle: false` で扱い、`try/finally` で必ず解放する
- 一時コレクション・クロージャを都度確保しない（Service に渡すコールバックはフィールドにキャッシュした delegate。Label の一覧を `ToList()` しない）。`DownloadStatus` の取得・通知でヒープ確保をしない
- 最後の通知（`Completed` / `Failed` / `Canceled`）は、通知先を外す前に届ける。順序は、結果の記録 → 通知 → 通知先を外す → 呼び出し元へ完了または例外を返す
- 仕様書と矛盾する実装を思いついた場合は、黙って実装せず先に仕様書の更新提案を出す。進捗の型の変更（`IProgress<float>` → `IProgress<DownloadStatus>`）は、仕様書への「`IProgress<DownloadStatus>` を推奨する」という記述の追加とあわせて、ステップ1で更新する
- 命名・namespace は `src/AddressableManager/Docs/conventions.md`、用語は `Docs/glossary.md` に従う。コードコメントは日本語。画面の文字は英語で、縦長 1080x2520 と横長の両方に収める（サンプルシーン）
- `.meta` は Unity が生成するため手で作らない。`CHANGELOG.md` は更新しない

## 5. スコープ外（今回やらないこと）

- 起動時の自動ダウンロード（Manager の `Awake` から自動で確認・ダウンロードする機能）。容量の確認ダイアログを挟めなくなるため。必要になったら SO の設定として追加を検討する
- `AssetKey` 単位のダウンロード（設計仕様書 3.3 の案 `GetDownloadSizeAsync(AssetKey)`）。今回は Label 単位のみ。案は仕様書から外し、7章の将来の検討事項に移す
- ダウンロード済みバンドルのキャッシュ削除 API（`ClearDependencyCacheAsync` 相当）
- ダウンロードの一時停止・再開（Addressables に該当する API が無いため）
- ネットワーク接続の事前確認（参考実装の `NetworkChecker`）。通信の可否はゲーム側の責務とし、失敗はリトライの後に例外で返す
- ダウンロード中の通信そのものの中断（Addressables に API が無い。`ct` で止まるのは呼び出し元の待機で、開始済みの通信は裏で続く場合がある。仕様書の注意点に書く）
- Editor 拡張（デバッグウィンドウへのダウンロード状態の表示）。Editor 拡張の実装フェーズで扱う
- 実際のリモート配信環境でのダウンロードの確認（ローカルの HTTP サーバーを使う手動検証。ユーザーが実施する）
- UniTask 版のコンパイル確認（UniTask 導入環境でユーザーが行う）
- `CHANGELOG.md`・利用者向けドキュメント（`Documentation~/` など）の更新
