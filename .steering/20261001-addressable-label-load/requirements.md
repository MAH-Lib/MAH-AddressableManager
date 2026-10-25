# 要求内容：AddressableManager Label 一括ロード

> 作業ディレクトリ：`.steering/20261001-addressable-label-load/` ／ 作成日：2026-10-01 ／ ステータス：承認済み ／ 対象 Unity バージョン：6000.3.23f1
> 関連する永続的ドキュメント：`src/AddressableManager/Assets/Docs/addressable-manager-design.md`（3.5・3.6・Controller の責務・5.3・6章。更新要。5.3 の「AssetKey に型を含める設計」の却下を取り消す）／`src/AddressableManager/Assets/Docs/specs/loading-spec.md`（「Label 一括ロード」の章を新設。3.1・6.1・7.1・7.3・8章も更新要。今回の機能の正）／`src/AddressableManager/Assets/Docs/diagrams/loading-class.md`（更新要）／`src/AddressableManager/Assets/Docs/conventions.md`（7.2・10章に新しい型の分類を追記。パターンは変えない）／`Docs/glossary.md`（「Label 一括ロード」「まとめチケット」を追加）

## 1. 変更・追加する機能の説明

`IAssetHandler` に、Label を指定して一括で読み込む `LoadByLabelAsync(label, lifetime, ct)` を追加する。あわせて、読み込んだ分を1回でまとめて解放する `Release(AssetGroupTicket)` を追加する。

Load の時点では型を指定しない。型は、まとめチケットから取り出すときに `TryGetAsset<T>` / `GetAssets<T>` で指定する。こうすることで、AudioClip・Prefab・Sprite などが混ざった Label でも、中身の型を知らずに扱える。

前提として、重複ロードの判定キーを `(AssetKey, Type)` に変える。同じアセットを Label で読んだ分と単体で読んだ分を、型ごとに共有するため。

今やる理由：今の実装では Label 一括ロードを安全に使えない。
- Label 文字列を `AssetKey.FromPath` に渡すと、エラーも出ずに1件しか読まれない
- 型が混ざると、型違いの例外になる
- Label 単位で解放する手段が無い

あわせて、既存の不具合も直す。ロード失敗時の巻き戻しが、同じキーで新しく作られたエントリの RefCount を誤って減らすおそれがある。

## 2. ユーザーストーリー

- 実装担当者として、どの Label にどの型のアセットが入っているかを把握していなくても使えるように、Label を指定するだけでまとめて読み込みたい（中〜大規模では、型の把握が不可能なため）
- 実装担当者として、Label で読み込んだものを1つずつ解放しなくて済むように、1回の呼び出しでまとめて解放したい
- 実装担当者として、読み込んだアセットのうち必要な型のものだけを使えるように、取り出すときに型を指定したい
- 実装担当者として、一括ロードの途中で失敗やキャンセルが起きても、読み込みかけの分がリークしないようにしたい
- レビュアーとして、Label ロードを追加しても二重解放や RefCount の不整合が起きないように、解放の経路は既存の1本だけにしておいてほしい

## 3. 受け入れ条件

### 読み込みと取り出し
- [x] `LoadByLabelAsync(label, lifetime, ct)` を呼ぶと `AssetGroupTicket` が返り、Label 内のアセットがすべて読み込まれている。型が混ざっていても同じ
- [x] `GetAssets<T>()` は、読み込んだ型が `T` と完全一致するものをすべて返す
  - 基底型では探さない（`Object` を指定すると空になる）
  - 同じ型での2回目以降の呼び出しは、同じインスタンスを返す
  - 戻り値は `ReadOnlyCollection` で、`List<T>` にキャストできない
  - 0件、または解放済みのときは空を返す
- [x] `TryGetAsset<T>(address, out T)` は、そのアドレスに `T` 型のアセットがあれば `true` を返す。無ければ `false` と `null` を返す
- [x] `UnityEngine.Object` ではない型（`SceneInstance` など）が混ざった Label では、その分が除外され、警告が出る。残りは読み込まれる
- [x] 該当0件の Label では、警告が出て、空のまとめチケットが返る
- [x] 同じアドレス・同じ型なら、単体の `LoadAsync<T>` とエントリを共有する（実ロードは1回）。型が違えば別のエントリになる

### 解放と自動回収
- [x] `Release(group)` を呼ぶと、中のすべてのエントリの RefCount が減り、0 になったものは Worker が破棄される
- [x] 同じまとめチケットを2回 `Release` しても、警告が出るだけで RefCount は壊れない。`null` を渡した場合も同じ
- [x] Scene / Owner 寿命の Label ロードは、寿命が終わると全件まとめて回収される
  - リーク警告は、まとめチケット1枚につき1件だけ出る（label と件数を表示）
  - 回収後のまとめチケットは `AccIsReleased == true` になり、`GetAssets<T>()` は空を返す
- [x] Owner 寿命の Label ロードでは、Watcher の登録が1回だけ行われ、全件が解放されると登録が外れる

### 異常系
- [x] 1件でもロードに失敗すると、残りのロードを待たずに全件が巻き戻され（RefCount が 0 に戻る）、`AssetLoadException` が届く
- [x] ct をキャンセルすると、全件が巻き戻され、`OperationCanceledException` が届く
- [x] ロード中に寿命が終わると、全件が回収され、`OperationCanceledException` が届く
- [x] 例外の優先順位は次のとおり
  1. ct のキャンセル → `OperationCanceledException`
  2. 寿命の終了 → `OperationCanceledException`
  3. それ以外 → 最初の `AssetLoadException`
- [x] 同じ Label に対して並行に `LoadByLabelAsync` しても、2枚のまとめチケットを互いに影響なく解放できる
- [x] ロード中に `Dispose` すると、全件が強制解放され、待機中の呼び出し元には `OperationCanceledException` が届く（他の例外は出ない。単体の Load と同じ動作）
- [x] ロードに失敗したエントリが削除されたあと、同じキーで新しいエントリができても、巻き戻しが新しいエントリの RefCount を減らさない（既存の不具合の修正。単体の `LoadAsync` でも同じ）

### 既存機能とサンプル
- [x] 既存の EditMode テスト（`LoadingControllerLoadTests`・`LoadingControllerLifetimeTests`・`LoadingControllerPollingTests`）が、判定キーの変更と解放経路の変更のあとも通る。型違いの例外を期待していた1件は、「別エントリとして読める」に書き換えて通る
- [x] サンプルシーン `LabelLoadScene` で、次の操作が確認できる
  - Manual・Scene の Label の読み込みと、Sprite・Prefab・AudioClip（BGM・SE）の取り出し。Go to ボタンの下に、Manual と Scene の2行を縦に並べ、1回読み込むごとに、画像と Prefab のインスタンスの組を右へ追加する
  - Play BGM・Play SE で、Manual と Scene の両方で読み込んだ音が再生される
  - まとめての解放（最後に読み込んだ組の画像とインスタンスが消え、すべて解放すると Manual の音が止まる）と、二重解放
  - Scene 寿命の自動回収（リーク警告はまとめチケット1枚につき1件）
  - 該当0件の Label
- [x] `LabelLoadScene` の画面が、縦長 1080x2520 と横長 1920x1080 の両方で見切れない（2026-10-02 ユーザーが目視で確認）
- [x] 既存の3シーンから `LabelLoadScene` へ遷移できる

## 4. 制約事項

- 非同期処理の既定は `System.Threading.Tasks.Task`。UniTask は `#if ADDRESSABLE_UNITASK_SUPPORT` の出し分けの中でのみ使い、直接 `using` しない（CLAUDE.md「設計の絶対ルール」）
- VContainer・R3・その他の DI/Rx コンテナに依存させない
- MonoBehaviour を継承するのは Manager / View のみ。Controller・Service・Model はピュア C#
- 明示解放・自動回収・巻き戻しは、すべて既存の `ReleaseInternal` の1経路を通す。別の解放経路を作らない
- RefCount は await の前に加算する
- 複数のアセットを管理する別の Worker（`AddressableMultiLoaderBase<T>` 相当）は作らない。1件ずつの Load を Controller の中で組み合わせる
- Worker を実行時の型から生成するときは、`MakeGenericMethod` と `Delegate.CreateDelegate` でキャッシュした delegate を使う。`Activator.CreateInstance`・`Expression.Compile` は使わない（IL2CPP での動作とストリッピングのため）
- 一時コレクションの扱い
  - 毎フレーム走る処理では、一時コレクションを都度確保しない
  - ロケーションのバッファは、await をまたいで並行に使われるため、例外として呼び出しごとに確保する
- 仕様書（設計の根幹にあたる判定キーの変更）の承認前に、実装に着手しない
- 命名・namespace は `src/AddressableManager/Assets/Docs/conventions.md`、用語は `Docs/glossary.md` に従う
- サンプルの画面の文字は英語にする

## 5. スコープ外（今回やらないこと）

- `AssetKey` への Label 種別の追加（まとめチケットが label 文字列を持てば足りるため）
- Label 文字列を指定して解放する API（`ReleaseByLabel`）。同じ Label を複数の呼び出し元が読んだ場合に、どの分を解放するか決められないため
- Sprite Mode が Multiple の画像や Sprite Atlas の中身をすべて取り出すこと（1つのアドレスにつき最初の1枚だけ。制限事項として仕様書に書く）
- `UnityEngine.Object` ではない ResourceType（`SceneInstance` など）の読み込み（除外して警告を出す）
- 重複アドレスの区別、および「Include Addresses in Catalog」が無効な場合への対応（制限事項として書く。重複は検出して警告を出す）
- Task 版（UniTask を入れていない環境）のコンパイル確認（ユーザーが行う）
- Addressables の設定変更（コミット済みの既存ラベル `Manual` / `Scene` / `Owner` をそのまま使う）
- PlayMode 自動テストへの Label ケースの追加
- `CHANGELOG.md`・利用者向けドキュメント（`Documentation~/` など）の更新
