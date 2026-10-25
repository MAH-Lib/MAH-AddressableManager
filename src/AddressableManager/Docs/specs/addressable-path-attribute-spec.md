# 機能仕様書：AddressablePath プルダウン属性

> 最終更新日：2026-10-08 ／ 対象 Unity バージョン：6000.3.26f1
> 命名パターンは [`conventions.md`](../conventions.md)、用語は `Docs/glossary.md`（リポジトリルート）が正。全体の位置づけは [`addressable-manager-design.md`](../addressable-manager-design.md) 3.9(1) を参照。

## 1. 目的

Inspector 上で Addressable のアドレスを string フィールドに手入力させると、タイポや存在しないアドレスへの参照が起きる。プルダウンから選択させることでこれを防ぐ。

読み込み・解放機能（[`loading-spec.md`](loading-spec.md)）の PlayMode テストとサンプルで使うアドレスは、この属性で選ばせたものに限定する（テストコード・サンプルにアドレスを直書きしない）。

## 2. 対象範囲

- 対象：`AddressablePathAttribute` を付けた string フィールドの Inspector 表示
- 対象外：`AssetKey` 自体を Inspector から選ぶ仕組み（利用者向けのキー定義 SO は別途。本属性はそのキー定義 SO の内部実装として使う想定）
- 対象外：Addressable グループ・ラベルの運用ルール（[`conventions.md`](../conventions.md) 9章のとおり、本モジュールの対象外）

## 3. 構成

| クラス | namespace | 配置 |
|---|---|---|
| `AddressablePathAttribute`（sealed） | `MAH.AddressableManager.Attribute` | `Runtime/Attribute/AddressablePathAttribute.cs` |
| `AddressablePathDrawer`（internal sealed） | `MAH.AddressableManager.Editor.Attribute` | `Editor/Attribute/AddressablePathDrawer.cs` |
| `AddressablePathDropdown`（internal sealed） | `MAH.AddressableManager.Editor.Attribute` | `Editor/Attribute/AddressablePathDropdown.cs` |

属性クラス（`PropertyAttribute` 派生）はランタイム側に置き、`PropertyDrawer` のみ `Editor/` に置く。アドレス一覧の取得元 `AddressableAssetSettings` はエディタ専用アセンブリからしか参照できないため、この分離が必須（[`addressable-manager-design.md`](../addressable-manager-design.md) 3.9(1)）。

```csharp
namespace MAH.AddressableManager.Attribute
{
    /// <summary> Addressable のアドレスを Inspector のプルダウンから選択できるようにする属性 </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class AddressablePathAttribute : PropertyAttribute
    {
    }
}
```

## 4. Drawer の挙動

```csharp
namespace MAH.AddressableManager.Editor.Attribute
{
    [CustomPropertyDrawer(typeof(AddressablePathAttribute))]
    internal sealed class AddressablePathDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.HelpBox(position, $"{nameof(AddressablePathAttribute)} は string フィールドにのみ使用できます", MessageType.Error);
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                // Addressables の設定が無いプロジェクトでは通常の文字列入力に戻す
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            // プルダウンのボタンを描画し、押されたら AddressablePathDropdown を開く
            // 選ばれたエントリの address を property.stringValue に書き込む

            if (!string.IsNullOrEmpty(property.stringValue) && !AddressExists(settings, property.stringValue))
            {
                // 存在しないアドレスへの警告（フィールドの下に HelpBox で表示）
            }
        }
    }
}
```

挙動の詳細：

| 状況 | 挙動 |
|---|---|
| 対象フィールドが string 以外 | HelpBox でエラー表示（プルダウンは出さない） |
| Addressables の設定（`AddressableAssetSettingsDefaultObject.Settings`）が無い | 通常の文字列入力欄に戻し、注意書きを添える |
| 保存済みのアドレスが Addressables に存在しない | フィールドの下に HelpBox で警告を出す（アドレスを後から変更した場合に気づけるようにする） |
| プルダウンを開く | `AddressableAssetSettings` の全グループのエントリを集め、`AddressablePathDropdown`（`AdvancedDropdown` 派生）でグループごとに表示する。検索ボックスで絞り込める |
| アドレス一覧の取得タイミング | プルダウンを開くたびに集め直す（キャッシュしない）。件数は数百件程度で、開く操作自体が頻繁ではないため |

## 5. 制約

- Editor の asmdef が `Unity.Addressables.Editor` を参照している必要がある（`Editor/Attribute/` はこの制約の対象）
- Runtime 側の `AddressablePathAttribute` は Addressables の Editor API を一切参照しない（ビルドに含まれるコードから Editor 専用 API への依存を避けるため）
- アドレス数が数百件規模になるプロジェクトでは、検索ボックス付き `AdvancedDropdown` を用いることでプルダウンの実用性を保つ

## 6. テスト用キー定義での使い方

読み込み・解放機能の PlayMode テストは、この属性を使ったテスト専用の ScriptableObject からアドレスを読み込む。

```csharp
namespace MAH.AddressableManager.Tests.Runtime
{
    [CreateAssetMenu(menuName = "MAH/Addressable/Tests/LoadingTestKeys")]
    public sealed class LoadingTestKeysSO : ScriptableObject
    {
        [SerializeField, AddressablePath] private string _spriteAddress;
        [SerializeField, AddressablePath] private string _prefabAddress;

        public string AccSpriteAddress => _spriteAddress;
        public string AccPrefabAddress => _prefabAddress;
    }
}
```

- `Tests/Runtime/Data/LoadingTestKeys.asset` はユーザーが Inspector でプルダウンから設定する
- PlayMode テストは、Editor 上で `AssetDatabase.LoadAssetAtPath<LoadingTestKeysSO>(...)` でこのアセットを読み込み、アドレスを取り出す。テストコードにアドレスを直書きしない
- EditMode テストでも、`LoadingTestKeys.asset` の各アドレスが Addressables の設定に実在するかを確認する（アドレスの変更で PlayMode テストが分かりにくい失敗をするのを防ぐ）

サンプル（`Samples/AddressableLoad/Scripts/AssetLoadSampleViewBase`）のアドレスのフィールドにも、同じ属性を付ける。
