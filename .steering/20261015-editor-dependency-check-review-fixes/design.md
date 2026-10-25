# 設計：Dependency Check ウィンドウのレビュー指摘修正

> 対象：`requirements.md`（承認済み版） ／ 作成日：2026-10-15 ／ ステータス：承認済み（実装を先に行ったため、本書は実装した内容の記録である）
> 備考：仕様の正は `src/AddressableManager/Docs/specs/editor-dependency-check-spec.md`。

## 1. 実装アプローチ

判定の仕組み（依存の辿り方・グループ数・親の数）は変えず、**言葉と文書を機能の目的に合わせ**、**直接／経由の区別**を足す。Editor アセンブリ（Window）の中だけで完結させる。

- **言葉の統一**：「重複」→「複数グループ」。`DependencyCheckRow.AccIsDuplicate` を `AccIsMultiGroup` に改名（シリアライズするフィールド `_groupCount` は変えない）。出力は `[複数グループ]`・「複数グループ n 件」。画面の注記を、グループ数の意味（経由の使用も数える・ビルドでの重複の有無ではない）と、ビルド時の参考（`Pack Separately`。正確な確認は Analyze）に書き換える
- **直接／経由の区別**：
  - Scanner は、`GetDependencies(path, recursive: true)` ではなく、`GetDependencies(path, recursive: false)` で**直接の依存を1段ずつ**集める（幅優先。同じアセットは1回しか読まない）。結果は `DependencyCheckInput.AccDirectDependencies`（パス → 直接の依存）に持つ
  - 判定は Aggregator（Unity 非依存）で行う。親ごとに、(1) 未登録のアセットだけを通って届く子を「直接」とし、途中で最初に出会う登録済みのアセットを「経由先」として集める（登録済みのアセットに着いたらそこで止める）、(2) 経由先をパス順に並べ、経由先から（登録済みのアセットも通り抜けて）届く未登録の子のうち、直接でないものを「経由」の参照元にする。経由先ごとの「届く子」は、親をまたいで使い回す（初めて求めたときだけ辿る）
  - 参照元の集合は、再帰で集めていた修正前と同じ（親から届く子はすべて、直接か経由のどちらかになる）。変わるのは、参照元に直接／経由の情報が付くことだけ
  - 参照元（`DependencyCheckReferrer`）に、経由先のパス（パス順で最初の1つ）と経由先の数を持たせる。直接のときは数が 0。コンストラクタは既定値を持たせ、直接の呼び出しを変えない
  - 表示の組み立ては `DependencyCheckLogFormatter.FormatVia` に切り出し、テキスト出力と画面の展開行で共有する
- **サイズ取得**：`Packages/` で始まるパスは、`UnityEditor.PackageManager.PackageInfo.FindForAssetPath` の `resolvedPath` から実体の場所を引く（レジストリのパッケージの実体は `Library/PackageCache` にあるため）。それ以外は従来どおりプロジェクトのルートからの相対。例外は `System.Exception` を握って 0 にする
- **OnGUI の負荷**：複数グループの件数・高さの合計・状態の文字列を、`_visibleRows` を作り直した直後（`RefreshVisibleSummary`）にだけ計算してフィールドに持つ。行の展開・折りたたみは、高さの合計にその行の差分（参照元の数 × 行の高さ）を足し引きする

検討した代替案と却下理由：
- **`GetDependencies(path, true)` のまま、経由を別の走査で求める案**：再帰の結果から直接／経由は復元できない。直接の依存を1段ずつ持つ形に変えるほうが、判定が Aggregator に集まり、テストできる
- **経由先をすべて持つ案**：参照元の1行に収まらず、展開した行の表示が長くなる。パス順の最初の1つと数（「ほか n 件」）で、手がかりとして十分
- **「複数グループ」ではなく「共有」「グループ横断」にする案**：何と共有か読み取りにくい／グループ数の列との対応が薄い。「グループ数が2以上」という意味に直結する「複数グループ」を採用した
- **経由の使用を参照元から外す案**：Group の分け方を見直すには、経由を含めて「どのグループがその子を使っているか」を見たい。外さず、区別して見せる
- **`Pack Separately` の印を外す案**：ビルド時の情報も2番目に重要なため、削除せず、注記の位置づけだけ「ビルド時の参考」にした
- **`Path.GetFullPath` で `Packages/` を解決する案**：`Packages/` の実体はプロジェクトのルートの下にないことがあり、解決できない（レビューでの私の記述の誤り。`PackageInfo` を使う）

## 2. 変更するコンポーネント

コードは `src/AddressableManager/Assets/@MAH/AddressableManager/` を起点とする。ドキュメントは `src/AddressableManager/Docs/` 起点（README・用語集はリポジトリルート）。

### コード（Editor）

| 対象 | 変更内容 | 新規/修正 |
|---|---|---|
| `Editor/Window/DependencyCheckInput.cs` | `Parent.AccDependencies`（孫以降まとめた依存）を削除し、`AccDirectDependencies`（パス → 直接の依存）を追加 | 修正 |
| `Editor/Window/DependencyCheckScanner.cs` | 親から届く全アセットの直接の依存を幅優先で集める。サイズ取得を `ResolvePhysicalPath`（`Packages/` は `PackageInfo`）経由にし、例外を広げる | 修正 |
| `Editor/Window/DependencyCheckAggregator.cs` | 直接／経由の判定（`CollectDirectChildren`・`GetReachableChildren`）。参照元に経由先と数を載せる | 修正 |
| `Editor/Window/DependencyCheckReferrer.cs` | `AccViaPath`・`AccViaCount`・`AccIsVia` を追加。コンストラクタに既定値つきの経由の引数を足す | 修正 |
| `Editor/Window/DependencyCheckRow.cs` | `AccIsDuplicate` → `AccIsMultiGroup`、コメントの見直し（`Pack Separately` は「ビルド時の参考」） | 修正 |
| `Editor/Window/DependencyCheckLogFormatter.cs` | `[重複]` → `[複数グループ]`、件数行、`FormatVia` の追加と参照元の行への付加 | 修正 |
| `Editor/Window/AddressableDependencyWindow.cs` | 注記の書き換え、`RefreshVisibleSummary`（件数・高さ・状態の文字列）、展開時の高さの差分更新、参照元の行に経由を表示、日時の `InvariantCulture` | 修正 |

### テスト（EditMode）

| 対象 | 変更内容 | 新規/修正 |
|---|---|---|
| `Tests/Editor/DependencyCheckAggregatorTests.cs` | 入力を直接の依存の形（`AddParent`・`AddNode`）に変更。複数グループへの改名。経由・直接が優先・経由先が複数・経由先が連なる場合・循環のテストを追加 | 修正 |
| `Tests/Editor/DependencyCheckLogFormatterTests.cs` | 期待値を新しい表記に変更。経由の行と `FormatVia` のテストを追加 | 修正 |
| `Tests/Editor/AddressableDependencyWindowTests.cs` ほか | 変更なし（`DependencyCheckReferrer` の既存の呼び出しがそのまま通る） | 変更なし |

### ドキュメント

| 対象 | 変更内容 | 新規/修正 |
|---|---|---|
| `Docs/specs/editor-dependency-check-spec.md` | 1章（目的を2段に）・2.1 手順4・2.2（直接／経由・複数グループ・`Resources/`・`Pack Separately`）・2.3・2.5 出力例・4.1・4.3・4.4・5章（Analyze とは目的が違う）・6.2・6.5・7章 | 修正 |
| `Docs/addressable-manager-design.md` | 3.9 の機能表と本文・3.9(4)・5.3・6章に位置づけの明確化を追記 | 修正 |
| `Docs/diagrams/editor-dependency-check-class.md` | `AccDirectDependencies`・`AccIsMultiGroup`・経由のメンバー・`FormatVia`・シーケンス図（直接の依存を1段ずつ集める走査と経由の判定） | 修正 |
| `Docs/glossary.md`（ルート） | 4.2 に「複数グループ」「経由」を追加し「重複同梱」を削除。7章に「重複」の禁止を追加 | 修正 |
| `README.md`（ルート） | Dependency Check の記述を、実装済みの機能として書き直す | 修正 |

## 3. データ構造の変更

- `DependencyCheckInput`：`Parent.AccDependencies` を削除し、`AccDirectDependencies : Dictionary<string, List<string>>` を追加（実行時のデータで、保存しない）
- `DependencyCheckReferrer`（`[Serializable]`）：`_viaPath`（string）・`_viaCount`（int）を追加
- `DependencyCheckRow`（`[Serializable]`）：シリアライズするフィールドは変更なし（アクセサの名前だけ変更）
- マイグレーション：不要。走査結果はウィンドウのシリアライズに入るだけで、Editor の再起動で消える。再コンパイル前に保持していた行は経由の情報を持たず、すべて「直接」に見えるが、Scan し直せば直る。移行処理は作らない
- 設定（`AddressableDependencyCheckSettings`・Editor SO）：変更なし

## 4. 影響範囲の分析

- 既存機能への影響：Dependency Check ウィンドウとそのテキスト出力の文言・形式のみ（`[重複]` → `[複数グループ]`、経由の表記の追加）。デバッグウィンドウ・リークのログ出力・Runtime には影響しない。`BuildVisibleRows` の挙動は変わらない
- リスク：
  - 走査の時間が変わる。`GetDependencies(path, false)` を読み済みのアセットは飛ばしながら辿るため、多くの場合は修正前より速くなる見込み。一方、集約は親ごとにグラフを辿るため、親が多く依存が深い構成では集約の時間が増え得る（経由先ごとの結果は使い回す）。手動検証で、サンドボックスの規模で問題ないことを確認する
  - `GetDependencies(path, false)` の戻り値に自分自身が含まれる版があるため、自分自身は常に除く
  - `PackageInfo.FindForAssetPath` が `null` を返すパッケージ（組み込みなど）は、従来どおりのパス解決になり、サイズが 0 になる場合がある
- 永続的ドキュメント（Docs/）への影響：2章「ドキュメント」の表のとおり。設計仕様書 6章は、過去の項目を書き換えず、新しい項目を追記した
- テストへの影響：`DependencyCheckAggregatorTests`・`DependencyCheckLogFormatterTests` を修正。ほかの既存テストは無修正で通ること

## 5. 図表

クラス図・シーケンス図は恒久的な価値があるため、`src/AddressableManager/Docs/diagrams/editor-dependency-check-class.md` を更新済み（インデックスの登録は変更なし）。判定の例：

```
Title.prefab [A] ──→ Btn.mat [B]（登録済み）──→ Tex.png（未登録）

Tex.png の参照元：
  Btn.mat      [B]                    … 直接（未登録のアセットだけを通って届く）
  Title.prefab [A]（経由：Btn.mat）   … 経由（間に登録済みの Btn.mat がある）
グループ数 2 → 複数グループ
```
