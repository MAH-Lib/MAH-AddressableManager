# 機能仕様書：未登録の依存の確認（Dependency Check）

> 最終更新日：2026-10-15 ／ 対象 Unity バージョン：6000.3.26f1 ／ 実装状況：**実装済み（Scan・一覧・直接／経由の区別・絞り込み・並べ替え・移動・テキスト出力・列幅の変更）**
> **Editor 機能4「未登録の依存の確認（Dependency Check）」の仕様はこのファイルを正とする**。全体の位置づけは [`addressable-manager-design.md`](../addressable-manager-design.md) 3.9(4)、命名パターンは [`conventions.md`](../conventions.md)、用語は `Docs/glossary.md`（リポジトリルート）が正。
> 図は [`diagrams/editor-dependency-check-class.md`](../diagrams/editor-dependency-check-class.md) を参照。
>
> 本書は、Dependency Check ウィンドウが**どう動き、どう作られているか**、そして**改修するときにどこを触ればよいか**を、後から読む開発者が把握するための資料である。Play 中の状態を見るデバッグウィンドウ（[`editor-debug-window-spec.md`](editor-debug-window-spec.md)）とは別のウィンドウで、その共通基盤（窓口・Snapshot・pull 更新）は使わない。

---

## 1. 概要

Addressables に登録したアセット（**親**）が使っている、**未登録のアセット（子）**を一覧で確認する Editor ウィンドウ。Play もビルドもせずに使える。

**一番の目的**は、未登録の子が**どこで（どのグループのどの親から）、どのくらい使われているか**を見て、Group の作成や設定を見直す材料にすること。粒度の見直し、更新頻度の違うものが同じ Group に混ざっていないかの確認、登録の漏れへの気づきに使う。

**2番目の目的**は、ビルドしたときにどうなるかの参考情報を得ること。未登録の子は、使っている親のバンドルに**暗黙的に同梱**されるため、複数のグループの親から使われていると、ビルドでグループごとに重複して入る可能性がある。ただし、ここに出る「複数グループ」は、親が子を使っているグループの数であって、**ビルドで実際に重複するかどうかの判定ではない**（登録済みのアセットを経由した使用も数えるため。2.2）。ビルド結果の正確な確認は、Addressables 標準の Analyze（Check Duplicate Bundle Dependencies）で行う。

| できること | 内容 |
|---|---|
| 走査 | 「Scan」ボタンで、全グループの登録済みアセットの依存を辿る（キャンセルできる進捗バー付き） |
| 一覧 | 未登録の子1つにつき1行。使っている親とグループ（登録済みのアセットを経由した使用は経由先つき）、参照元のグループ数（2以上は複数グループとして強調） |
| 絞り込み・並べ替え | 型（複数選択）・名前・参照元のグループ。ファイルサイズ順 |
| 移動 | 行のクリックで、Project ウィンドウでそのアセットを選択・ハイライト |
| 出力 | 表示中の行を、人が読むテキスト（`.txt`）でファイルに出力 |

**この機能が持たないもの**：
- Addressables への登録・グループの整理（読み取り専用。どのグループに入れるかは設計判断で、自動化すると誤った分割を招くため）
- 「登録済みだが、どこからも参照されていない」アセットの検出（コードのアドレス指定〔`LoadAsync("key")`〕でしか参照されないものを、未使用と誤判定するため）
- バンドル単位での正確な重複判定（ビルドレイアウトの計算が必要。Analyze に任せる。7章）
- 走査の自動実行（ウィンドウを開いたとき・アセットの変更時）
- 走査結果のファイル保存と、Editor 再起動後の復元（出力したテキストで代える）
- ロード所要時間の計測（作らないことにした。設計仕様書 6章 2026-10-15）

### 1.1 全体の中での位置づけ

```
Editor アセンブリ（MAH.AddressableManager.Editor）
├─ AddressableDebugWindow      … Play 中の状態（実行時状態・リーク・Owner）。窓口 IAddressableDebugReader から pull
└─ AddressableDependencyWindow … 本書。Play せずに、アセットを静的に走査する
     ├─ DependencyCheckScanner     … AddressableAssetSettings・AssetDatabase・SpriteAtlas を読み、入力を作る
     ├─ DependencyCheckAggregator  … 入力から一覧の行を作る（Unity 非依存）
     └─ DependencyCheckLogFormatter … 表示中の行をテキストにする（Unity 非依存）
```

- 窓口（`IAddressableDebugReader`）・`AddressableManager` には触れない（Runtime のログ出力 `AddressableLogger` だけを使う）。本番ビルドには含まれない

---

## 2. 動作

### 2.1 走査（Scan）

1. `AddressableAssetSettingsDefaultObject.Settings` が無ければ、走査せずに「Addressables が未設定です」と表示して終わる（例外を出さない）
2. **親と「登録済み」の集合を作る**：全グループについて `AddressableAssetGroup.GatherAllAssets(list, includeSelf: true, recurseAll: true, includeSubObjects: false)` で、フォルダーエントリの配下も含めた登録済みアセットを集める。これが親の一覧であり、そのパスの集合が「登録済み」になる。Scene のエントリも親になる
   - `PlayerDataGroupSchema` を持つグループ（Built In Data：Resources・ビルド設定のシーン）は、Addressables のバンドルに入らないため除く
   - Bundle Mode が `Pack Separately` のグループ名を控える（2.3 の印のため）
3. **SpriteAtlas の packables を「登録済み」に加える**：親のうち `SpriteAtlas` のものについて、`SpriteAtlasExtensions.GetPackables` の Texture・Sprite はそのパスを、フォルダーは配下の Texture のパスを加える。全グループ分を先に集めてから 4 に進む（atlas と、Sprite を参照する親が別グループでも判定できるようにするため）
4. **依存を辿る**：親から届くすべてのアセットについて、`AssetDatabase.GetDependencies(path, recursive: false)` で**直接の依存を1段ずつ**集める（幅優先。同じアセットは1回しか読まない。戻り値に含まれる自分自身は除く）。1段ずつ持つのは、親が子を直接使っているのか、登録済みの別のアセットを通して使っているのかを、集約（2.2）で区別するため。「登録済み」に無いパスが**未登録の子**で、初めて出てきたときだけ型名（`AssetDatabase.GetMainAssetTypeAtPath(path).Name`。取得できなければ `Unknown`）とディスク上のファイルサイズ（取得できなければ 0。`Packages/` 配下は、パッケージ情報の `resolvedPath` から実体の場所を引く）を取る
5. 親1つごとに、キャンセルできる進捗バー（`EditorUtility.DisplayCancelableProgressBar`）を更新する。**キャンセルされたら打ち切り、一覧は前回の結果のまま**にする。進捗バーは必ず消す（`finally`）
6. 集約（2.2）して一覧を置き換え、走査した日時を記録する

### 2.2 集約（一覧の行の作り方）

- **未登録の子1つにつき1行**。テクスチャの中の Sprite のようなサブアセットは、`GetDependencies` がファイルのパス単位で返すため、ファイル単位で1行にまとまる
- 行は、その子へ届く**すべての登録済みの親**（パスとグループ名）を参照元として持つ。登録済みのアセットを通して届く親も、参照元に含める（Group の分け方を見直すとき、どのグループがその子を使っているかを知りたいため）
- **直接と経由の区別**：参照元の親ごとに、次のどちらかを付ける
  - **直接**：親から子まで、未登録のアセットだけを通って届く（間に未登録のアセットがあってもよい）
  - **経由**：親と子の間に、登録済みの別のアセット（**経由先**）がある。例：`Title.prefab`（Group A）→ `Btn.mat`（登録済み・Group B）→ `Tex.png`（未登録）のとき、`Tex.png` の参照元は `Btn.mat`（直接）と `Title.prefab`（経由先 `Btn.mat`）
  - 経由先は、親から最初に出会う登録済みのアセット。複数あるときは、パス順で最初の1つと、その数を持つ。直接と経由の両方で届くときは、直接として扱う
- **参照元のグループ数**：参照元の親が属するグループ名を、重複なく数える（経由の親も数える）。**2以上を複数グループ**とし、強調する。同じグループの2つの親から使われているだけなら、複数グループにしない。ビルドで実際に重複するかどうかを表すものではない
- **`Resources/` の印**：パスに `/Resources/` を含む子に付ける。`Resources/` 配下は Addressables とは別にビルドに入るため、Addressables の親から使われているのは、登録の漏れや移行の漏れの手がかりになる。隠さず印を付けて表示する
- **既定で隠す**：拡張子 `.cs`・`.dll`、`Packages/` で始まるパスは「既定で隠す」印を付けて行に残す（スクリプトはバンドルに入らず、`Packages/` は組み込みのシェーダー等で利用者が直せないため）。フィルターで表示に切り替えられる
- **`Pack Separately` の印**：参照元に `Pack Separately` のグループの親を含む行に付ける。ビルド時の参考情報で、このグループではエントリごとに別バンドルになるため、同じグループの中でも重複して入り得る（グループ数では分からない。7章）
- 集約は、パスと型名の文字列だけで行い、Unity の型（`Type`・`Object`）を使わない（EditMode テストで、偽の入力で検証できるようにするため）

### 2.3 一覧の表示

| 列 | 内容 |
|---|---|
| 名前 | ファイル名 |
| 型 | メインアセットの型名 |
| パス | アセットのパス |
| サイズ | ディスク上のファイルサイズ（KB・MB 表記） |
| グループ数 | 参照元のグループ数。**2以上は複数グループとして強調** |
| 親の数 | 参照元の親の数 |
| 印 | `Resources/`・`Pack Separately` |

- 行を展開すると、参照元の親とそのグループが並ぶ。経由の使用には、行の末尾に「（経由：パス）」、経由先が複数なら「（経由：パス ほか n 件）」を付ける
- 画面に注記を出す：グループ数は、その子を使っている親のグループの数で、登録済みのアセットを経由した使用も数える（ビルドでの重複の有無ではない）／サイズはディスク上の値で、バンドルに入ったときのサイズや実機メモリとは異なる／ビルド時の参考として、`Pack Separately` のグループでは、同じグループの中でも重複して入り得る（正確な確認は Analyze）
- 最後に走査した日時を表示する。走査していなければ「Scan を押してください」と表示する
- 列は見出しの境界（区切り線）をドラッグして幅を変えられる（最小 30px）。幅の合計が表示幅を超えたときは横スクロールになり、見出しも一緒に動く。列幅は再コンパイルでは保持され、Editor の再起動で既定値に戻る
- 画面の文言は `EditorText` から取り、表示言語に従う（[`editor-localization-spec.md`](editor-localization-spec.md)）。メニュー `MAH/Addressable/Dependency Check`、タイトル `Addressable Dependency`

### 2.4 絞り込み・並べ替え・移動

- **型**：走査結果に現れた型の、複数選択のトグル（型の一覧を固定せず、結果から作る）
- **名前**：検索ボックス（部分一致・大文字小文字を区別しない）
- **グループ**：参照元のグループで絞る
- **既定で隠す行の表示**：スクリプト・DLL・`Packages/` を表示に切り替える
- **並べ替え**：ファイルサイズの大きい順（既定はパス順）
- **移動**：子の行・参照元の親の行をクリックすると、`Selection.activeObject` に設定し `EditorGUIUtility.PingObject` で Project ウィンドウに表示する（フォルダーが開き、アセットがハイライトされる）。アセットが消えていたら、警告を出して何もしない

### 2.5 テキスト出力

表示中の行（絞り込み後・並べ替え後）を、人が読むためのテキストで出力する。リークのログ（`editor-debug-window-spec.md` 8.2）と同じ形。

```
Addressable Dependency Check  2026-10-15 14:30:00
未登録の依存 2 件（複数グループ 1 件）  絞り込み：型 すべて / グループ すべて

[複数グループ] Assets/Art/Tex_Bg.png  (Texture2D, 2.0 MB, 2 グループ)
  ← Assets/Prefabs/Title.prefab  [Title]（経由：Assets/Mats/Btn.mat）
  ← Assets/Prefabs/Menu.prefab   [Menu]

Assets/Art/Mat_Btn.mat  (Material, 4 KB, 1 グループ)
  ← Assets/Prefabs/Menu.prefab   [Menu]
```

- ファイル名は `AddressableDependencies_yyyyMMdd_HHmmss.txt`。出力するたびに新しいファイルが溜まる（同じ秒なら `_2`・`_3`… を付ける）
- 文字コードは UTF-8（BOM なし）。出力先は 3章の設定
- 書き込みに失敗したら、警告を出して Editor の操作は止めない

### 2.6 結果の保持

- 走査結果は、ウィンドウのシリアライズ対象のフィールドに持つ。スクリプトの再コンパイル（ドメインリロード）では消えず、Editor を再起動すると消える

---

## 3. 設定項目（`AddressableDependencyCheckSettings`）

`AddressableEditorSettingsSO`（`editor-debug-window-spec.md` 3章）が保持する `[Serializable]` クラス。

| 項目 | 既定 | 内容 |
|---|---|---|
| 出力先フォルダ | `Logs/AddressableManager` | テキスト出力の出力先。相対パスはプロジェクトのルート（Assets の親）から、絶対パスはそのまま。空にすると既定値に戻る |

- 取得は `AddressableEditorSettingsLocator.FindDependencyCheckSettings()`。SO が無いときは既定値を使う
- 既存の SO アセットは、`OnValidate` で既定値が補われる（マイグレーション不要）

---

## 4. 内部構成

### 4.1 クラスと責務

すべて Editor アセンブリ・`internal`。

| 型 | 層 | 配置 | 責務 |
|---|---|---|---|
| `AddressableDependencyWindow` | Editor（`EditorWindow`） | `Editor/Window/` | Scan・絞り込み・並べ替え・一覧・移動・出力。結果と絞り込みの状態を持つ |
| `DependencyCheckScanner` | static | `Editor/Window/` | `AddressableAssetSettings`・`AssetDatabase`・`SpriteAtlas` を読み、`DependencyCheckInput` を作る。進捗とキャンセル。グループ・エントリを書き換えない |
| `DependencyCheckInput` | Definition | `Editor/Window/` | 集約への入力（親・直接の依存の表・登録済みの集合・型名とサイズ・`Pack Separately` のグループ）。Unity 非依存 |
| `DependencyCheckAggregator` | Logic（static） | `Editor/Window/` | 入力から一覧の行を作る。直接と経由の区別もここで行う（2.2）。Unity 非依存 |
| `DependencyCheckRow` | Definition（`[Serializable]`） | `Editor/Window/` | 一覧の1行（未登録の子） |
| `DependencyCheckReferrer` | Definition（`[Serializable]`） | `Editor/Window/` | 参照元の親（パス・グループ名・経由先のパスと数） |
| `DependencyCheckLogFormatter` | Logic（static） | `Editor/Window/` | 表示中の行をテキストにする（2.5）。サイズ表記・経由の表記（`FormatVia`）を含む。Unity 非依存 |
| `EditorTextFileWriter` | static | `Editor/Window/` | 出力先の解決と、日時付きファイル名での書き込み（同じ秒なら連番）。リークのログ出力（`LeakLogWriter`）と共有する |
| `AddressableDependencyCheckSettings` | Data（`[Serializable]`） | `Editor/SettingsSync/` | 出力先の設定（3章） |

namespace は、`Editor/Window/` が `MAH.AddressableManager.Editor.Window`、`Editor/SettingsSync/` が `MAH.AddressableManager.Editor.SettingsSync`（conventions 7.3）。

絞り込み・並べ替えは、ウィンドウの `internal static BuildVisibleRows`（表示する行の添字を作る）に切り出してあり、テストから直接検証できる。`DependencyCheckScanner.TryScan` は、結果を `ScanResult`（`Completed`・`Canceled`・`NoSettings`）で返し、`Completed` のときだけ入力を渡す。

### 4.2 行と参照元が `[Serializable]` のクラスである理由

`readonly struct` は Unity がシリアライズできず、再コンパイルのたびに結果が消えるため。フィールドは `private` ＋ `[SerializeField]`、読むのは `Acc` アクセサ（conventions 2章）。

### 4.3 描画の負荷

- 絞り込み・並べ替えの結果（表示する行の添字の `List<int>`）は、Scan・絞り込みの変更・並べ替えの変更のときだけ作り直し、`OnGUI` のたびには作らない
- 表示行から作る値（複数グループの件数・高さの合計・状態の文字列）も、同じときだけ作り直す。行の展開・折りたたみでは、高さの合計にその行の差分（参照元の数 × 行の高さ）を足し引きするだけで、全行を数え直さない
- 描画は、スクロール位置から見えている範囲の行だけを描く（行の高さは固定。展開した行は、参照元の数だけ高さを足して計算する）。数千行でも `OnGUI` の負荷を抑えるため
- 集約は、経由の判定のために、登録済みのアセットから届く未登録の子を、登録済みのアセットごとに1回だけ求めて使い回す

### 4.4 テスト（EditMode）

- `DependencyCheckAggregatorTests`：登録済み・親自身は行にならない／型・サイズ／未登録の中間を挟む孫は直接／登録済みのアセットを経由した使用（経由先・数・直接が優先・パス順で最初の1つ・最も近い登録済みのアセットが経由先）／依存の循環／グループ数による複数グループ（同グループは複数グループにしない）／`Resources/`・既定で隠す・`Pack Separately` の印／パス順
- `DependencyCheckLogFormatterTests`：2.5 の出力例との一致（親のパスの幅は出力全体の最長にそろえる。経由の表記を含む）／経由の表記（直接・経由先1つ・複数）／表示中の行だけが出る／0件／サイズ表記の境界
- `AddressableDependencyWindowTests`：`BuildVisibleRows` の型（複数選択）・名前・グループ・既定で隠す行・サイズ順
- `EditorTextFileWriterTests`・`AddressableDependencyCheckSettingsTests`：出力先の解決・連番・UTF-8（BOM なし）・失敗時の警告／既定値・補正・SO の `OnValidate`
- `DependencyCheckScanner` とウィンドウの描画は、Unity のアセットに依存するため、テストでは覆わず手動で確認する

---

## 5. 設計の判断と理由

- **Addressables 標準の Analyze とは目的が違うため、別に作る**：この機能は、未登録の子が「どこで・どのくらい使われているか」を、Group の作成・設定を見直す材料として見るためのもの。Analyze の Check Duplicate Bundle Dependencies は、ビルドしたときの重複バンドルを確認するもので、ビルドレイアウトの計算を伴って重く、絞り込み・アセットへの移動もできない。軽い静的走査（`AssetDatabase.GetDependencies`）で、日常的に確認できるようにした。ビルド結果の正確な確認は、引き続き Analyze に任せる（7章）
- **デバッグウィンドウのタブにせず、別のウィンドウにする**：デバッグウィンドウのタブの契約（`IDebugWindowTab.Refresh(reader)`）は Play 中の窓口を前提にしている。この機能のデータの出所はアセットの静的な走査で、Manager とは無関係なため、契約を広げずに分けた（設計仕様書 3.9 の「同じデータソースなら1ウィンドウ」の裏返し）
- **未登録の子1つにつき1行にする**：直すべき対象は未登録の子で、「登録する」か「参照を外す」かを判断するには、その子を誰が参照しているかが一目で見える必要があるため
- **グループ数は、使っている親のグループの数にする**：Group の分け方を見直すには、どのグループがその子を使っているかが分かればよく、バンドル単位の判定（ビルドレイアウトの計算が必要で、Analyze と同じ重さになる）は要らないため。そのため、登録済みのアセットを経由した使用も数える
- **直接と経由を区別して見せる**：経由の使用は、間にある登録済みのアセット（経由先）が子を使っているのであって、親が子を使っているわけではない。Group を見直すとき、どちらの親を動かすべきかが変わるため、経由先を表示する
- **フォルダーエントリの配下・登録済みの SpriteAtlas の Sprite を「登録済み」とみなす**：そうしないと、フォルダーごと登録する運用や、UI の Image が参照する Sprite が、すべて未登録として一覧を埋め、本当の問題が埋もれるため
- **「Scan」ボタンでだけ走査する**：全グループの依存を辿るため、規模によっては数秒以上かかる
- **出力は人が読むテキストにする**：出力先を SO で決め、人が開いて読む前提のため。CSV は参照元（複数）を1行に入れると読みにくく、リークのログと同じ形にそろえた
- **集約を Unity 非依存にする**：実アセットを用意しなくても、偽の入力で EditMode テストができるようにするため
- **読み込み計測をやめた経緯**：設計仕様書 6章（2026-10-15）を参照

---

## 6. 改修ガイド

### 6.1 「登録済み」の判定を変えたい

`DependencyCheckScanner` の「登録済み」の集合の作り方（2.1 の 2・3）を変える。集約（`DependencyCheckAggregator`）は集合を受け取るだけなので変えなくてよい。判定を足したら、`DependencyCheckAggregatorTests` に入力のパターンを足す。

直接と経由の区別（2.2）を変えたいときは、集約の `CollectDirectChildren`（未登録のアセットだけを通って届く子と、最初に出会う登録済みのアセットを集める）と `GetReachableChildren`（登録済みのアセットから届く未登録の子）を変える。走査（Scanner）は、直接の依存を1段ずつ集めるだけで、判定には関わらない。

### 6.2 列・印を足したい

走査で値を取り（`DependencyCheckInput`）、集約で行に載せ（`DependencyCheckRow`）、ウィンドウと `DependencyCheckLogFormatter` で表示する。行のフィールドは `[SerializeField]` にする（再コンパイルで消えないように）。

### 6.3 Addressables を更新したとき

`GatherAllAssets` の引数・`PlayerDataGroupSchema`・`BundledAssetGroupSchema.BundleMode` の扱いが変わっていないかを確認する。Analyze の結果と大きく食い違わないかで確かめる。

### 6.4 改修時に守ること

- グループ・エントリ・アセットを書き換える API（`CreateOrMoveEntry`・`RemoveAssetEntry` 等）を呼ばない（読み取り専用）
- 窓口（`IAddressableDebugReader`）・`AddressableManager` に触れない
- 集約・整形に Unity の型を持ち込まない
- `OnGUI` のたびに集計し直さない

---

## 7. 注意点・既知の制約

- **サイズはディスク上のファイルサイズ**で、バンドルに入ったときのサイズ（圧縮・プラットフォーム別のテクスチャ設定）や実機メモリとは異なる。アセット同士の相対比較に使う
- **ビルド時の参考：`Pack Separately` のグループでは、同じグループの中でも重複して入り得る**（エントリごとに別バンドルになるため）。グループ数では分からないため、印と注記で知らせる
- **コードのアドレス指定による参照は見えない**。`GetDependencies` はシリアライズされた参照だけを辿るため、`LoadAsync("key")` で読むものの依存関係は対象外
- **Built In Data のグループ**（`PlayerDataGroupSchema`）は、バンドルに入らないため親として扱わない
- 走査は全グループの依存を辿るため、規模によっては時間がかかる。キャンセルすると結果は前回のまま
- **「複数グループ」はビルドでの重複の判定ではない**：登録済みのアセットを経由した使用も数える。経由先のバンドルに入るだけで、親のバンドルには入らないこともあるため、「複数グループ」の行がビルドで必ず重複するわけではない。逆に、`Pack Separately` のグループ内の重複は、グループ数が1のため「複数グループ」にならない（印で知らせる）。ビルド結果の正確な確認は、標準の Analyze（バンドル単位で判定する）で行う
