# 機能仕様書：AddressableLabel プルダウン属性

> 最終更新日：2026-10-08 ／ 対象 Unity バージョン：6000.3.26f1
> 命名パターンは [`conventions.md`](../conventions.md)、用語は `Docs/glossary.md`（リポジトリルート）が正。全体の位置づけは [`addressable-manager-design.md`](../addressable-manager-design.md) 3.9(1) を参照。
> 兄弟機能の [`addressable-path-attribute-spec.md`](addressable-path-attribute-spec.md) と構成・挙動を揃えている。差分のみ本書に書く。

## 1. 目的

Label 一括ロード（`LoadByLabelAsync`）に渡す Label を Inspector 上で string フィールドに手入力させると、タイポや存在しない Label の指定が起きる。プルダウンから選択させることでこれを防ぐ。

## 2. 対象範囲

- 対象：`AddressableLabelAttribute` を付けた string フィールドの Inspector 表示
- 対象外：Label の運用ルール（[`conventions.md`](../conventions.md) 9章のとおり、本モジュールの対象外）
- 対象外：「存在しない Label を読む」検証用のフィールド（サンプルの `_missingLabel` など）。意図的に存在しない値を入れるため、この属性は付けない

## 3. 構成

| クラス | namespace | 配置 |
|---|---|---|
| `AddressableLabelAttribute`（sealed） | `MAH.AddressableManager.Attribute` | `Runtime/Attribute/AddressableLabelAttribute.cs` |
| `AddressableLabelDrawer`（internal sealed） | `MAH.AddressableManager.Editor.Attribute` | `Editor/Attribute/AddressableLabelDrawer.cs` |
| `AddressableLabelDropdown`（internal sealed） | `MAH.AddressableManager.Editor.Attribute` | `Editor/Attribute/AddressableLabelDropdown.cs` |

属性クラスはランタイム側、`PropertyDrawer` のみ `Editor/` に置く理由は AddressablePath と同じ（`AddressableAssetSettings` がエディタ専用アセンブリからしか参照できないため）。

## 4. AddressablePath との差分

| 項目 | AddressablePath | AddressableLabel |
|---|---|---|
| 一覧の取得元 | 全グループのエントリの `address` | `AddressableAssetSettings.GetLabels()` |
| プルダウンの構造 | グループごとの階層 | 階層なし（Label はグループに属さないため）。「(未設定)」＋ Label 一覧 |
| 存在確認 | `address` の一致 | `GetLabels()` に含まれるか |
| 警告文 | アドレス「…」は存在しません | Label「…」は存在しません |

それ以外（string 以外はエラー表示／Addressables の設定が無ければ通常の文字列入力に戻す／プルダウンを開くたびに集め直す）は AddressablePath と同じ。

## 5. 制約

- `GetLabels()` が返すのは Addressables の Labels 設定（Groups 画面の Labels 管理）に登録された Label。どのエントリにも付いていない Label も一覧に出る
- Drawer 同士は基底クラスで共通化しない（存在判定とプルダウンが異なり、共通化すると読みにくくなるため）
- Runtime 側の `AddressableLabelAttribute` は Addressables の Editor API を一切参照しない
