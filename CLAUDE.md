# CLAUDE.md

> このファイルはClaudeへの作業指示書。GitHub Copilotとは共有しない。
> 設計の詳細・採用理由は `src/AddressableManager/Docs/addressable-manager-design.md` を参照。ここには行動ルールと罠のみを書く。
> 200行以内を目標に保つ。肥大化してきたら `.claude/rules/` に移す。
>
> **本書内のパスはすべてリポジトリルート起点**。Unity作業時のカレントディレクトリは `src/AddressableManager/` になることが多いため、パスを解決する際は起点を取り違えないこと。

最終更新日：2026-10-31 ／ 対象 Unity バージョン：6000.3.26f1

---

## プロジェクト概要

ゲームではなく、**Unity Addressables の読み込み〜解放を一括管理する汎用モジュール** `AddressableManager` を開発するリポジトリ。他プロジェクトへ UPM（git URL の `?path=MAH-AddressableManager` 指定。パッケージ名 `com.github.mah-lib.addressable-manager`）で配布する。

Unity 6000.3.26f1 / C#

```
MAH-AddressableManager/        … リポジトリルート
├─ CLAUDE.md                   … 本書
├─ MAH-AddressableManager/     … UPM の配布フォルダ（package.json・Samples~/）。リリース時に開発フォルダから複製する。直接編集しない
├─ Docs/                       … 利用者向けの使い方・用語集・リリースノート（README経由でGitHub上で読む）
└─ src/AddressableManager/     … Unityプロジェクト（開発・動作検証用サンドボックス）
    ├─ Docs/                   … 設計仕様書・コーディング規則・機能仕様書・図（開発者向けの本体。Assets の外）
    └─ Assets/@MAH/MAH-AddressableManager/   … モジュール本体の開発フォルダ（コードの変更はここで行う）
```

---

## ドキュメント参照

<!-- @ なし：Claudeが作業内容を判断して必要なときに読みに行く。長いものはこちら。 -->
src/AddressableManager/Docs/addressable-manager-design.md   <!-- 設計仕様書。全体構成・他機能との関係の概要。実装前に該当章を必ず読む -->
src/AddressableManager/Docs/specs/                          <!-- 機能仕様書。機能ごとの詳細（手順・例外・サンプルコード）はこちらが正。設計仕様書は概要のみ -->
src/AddressableManager/Docs/conventions.md                  <!-- コーディング規則。命名・namespace・レイヤー分類はこちらが正 -->
Docs/glossary.md                                                   <!-- 用語集。用語の語彙（解放/破棄/回収の使い分け・禁止用語）はこちらが正 -->

命名規則（クラス/フィールド/アクセサ/定数/関数/ローカル変数）・レイヤー別の接尾辞パターン・namespace規則は、すべて `src/AddressableManager/Docs/conventions.md` に定義されている。**本書には転記しない**（二重管理を避けるため）。コードを書く前に該当章を読むこと。

### ドキュメントの置き場所ルール

配布に載るかどうかの判断軸は変わらず「UPM 配布（git URL の `?path=`）で指したサブフォルダに含まれるか」。開発者向けドキュメントの本体は Unity プロジェクト直下（`src/AddressableManager/Docs/`。`Assets/` の外）に置き、リポジトリルート `Docs/` には利用者が GitHub 上で読むもの（使い方・用語集・リリースノート）を置く。

| 置き場所 | 内容 | 配布 |
| --- | --- | --- |
| `src/AddressableManager/Assets/@MAH/MAH-AddressableManager/` の `README.md`・`LICENSE.md`・`Documentation~/`（配布フォルダにも複製する） | `README.md` は概要と git URL のみ。`LICENSE.md` はルートの `LICENSE` と同じ。`Documentation~/` は英語の短いオフライン用要約で、使い方の原本は `Docs/usage.md` | される |
| `src/AddressableManager/Docs/` | 開発者向けドキュメントの**本体**（設計仕様書・コーディング規則・機能仕様書・クラス図/シーケンス図）。`Assets/` の外に置くため Unity にインポートされず（`.meta` も不要）、配布フォルダの外なので配布もされない | されない |
| `Docs/`（リポジトリルート） | 利用者が GitHub 上で読むもの：使い方（`usage.md`。設定項目・API の使い方・例外を**1ファイルに集約**）・用語集・リリースノート（`release-notes.md`。全バージョンを1ファイルにまとめ、新しい版を先頭に追記）とその雛形 | されない |
| `README.md`（ルート） | 利用者向けの**入口**。モジュール概要・導入手順（Package Manager から／`manifest.json` にバージョン指定で書く）・最短の使い方（Manager の配置〜Load/Release）・サンプルと Editor ツールの一覧＋ `Docs/usage.md` へのリンク。英語 | されない |
| `README_JP.md`（ルート） | ルート `README.md` の日本語版。内容は英語版と同じに保つ | されない |
| `CONTRIBUTING.md`（ルート） | 開発者向けの運用フロー（ブランチ・PR・CHANGELOG 追記・リリース手順） | されない |
| `.steering/<YYYYMMDD>-<開発タイトル>/`（ルート） | 作業単位ドキュメント（`requirements.md` / `design.md` / `tasklist.md` の3点セット）。「今回何をするか」の記録 | されない |

- **利用者向けは英語、開発者向けは日本語で書く**。英語：ルート `README.md`・`Docs/usage.md`・`Docs/release-notes.md`・配布フォルダの `README.md`・`package.json` の説明文。日本語：`src/AddressableManager/Docs/`・`CONTRIBUTING.md`・`CHANGELOG.md`・`.steering/`・本書・用語集 `Docs/glossary.md`（利用者は自動翻訳で読む前提。英語の用語は表の「英語」列で示す）。例外として、ルート README は日本語版 `README_JP.md` を併設し、**変更するときは日英を同時に更新する**。Editor ツールの UI は日本語・英語を切り替えられる（利用者が JSON で言語を足せる）。英語のドキュメントでは、画面の文言を日本語表示のままの文言で書き、括弧で英訳を添える（`Docs/usage.md` 6章の注記のとおり）。言語の追加の手順は `Docs/usage.md` の「6.6 Editor Language」が原本
- **利用者は GitHub 上でしか読まない**前提で置き分ける。最短の使い方はルート `README.md`、API の使い方は `Docs/usage.md` が原本。パッケージ内の `README.md` に使い方は書かず、`Documentation~/` は短い要約にとどめて `Docs/usage.md` へ誘導する（全文を複製しない）
- **利用者向けドキュメントを `src/AddressableManager/Docs/`（開発者向け）に複製しない**。複製すると原本ではなくコピーが編集され、必ず乖離する
- **設計仕様書（`src/AddressableManager/Docs/addressable-manager-design.md`）は概要に留める**。機能ごとの詳細（`LoadAsync` の手順・例外の表・サンプルコード等）は `src/AddressableManager/Docs/specs/<機能名>-spec.md` に書き、設計仕様書側からは参照リンク（またはパスの明記）に留める
- `src/AddressableManager/Assets/Docs/`（`Assets/` 配下）は作らない。開発者向けドキュメントは必ず `src/AddressableManager/Docs/` の下に置く（`.meta` が生成され Unity にインポートされるため）
- **`.steering/` は作業単位の履歴**。作業完了後も削除しない。「アプリはどうあるべきか」の正は `src/AddressableManager/Docs/` 側で、`.steering/` と混同しない。作成手順（requirements → design → tasklist の順に1ファイルごと承認）とひな形はスキル `documentation-writing` の `references/doc-types/steering.md`（PC側の `~/.claude/skills/` に配置。リポジトリには含めない）を参照
- `CHANGELOG.md` は担当者が PR・リリース単位で記入する。**指示がない限り Claude から更新しない**（担当者の運用と衝突するため）

---

## Claudeへの行動ルール

### 作業前に必ず行うこと

- 実装に着手する前に `src/AddressableManager/Docs/addressable-manager-design.md` の**該当章を読む**。本書に書かれているのは結論だけで、理由と詳細は仕様書側にある
- 既存ファイルを変更する前に、対象ファイルを全て読む
- 新規クラスを作る前に、同じ層の既存クラスを1つ読んでパターンを確認する
- 複数ファイルにまたがる変更は、計画を先に提示して承認を得る

### 仕様書と矛盾する実装を思いついた場合

黙って実装しない。**先に仕様書の更新提案を出して承認を得る**。本モジュールは「なぜその設計にしたか」が仕様書5.3「今回あえて採用しない設計」に列挙されており、多くの"良さそうな改善案"は検討済みで意図的に却下されている。

ただし**この承認プロセスの対象は、下記「設計の絶対ルール（禁止事項）」の各項目**（非同期方針・依存関係・レイヤー分離・API制約など、設計の根幹）に限る。フォルダ分けや個々のクラス名といった粒度の変更は対象外であり、`src/AddressableManager/Docs/conventions.md` の命名パターンに従っていれば都度変更してよい（仕様書4章のフォルダ構成・クラス名は暫定案のため）。

---

## 設計の絶対ルール（禁止事項）

以下は Unity の一般的な慣習と食い違う項目を含む。**慣習を優先せず、必ず本ルールに従うこと。**

### 非同期処理

- **UniTask が正**（Unity のライフサイクルに組み込まれ、GC が発生しにくいため）。UniTask が導入されていない場合の代替は **`UnityEngine.Awaitable`**（**Unity 6.0 / 6000.0 以上**が前提。`AwaitableCompletionSource` を使うため）。**`System.Threading.Tasks.Task` は使わない**（戻り値・引数・フィールド・`TaskCompletionSource`・`Task.WhenAny` / `ContinueWith` を含む）
- UniTask と Awaitable は**どちらも「1つのインスタンスを1回しか `await` できない」**。この共通ルールを前提に設計する。**同じインスタンスを複数箇所で `await` しない／フィールドに保持して後から `await` しない**
- ただし UniTask は必須の依存にしない。**`#if ADDRESSABLE_UNITASK_SUPPORT` の外で UniTask を `using` しない**。`#if` 側に UniTask 版、`#else` 側に Awaitable 版を書く
- ⚠️ **複数の呼び出し元が待つ共有（`AccLoadTask`・初期化の共有・`IInitializationGate`）は、`await` 可能な値を共有せず、共有完了オブジェクト（`SharedCompletion`）を共有する**。待ち手ごとに専用の CompletionSource（`UniTaskCompletionSource` / `AwaitableCompletionSource`）を作って登録し、完了時に全待ち手へ結果を配る。完了済みなら即完了の値を返す。複数完了の待ち合わせ（最初の失敗で打ち切る一括待機）も、完了通知のコールバックで作る。`.Preserve()` や、Awaitable を複数人で `await` する実装を提案しない
- `Awaitable` は**メインスレッド専用**。`SetResult` 等の完了通知もメインスレッドから呼ぶ。`await` されなかった例外は握りつぶされるため、fire-and-forget にする箇所は例外を観測する経路（ログ出力）を必ず用意する
- キャンセル付き待機は `AsyncCompat.WithCancellation`（共有完了では `SharedCompletion.Wait(ct)`）に集約する（UniTask/Awaitable の差はここだけで吸収）
- 時間待ちは `AsyncCompat.Delay` を使い、`UniTask.Delay` を直接呼ばない（既定では `timeScale = 0` の間に待機が終わらない）。Awaitable 版は `Awaitable.WaitForSecondsAsync`（`timeScale` の影響を受ける）を使わず、実時間で待つよう実装する
- 🚧 **移行中**：コードの一部は旧方針（`Task`）のまま残っている。移行の計画と進捗は `.steering/` の該当作業を参照。新規・変更するコードは本節の方針で書き、既存の `Task` を見つけたら移行対象として扱う（無断で広範囲に書き換えず、計画に沿って進める）

### 依存関係

- **VContainer / R3 / DIコンテナに依存させない**。DIコンテナを導入するプロジェクトでも Manager 内部（Controller以下）の実装は変更しない

### レイヤー

- **MonoBehaviour を継承するのは Manager / Editor / View のみ**。Controller・Service・Model はピュアC#に保つ（Unity非依存のままテスト可能にするため）
- **3つの Controller（Initialize / Download / Loading）は互いを参照しない**。実行順序は Manager が保証する
- Registry（Model）と Service の両方を知ってよいのは Controller だけ。他の層は隣の層としか話さない
- 機能フォルダ同士の相互参照は設計違反。`Core/Loading/` のファイルに `using MAH.AddressableManager.Core.Download;` が現れたらアウト

### API

- **`InstantiateAsync` / `ReleaseInstance` 相当の API を追加しない**。`LoadAsync<T>` は常に**アセット本体**を返す（`LoadAsync<GameObject>` は Prefab アセットであってインスタンスではない）
- Editor 拡張は**読み取り専用**。手動 Release ボタン・強制回収ボタンを作らない。例外は `Editor/Registration/`（アセットの登録）だけで、ルールの SO か手動一括登録の画面の指示に従う場合に限り `AddressableAssetSettings` に書き込む。書き込みは Registration の書き込み担当（`RegistrationEntryWriter`）の1か所に集め、`Editor/Window/`（Dependency Check 等）から書き込まない（仕様書3.9(7)）。`Editor/Localization/` の翻訳の雛形の書き出しは、利用者が選んだ新しいファイルにだけ書き、`AddressableAssetSettings` には触れないため、この規則の例外には当たらない
- **Editor ツールの利用者に見える文言は、`EditorText.Get` / `Format` でキーから取る**（日本語の文字列をコードに書かない。`const string` にもしない）。日本語・英語は同梱の言語ファイル（`Editor/Localization/` の JSON）の**両方**に書く。**設定 SO・ルールの SO（Runtime の設定 SO を含む）のクラスには `[Header]` / `[Tooltip]` を書かない**（`PropertyField` が必ず描画し、日本語で固定されるため。見出し・ツールチップは `header.` / `tooltip.` のキーで持つ）。Runtime の文言は翻訳しない。詳細は `src/AddressableManager/Docs/specs/editor-localization-spec.md`

### 実装の細部

- Unity のライフサイクル中継は `Update` ではなく **`LateUpdate`**（同一フレーム内で後から実行された `Destroy` を取りこぼさないため）
- **RefCount は `await` の前に加算する**。キャンセル・失敗時は `catch` で巻き戻す
- 明示解放と自動回収は**同じ経路（`ReleaseInternal`）を通す**。別経路を作ると二重解放が発生する
- イベント購読は必ず対で書く（`OnLateUpdate += / -=`）。解除漏れは破棄済み Controller の呼び出しにつながる

---

## フォルダ構成と namespace

> **具体的なフォルダ配置・クラス名は暫定案**であり、`src/AddressableManager/Docs/conventions.md` の命名パターンに沿っていれば実装時に変更してよい。ここに書くのは判断に必要な骨組みのみで、詳細は仕様書4章、命名パターンは `conventions.md` を参照。

- 最上位は Unity のパッケージ構成（`Runtime/` `Editor/` `Tests/` `Samples/`）
- 機能の縦割り（`Initialize/` `Download/` `Loading/`）は **`Runtime/Core/` の配下**に置く
- `Core/` 直下は共通物（`AssetKey` `AssetTicket` `Interfaces/`）のみ
- 属性クラスは `Runtime/Attribute/`、その `PropertyDrawer` は `Editor/Attribute/`（`AddressableAssetSettings` がエディタ専用アセンブリからしか参照できないため）
- `Editor/` は役割別に5区分（`Window/` `Attribute/` `SettingsSync/` `Registration/` `Localization/`）。asmdef は分割せず1つに集約する
- 空になるフォルダは作らない

namespace のルートは `MAH.AddressableManager`。対応関係には**2つの軸**がある（詳細は `conventions.md` 7章）：

- **機能単位では対応する**：`Core/Loading/` → `MAH.AddressableManager.Core.Loading`。`Core/Loading/` のファイルに `using MAH.AddressableManager.Core.Download;` が現れたら設計違反
- **外部依存/内部実装の単位では対応しない**：利用者プロジェクトが直接触れる型（`AddressableManager` 本体・`IAddressableHandler` 等）は、`Runtime/Core/` に置かれていても namespace に `Core` を含めず `MAH.AddressableManager` にする
- `Runtime/` は namespace に含めない

---

## 使用パッケージと用途

| パッケージ | バージョン | 用途 |
| --- | --- | --- |
| Addressables | 2.10.3 | 本モジュールが管理対象とする実体 |
| ScriptableBuildPipeline | 2.6.2 | Addressables の依存（自動解決） |
| UniTask | git URL（UPM） | 非同期処理の正（必須依存にはしない）。導入時のみ Version Defines で `ADDRESSABLE_UNITASK_SUPPORT` が定義される |
| Awaitable（`UnityEngine.Awaitable`） | Unity 本体（Unity 6.0 以上） | UniTask が無い場合の代替。パッケージ導入は不要 |
| URP | 17.3.0 | 検証用サンドボックスのレンダリング |
| Test Framework | 1.6.0 | EditMode / PlayMode テスト |

- 使用禁止：VContainer / R3 / その他 DI・Rx ライブラリ（汎用モジュール方針のため）

---

## パフォーマンス方針

- **一時コレクションを都度確保しない**。`ReleaseLifetime` の一時退避に `ToArray()` / `ToList()` を使わず、フィールドに保持した `List` を `Clear()` して再利用する（大量の GameObject 同時破棄で連続呼び出しされるため実害が出やすい）
- **クロージャ・ラムダを毎回生成しない**。Watcher へ渡すコールバックはフィールドにキャッシュした delegate を使う
- `Dictionary` は具体型のまま `foreach` する（`IEnumerable` にアップキャストするとボックス化する）
- **リーク検出のスタックトレース記録は `#if UNITY_EDITOR` で本番ビルドから完全に除外する**。取得コストと文字列生成の GC アロケーションが大きい

---

## プロジェクト固有の注意点（罠・落とし穴）

<!-- Claudeが実際に間違えた直後に随時追記する。このセクションだけは積極的に育てる。 -->

- **Version Defines は UPM 経由のパッケージにのみ反応する**。UniTask を `.unitypackage` で `Assets/` 配下に展開するとシンボルが定義されず、UniTask があるのに Awaitable 版が使われる。UniTask は UPM（git URL 可）で導入すること
- **`AssetKey` は「入口の種別＋値」の一致のみで同一判定する**。同一アセットを Path と AssetReference の両方で要求すると二重ロードされるが、これは**コストとの兼ね合いで許容した仕様**であってバグではない（仕様書3.6）
- CLAUDE.md はリポジトリルート、Unity プロジェクトは `src/AddressableManager/` と階層がずれている。パスを書くときは起点を取り違えない
- **「Docs」が2箇所ある**：リポジトリルートの `Docs/`（利用者向け：使い方・用語集・リリースノート）と `src/AddressableManager/Docs/`（設計仕様書・コーディング規則・機能仕様書・図）は別フォルダ。設計仕様書・conventions・機能ごとの詳細を探すときは後者
- `Assets/TutorialInfo/` と `Assets/Readme.asset` は URP テンプレートの残骸。モジュールとは無関係
- `ProjectSettings/EditorBuildSettings.asset` が既に存在しない `Assets/Scenes/SampleScene.unity` を参照している（ビルドシーンリストが壊れた状態）
- **モジュールのコードが2か所にある**：開発フォルダ `src/AddressableManager/Assets/@MAH/MAH-AddressableManager/` と、配布フォルダ（ルート `MAH-AddressableManager/`）。**コードの変更は開発フォルダだけで行う**。配布フォルダはリリース時に複製で上書きされる（`Samples/` → `Samples~/`。手順は `CONTRIBUTING.md` 4章）。検索結果に両方が出るため、編集先を取り違えない

---

## コミュニケーションの設定

- コードコメントは日本語
- 変数名・クラス名・メソッド名は英語
- Claudeの回答は日本語
- 複数の実装方法がある場合は、採用した理由を短く添える
- 変更量が大きいときは計画を箇条書きで提示してから着手する
