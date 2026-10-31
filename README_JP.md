# MAH-AddressableManager

[English](README.md) | 日本語

> 最終更新日：2026-10-24 ／ 対象 Unity バージョン：6000.3.26f1

Unity Addressables の読み込み〜解放を一括管理する汎用モジュールです。

- 導入を検討している方 → このまま下の「導入」「使い方」を参照してください
- 開発に参加する方 → まず [CONTRIBUTING.md](CONTRIBUTING.md) を読んでください

---

## 特徴

- Addressables の参照カウント・handle を意識せず、`LoadAsync<T>` と `Release` だけで扱える
- `LoadAsync<T>` は常に**読み込んだアセット本体**を返す（`GameObject` の場合は Prefab アセット）
- 明示解放が主経路。解放し忘れた分は、Load 時に指定した寿命（Manual / Scene / Owner）の終了時に自動回収される
- 初期化は自動（Manager の起動時）／手動を設定で選べる。完了・失敗は `await` とイベントの両方で受け取れる
- 初期化・ダウンロード・読み込み・解放の操作は、すべて `IAddressableHandler`（`AddressableManager.AccInstance.AccHandler`）1つから行う
- 更新データの確認・ダウンロードを Label 単位で行える。確認ダイアログ用の「件数・容量」の取得、失敗時のリトライ、進捗の通知を備える
- Label を指定して、型の異なるアセットをまとめて読み込み・まとめて解放できる
- `[AddressablePath]` / `[AddressableLabel]` 属性で、アドレス・Label を Inspector のプルダウンから選べる（手入力によるタイポを防ぐ）
- 非同期処理は UniTask を前提とする（UPM で導入すると自動で UniTask 版になる）。UniTask が無いプロジェクトでは `Awaitable` で動作する
- DI コンテナ（VContainer 等）・R3 のような外部ライブラリに依存しない
- 読み込み状態・リーク・Owner の回収を確認できるデバッグウィンドウと、Addressables に未登録のアセットがどこでどのくらい使われているかを Play もビルドもせずに確認できるウィンドウを備える（[Editor ツール](#editor-ツール)）

## 動作環境

| 項目 | バージョン |
| --- | --- |
| Unity | 6000.3.26f1 で動作確認 |
| Addressables | 2.10.3 で動作確認 |
| UniTask（任意・推奨） | 2.5.0 以上・**UPM 経由での導入が必須**（`.unitypackage` で導入すると認識されず、Awaitable 版で動作する） |

## 導入

Unity Package Manager（UPM）で、GitHub の git URL から導入します。パッケージ名は `com.github.mah-lib.addressable-manager` です。

UniTask を使う場合は、**先に UniTask を UPM で導入**してください。導入済みなら、本モジュールは自動で UniTask 版になります。

### Package Manager から導入する

1. 次の URL をコピーします

   ```
   https://github.com/MAH-Lib/MAH-AddressableManager.git?path=MAH-AddressableManager
   ```

2. `Window > Package Manager` を開き、左上の **+** から **Add package from git URL...** を選びます
3. 入力欄にコピーした URL を貼り付け、**Install**（**Add**）を押します

既定ブランチ（`main`）の最新が導入されます。

### manifest.json に直接書いて導入する（バージョン指定）

プロジェクトの `Packages/manifest.json` の `dependencies` に追記します。Unity に戻ると自動で取得されます。

```json
{
  "dependencies": {
    "com.github.mah-lib.addressable-manager": "https://github.com/MAH-Lib/MAH-AddressableManager.git?path=MAH-AddressableManager#vX.Y.Z"
  }
}
```

### バージョンの指定について

- manifest.json の URL の末尾の `#` の後ろに**リリースのタグ名**（`v` + バージョン番号）を書くと、そのバージョンに固定されます
- 例の `vX.Y.Z` は、導入したいバージョンのタグ名に置き換えてください（公開中のバージョンは [リリースノート（英語）](Docs/release-notes.md) を参照）
- バージョンを上げるときは、`#` の後ろを新しいタグ名に書き換えます
- 既存の `dependencies` の中に行を追加してください（上の例は追加する行だけを示しています）
- 公開されているバージョンと変更内容は、[リリースノート（英語）](Docs/release-notes.md) と [CHANGELOG.md](CHANGELOG.md) を参照してください

## 使い方

コードの例は UniTask 版で記載しています。UniTask が無いプロジェクトでは、戻り値が `UniTask` の代わりに `Awaitable` になります（呼び出し方は同じです）。

### 1. 設定アセットを作る

Project ウィンドウで `Create > MAH > Addressable > RuntimeSettings` を選び、設定アセットを作ります。初期化の開始方法・ダウンロード対象の Label・リトライ回数などをここで設定します（各項目は [設定アセット（英語）](Docs/usage.md#1-settings-asset) を参照）。

### 2. Manager をシーンに置く

起動シーンの GameObject に `AddressableManager` コンポーネントを付け、Inspector で 1. の設定アセットを割り当てます。

- Manager は `DontDestroyOnLoad` で常駐し、シーンを切り替えても残ります。2つ目以降に起動した Manager は自動で破棄されます
- 利用者のスクリプトの `Awake` より先に起動するため、`Awake` から `AddressableManager.AccInstance` を参照できます
- 設定アセットが未割り当ての場合は、警告を出して既定値で動作します

### 3. 読み込んで、使い終わったら解放する

```csharp
using System;
using MAH.AddressableManager;
using MAH.AddressableManager.Attribute;
using UnityEngine;

public sealed class EnemyView : MonoBehaviour
{
    // Inspector のプルダウンからアドレスを選ぶ
    [SerializeField, AddressablePath] private string _spriteAddress;

    private AssetTicket<Sprite> _ticket;
    private bool _hasTicket;

    private async void Start()
    {
        var handler = AddressableManager.AccInstance.AccHandler;
        try
        {
            // 読み込んだアセット本体と、解放用のチケットが返る
            _ticket = await handler.LoadAsync<Sprite>(
                AssetKey.FromPath(_spriteAddress), AssetLifetime.Manual, destroyCancellationToken);
            _hasTicket = true;
            GetComponent<SpriteRenderer>().sprite = _ticket.AccAsset;
        }
        catch (OperationCanceledException)
        {
            // 読み込み中に破棄された。チケットは発行されないため、解放は不要
        }
    }

    private void OnDestroy()
    {
        // 使い終わったらチケットを返して解放する（明示解放が基本）
        if (_hasTicket) AddressableManager.AccInstance?.AccHandler.Release(_ticket);
    }
}
```

- `LoadAsync<GameObject>` が返すのは Prefab アセットです。インスタンス化は利用者側で `Instantiate` してください
- 寿命（`AssetLifetime`）に `Scene()` や `Owner(gameObject)` を指定すると、解放し忘れた分がシーンのアンロード・GameObject の破棄の時点で自動回収されます。自動回収は解放し忘れに備える安全網で、通常は `Release` を呼んでください

### もっと詳しく

設定アセットの全項目、初期化・データ更新（ダウンロード）・読み込み/解放・属性の API の使い方は [使い方（API リファレンス・英語）](Docs/usage.md) にまとめています。

## サンプル

Package Manager で MAH-AddressableManager を選び、**Samples** から **Import** すると、`Assets/Samples/MAH-AddressableManager/` 以下にコピーされます。サンプルには Addressables の設定が含まれないため、動かす前にアドレス・Label の登録とシーンの Build Settings への追加が必要です（[準備の手順（英語）](Docs/usage.md#82-setup-before-running)）。

| サンプル | 内容 |
| --- | --- |
| Load and Release | Prefab・Sprite・Audio・Label 一括の読み込みと解放を、寿命（Manual / Scene / Owner）の違いとあわせて確認する。シーンを切り替えて Scene 寿命の自動回収とリーク警告も確かめられる |
| Initialize | 自動／手動の初期化と、初期化前の Load の扱い（PassThrough / WaitForInitialize）、完了・失敗の受け取り方（`await` とイベント）を確認する |
| Download | 更新データの有無の判定・件数と容量の取得・進捗つきのダウンロード・キャンセルを確認する（実際のダウンロードにはリモート配信の環境が必要） |

各サンプルのシーン・操作・確認できることは [使い方：サンプル（英語）](Docs/usage.md#8-samples) を参照してください。

## Editor ツール

どれもビルドには含まれません。デバッグウィンドウと Dependency Check は読み取り専用で、アセットの登録の2つのツールだけが、指示したときに限って Addressables の設定に書き込みます。ツールの表示は日本語と英語を切り替えられ（`Edit > Preferences > MAH Addressable > Language`）、JSON ファイルで他の言語を足すこともできます（[使い方：Editor Language（英語）](Docs/usage.md#66-editor-language)）。

| ツール | 開き方 | 内容 |
| --- | --- | --- |
| デバッグウィンドウ | `MAH/Addressable/Debug Window` | Play 中の状態をタブで確認する。「実行時状態」：読み込み済みのアセット・参照カウント・寿命ごとの確保数・確保元のスタックトレース／「リーク」：`Release` されずに自動回収されたものを確保元ごとに集計し、コードへジャンプ・テキスト出力できる／「Owner」：Owner 寿命の監視状態と、回収の経路 |
| Dependency Check | `MAH/Addressable/Dependency Check` | 登録済みアセットが使っている未登録のアセットを、Play もビルドもせずに一覧する。複数のグループから使われているものを強調し、Group の見直しに使う |
| 自動登録（Auto Registration） | `MAH/Addressable/Auto Registration`、`Create > MAH > Addressable > AutoRegistrationRule` で作るルールの SO | 利用者が書いたルール（対象フォルダ・拡張子・アドレス・グループ・Label）に従って、インポートのたびにアセットを Addressables に登録する。ルールを作るまでは何も起きない。[使い方：Auto Registration（英語）](Docs/usage.md#63-auto-registration) |
| 手動一括登録（Register to Addressables） | Project ウィンドウで右クリック → `MAH` → `Register to Addressables...` | 選んだフォルダの配下を一覧に出し、拡張子とチェックで絞り込んで、グループにまとめて登録する。[使い方：Register to Addressables（英語）](Docs/usage.md#64-register-to-addressables-manual) |
| `[AddressablePath]` / `[AddressableLabel]` | string フィールドに付ける属性 | Inspector でアドレス・Label をプルダウンから選べる |
| Editor 設定 | `Create > MAH > Addressable > EditorSettings`（無ければ起動時に `Assets/MAH.Data/AddressableManager/Editor/` に自動作成） | デバッグウィンドウの更新間隔、リークのログ・Dependency Check の出力先、Play 終了時のリークの自動出力、アセットの登録の既定値 |
| 表示言語（Language） | `Edit > Preferences > MAH Addressable`、`MAH/Addressable/Localization/Export Translation Template` | ツールの表示言語を日本語と英語で切り替える（個人ごとの設定）。雛形を書き出して翻訳すれば、言語を足したり、同梱の訳を直したりできる。[使い方：Editor Language（英語）](Docs/usage.md#66-editor-language) |

使い方の詳細は [使い方：Editor ツール（英語）](Docs/usage.md#6-editor-tools) を参照してください。

## ドキュメント

- 利用者向け
  - [使い方（API リファレンス・Editor ツール・サンプル。英語）](Docs/usage.md)
  - [リリースノート（英語）](Docs/release-notes.md)
  - [用語集](Docs/glossary.md)
- 開発者向け
  - [設計仕様書](src/AddressableManager/Docs/addressable-manager-design.md)
  - [コーディング規則](src/AddressableManager/Docs/conventions.md)
  - 機能ごとの詳細仕様は [`src/AddressableManager/Docs/specs/`](src/AddressableManager/Docs/specs/) を参照

## 開発に参加する方へ

ブランチ運用・PR・CHANGELOG・リリース手順は [CONTRIBUTING.md](CONTRIBUTING.md) にまとめています。作業前に必ず読んでください。

- Unity で開くのは `src/AddressableManager/` です。リポジトリルートは Unity プロジェクトではありません
- 開発は `src/AddressableManager/Assets/@MAH/MAH-AddressableManager/` で行います。ルートの `MAH-AddressableManager/` は**配布用**で、リリース時に開発側から複製します（直接編集しないでください）
- 設計変更を提案する前に、[設計仕様書](src/AddressableManager/Docs/addressable-manager-design.md) 5.3「今回あえて採用しない設計」を確認してください

## 変更履歴

[CHANGELOG.md](CHANGELOG.md) と [リリースノート（英語）](Docs/release-notes.md) を参照してください。
