# 機能仕様書：Editor ツールの表示言語の切り替え（Localization）

> 最終更新日：2026-10-24 ／ 対象 Unity バージョン：6000.3.26f1 ／ 実装状況：**実装済み（仕組み・Preferences・雛形の書き出し・全 Editor ツールの移行）**
> **Editor 機能8「表示言語の切り替え」の仕様はこのファイルを正とする**。全体の位置づけは [`addressable-manager-design.md`](../addressable-manager-design.md) を参照。
> 利用者向けの使い方（言語の追加の手順）は、リポジトリルートの [`Docs/usage.md`](../../../../Docs/usage.md) の「6.6 Editor Language」が原本。本書には複製しない。

---

## 1. 概要

Editor ツール（ウィンドウ・Inspector・ダイアログ・Editor 側の Console ログ・出力ファイル）の文言を、**日本語と英語で切り替える**。公開パッケージで海外の利用者も使うため。他言語は、**利用者がコードを変えずに JSON ファイルで足せる**ようにする（要望のたびにパッケージを更新しなくて済み、git URL で入れたパッケージは読み取り専用でも使える）。

| できること | 内容 |
|---|---|
| 切り替え | `Edit > Preferences > MAH Addressable > Language`。個人ごとの設定（EditorPrefs） |
| 初期値 | 未設定のときは PC の言語設定から決める。**日本語を優先**（4章） |
| 言語の追加 | `Assets/` 配下に `MAH.AddressableManager.Editor.<言語コード>.json` を置く。雛形はメニューで書き出せる |
| 同梱の訳の修正 | 同じ言語コードのファイルを置くと、**キー単位で**同梱の訳を上書きする |

**この機能が持たないもの**：
- **Runtime のログ・例外メッセージの翻訳**。Runtime に翻訳の仕組みを入れるとビルドにも載るため（日本語のまま）
- Inspector の項目名（`Target Group` など）の翻訳。Unity が変数名から自動で付ける英語のまま
- 言語の追加を、Editor SO（プロジェクトで共有される設定）に持たせること。言語は人ごとの好みのため

---

## 2. 構成

`Editor/Localization/`（namespace `MAH.AddressableManager.Editor.Localization`。Editor asmdef は分割せず1つのまま）。

| 型 | 役割 |
|---|---|
| `EditorText`（static） | 文言の窓口。`Get(key)`・`Format(key, args)`・`TryGet(key, out text)`・`SetLanguage`・`Reload`・`LanguageChanged`。初回の呼び出しで言語ファイルを読む |
| `EditorLanguageTable` | **ピュア C#**。JSON の解析・同梱分と利用者分の統合・フォールバック付きの引き当て・雛形の生成・書式の引数の数え上げ。AssetDatabase に依存せず、EditMode でテストできる |
| `EditorLanguageFile` / `EditorLanguageEntry` | JSON の形（`[Serializable]`。`JsonUtility` は Dictionary を扱えないため、キーと値の組の配列） |
| `EditorLanguageFileLocator` | 言語ファイルを探して読む（同梱分・利用者分） |
| `EditorLanguagePreferences` | EditorPrefs への言語コードの読み書きと、未設定のときの初期値 |
| `EditorLanguagePostprocessor` | 言語ファイルのインポート・削除・移動と、ドメインのリロード後に、読み直す（`AssetPostprocessor`） |
| `EditorLanguageSettingsProvider` | Preferences の画面（`SettingsScope.User`） |
| `EditorLanguageTemplateCreator` | メニュー `MAH/Addressable/Localization/Export Translation Template` |
| `LocalizedPropertyView` | Inspector の描画部品。見出し・ツールチップをキーで引いて描く（MonoBehaviour ではない） |
| `AddressableEditorSettingsEditor` / `AddressableRuntimeSettingsEditor`（`SettingsSync/`） | 2つの設定 SO の Inspector（`LocalizedPropertyView` で描く） |

---

## 3. 言語ファイル

### 3.1 形式

```json
{
    "languageCode": "fr",
    "languageName": "Français",
    "entries": [
        { "key": "debug.notRunning", "value": "…" }
    ]
}
```

- ファイル名は **`MAH.AddressableManager.Editor.<言語コード>.json`**（言語コードは英数字・`-`・`_`）。探すときの目印。
- `languageCode`（空でないこと）が、言語の識別に使われる値で、ファイル名の言語コードとは独立している（雛形では、ファイル名と同じ値を入れる）。
- `languageName` は、Preferences の選択肢に出す名前。空なら言語コードを出す。
- `value` の書式の引数（`{0}`・`{1}` …）は `string.Format` で埋める。**引数の数は、英語と同じにする**（同梱の ja と en は、テストで一致を確かめる）。

### 3.2 探す場所と優先順位

1. **同梱分**（`ja`・`en`）：Editor asmdef のフォルダ（`CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName`）の `Localization/`。開発フォルダ（`Assets/` 配下）でも、配布先（`Packages/` 配下）でも同じコードで動く。**AssetDatabase が使えないタイミング（起動直後）でも読めるよう、ファイルを直接読む**（`Packages/` のパスは `PackageInfo.FindForAssetPath` で実際の場所に変換する）。
2. **利用者の追加分**：`AssetDatabase.FindAssets` で見つけた、`Assets/` 配下の同じ名前の TextAsset（置き場所は自由）。起動直後や Refresh の最中など `EditorApplication.isUpdating` のときは読まない。`EditorLanguagePostprocessor` が、言語ファイルのインポート・削除・移動とドメインのリロード後に、**AssetDatabase の Refresh の完了後（`EditorApplication.delayCall`）に1回だけ**読み直しを予約する（Refresh の最中に読み直すと、利用者のファイルを読み飛ばすため）。

同梱 → 利用者の順に表へ足し、**同じ言語コードなら、後から足した利用者分が、キー単位で上書きする**（他のキーは同梱のまま）。

### 3.3 引き当てとフォールバック

`Get(key)`：現在の言語 → **英語** → **キー文字列そのもの**（置き換えの漏れを見つけやすくするため）の順。キーが無いときは、警告をキーごとに1回だけ出す（英語）。一部だけ訳した言語ファイルでも動く。

`Format(key, args)`：引数の数がずれた言語ファイルで `string.Format` が失敗したら、英語の文言で埋め直し（それも失敗したらキー文字列）、警告を1回だけ出す。引数が3個までのときは、`params` の配列を確保しないオーバーロードを使う。

`TryGet(key, out text)`：警告を出さない。Inspector の見出し・ツールチップのように、**キーが無いことが正常な場合**（書いたものだけが出る）に使う。

### 3.4 形式の誤り

JSON を解析できない・`languageCode` が空のファイルは、そのファイルだけを無視し、Console に警告を出す（英語。基盤自身のメッセージは、翻訳に頼れないため英語。**理由の文も英語**で、`Ignored the language file <パス>: Could not parse JSON: …` のように出る）。他の言語は動き続ける。

### 3.5 キーの命名

`<画面または機能>.<要素>`。語順が違う言語でも訳せるよう、**文の単位でキーにする**（「件」「を登録」のように語を切り出さない）。

| 接頭辞 | 対象 |
|---|---|
| `preferences.` `template.` | 言語の設定・雛形の書き出し |
| `debug.` `runtimeState.` `leak.` `owner.` `stackTrace.` `leaklog.` | デバッグウィンドウと各タブ、リークのログ |
| `dep.` | Dependency Check（画面・出力ファイル） |
| `registration.` `rule.` `rulePreview.` `ruleEditor.` `manual.` `dropdown.` | アセットの登録（ログ・ルール一覧・手動一括登録・ルールの Inspector） |
| `attr.` `settings.` | 属性の Drawer・設定 SO の探索 |
| `common.` | 複数の画面で共通の文言（区切り文字・名前なし・スコープ名） |
| `header.<SO の型名>.<propertyPath>` `tooltip.<SO の型名>.<propertyPath>` | Inspector の見出し・ツールチップ（例：`tooltip.AddressableEditorSettingsSO._registration._autoRegistration`） |

---

## 4. 表示言語の決定

1. EditorPrefs（`MAH.AddressableManager.Editor.LanguageCode`）に保存された言語コード。
2. 無ければ、**PC の言語設定**から決める（`EditorLanguagePreferences.DecideDefaultLanguageCode`。**日本語を優先する**）。OS の言語（`Application.systemLanguage`）・表示言語（`CultureInfo.InstalledUICulture`）・地域の書式（`CultureInfo.CurrentCulture`）の**どれかが日本語なら `ja`**（日本の PC で、表示言語だけ英語にしている利用者も日本語になる）。**どれも読めなかったときも `ja`**。読めて、どれも日本語でなければ `en`。
   - Unity エディタの表示言語（`LocalizationDatabase`）は使わない：internal でリフレクションが要るうえ、エディタを英語で使っている日本語ユーザーが、日本語にならないため（2026-10-24 に、日本語優先として変更）。
3. 決めた言語の言語ファイルが無ければ（保存した言語のファイルを消したなど）、**英語**で表示する（保存した値は消さない。ファイルが戻れば、その言語に戻る）。

言語が変わったとき・言語ファイルが読み直されたときは `EditorText.LanguageChanged` を発行する。

---

## 5. 画面側の作法

- 文言は **`EditorText.Get` / `Format` で、使う場所で取る**。`const string` にしない（言語が変わっても追従するため）。
- **取得時にまとめて作る文字列を持つ画面**（一覧の行・状態表示・差分の行など）は、`LanguageChanged` を購読し、作り直す。購読は `OnEnable` / `OnDisable` で対にする（解除漏れは、破棄済みのウィンドウの呼び出しにつながる）。
- `OnGUI` の中で毎回 `Format` しない（既存のキャッシュはそのまま使い、言語が変わったときだけ作り直す）。`Get` は Dictionary を引くだけで、アロケーションしない。
- 日本語固定で残す文字列：Runtime の文言、**開発者向けの例外・`Debug.Assert`**（利用者の操作では起きない）、`×` `■` `●` `▶` `✔` `←` などの記号。
- 出力ファイル（リークのログ・Dependency Check）の文言は、**出力した時点の表示言語**で書く。
- 失敗メッセージの「：」は、全角で固定しない（`: `）。言語によって区切りが違うため。

### 5.1 Inspector

`[Header]` / `[Tooltip]` は定数で切り替えられず、`PropertyField` は必ず描画してしまうため、**対象のクラスから外し**、文言は言語ファイルのキーで持つ。対象は次の3種類の SO（`AddressableRuntimeSettingsSO` と、その機能ごとの設定クラスを含む）：

- `AddressableEditorSettingsSO`（`AddressableEditorSettingsEditor`）
- `AddressableRuntimeSettingsSO`（`AddressableRuntimeSettingsEditor`）
- `AddressableAutoRegistrationRuleSO`（既存の `AddressableAutoRegistrationRuleEditor` が、`LocalizedPropertyView.Field` で描く）

`LocalizedPropertyView` は、`[Serializable]` のクラスを、標準と同じ折りたたみ（Foldout）にして再帰して描く。**展開するのは、このモジュール自身の型（namespace が `MAH.AddressableManager` 以下）だけ**。`AssetReference`・`UnityEvent`・利用者の型のように、独自の描画（PropertyDrawer）を持ち得る型は、`PropertyField` に任せる（`propertyPath` をフィールドの型でたどって判定し、結果はキャッシュする。たどれないときは展開しない）。GUIContent は言語が変わるまで使い回す。

> **Runtime の属性を外した理由**（2026-10-24）：Runtime の設定クラスに `[Header]` / `[Tooltip]` を残すと、英語を選んでも日本語が残る。文言は Editor 側の言語ファイルに移したため、ビルドには、翻訳のファイルも日本語の文字列も載らない（Editor の `Localization/` は Editor asmdef の中で、ビルドに含まれない）。

---

## 6. テスト

すべて EditMode（`Tests/Editor/`）。

| テスト | 内容 |
|---|---|
| `EditorLanguageTableTests` | 解析（正常・形式の誤り）、フォールバック、キー単位の上書き、`languageName` が空のとき、雛形に英語の全キーが入ること、欠落キーの検出、書式の引数の数え上げ、`EditorText` のフォールバック・引数のずれ |
| `EditorLanguageFileConsistencyTests` | 同梱の ja と en を実際に読み、**キーの集合が一致する**こと・同じキーで**書式の引数の数が一致する**こと |
| `EditorTestLanguageFixture`（`[SetUpFixture]`） | この名前空間のテスト全体で、表示言語を日本語に固定する。既存の Formatter などのテストの期待値（日本語）が、実行する人の設定に左右されないようにする |

---

## 7. 設計判断

- **言語を列挙型にしない**：利用者が足した言語を、コードの変更なしで扱うため。
- **翻訳データを C# ではなく JSON にする**：翻訳する人がコードに触れずに済み、パッケージ（読み取り専用）の外に置ける。読み込みと欠落の確認の仕組みが要るが、それは `EditorLanguageTable` のテストで担保する。
- **言語の設定を EditorPrefs（個人）にする**：チームで共有される Editor SO に入れると、全員が同じ言語になってしまうため。
- **利用者の言語ファイルを `Assets/` 配下に限る**：他のパッケージ内のファイルを、意図せず拾わないため。
- **雛形の書き出しは、利用者が選んだ新しいファイルにだけ書く**：Addressables の設定には触れないため、「Editor 拡張は読み取り専用」（CLAUDE.md）の例外には当たらない。
- **Runtime の文言は翻訳しない**：Runtime に翻訳の仕組みを入れるとビルドにも載る。要望があれば別の作業で検討する。

---

## 8. 今後の検討事項

- Runtime のログ・例外メッセージの翻訳（要望があれば）。
- 言語ファイルの検証用のメニュー（欠けているキーの一覧を Console に出すなど）。今は、雛形を書き出し直して比べる。
- 日英以外の言語の同梱（要望があった言語の翻訳を、Issue / Pull Request で受けて足す）。
