# 機能仕様書：読み込み・解放（Loading）

> 最終更新日：2026-10-15 ／ 対象 Unity バージョン：6000.3.26f1
> **この機能（読み込み・解放・自動回収）の仕様はこのファイルを正とする**。全体の概要・他機能との関係は [`addressable-manager-design.md`](../addressable-manager-design.md) を参照。命名パターンは [`conventions.md`](../conventions.md)、用語は `Docs/glossary.md`（リポジトリルート）が正。
> 図は [`diagrams/loading-class.md`](../diagrams/loading-class.md)・[`diagrams/loading-sequence.md`](../diagrams/loading-sequence.md) を参照。

---

## 1. 対象範囲

`AddressableManager` の読み込み（`LoadAsync<T>`、Label 一括ロードの `LoadByLabelAsync`）と解放（`Release`、寿命による自動回収）。Download・Initialize・Editor 拡張（デバッグウィンドウ等）・Profiling は対象外（それぞれの機能を実装するときに専用の仕様書を作る）。

## 2. AddressableManager（Manager）

- 責務：機能内クラス（Controller・Service・Model・Handler）の生成と寿命管理、Unity ライフサイクルの中継、および `IAddressableHandler` を返すこと。ロジックは持たない
- 公開メンバーは `AccInstance` と `AccHandler` の2つだけ。初期化・読み込み・解放（今後はダウンロードも）の操作は、利用者が触れる入口を1つに絞るため、すべて Handler（8章）に置く
- MonoBehaviour を継承する。本モジュールで MonoBehaviour を継承するのは Manager / Editor / View のみで、Controller・Service・Model はすべてピュア C#
- static な `AccInstance` を持つ。`Awake` で自身を登録して `DontDestroyOnLoad` にし、2つ目以降の Manager は `Destroy` する
- `LoadingController` と Handler は `Awake` で生成し、`AccHandler` をすぐに公開する。Addressables は最初のロード時に自動で初期化されるため、読み込み・解放は `InitializeAsync` に依存しない
  - ダウンロードの操作も `IAddressableHandler` に追加する（別の Handler や `AccDownloadHandler` は作らない）。初期化の前に呼ばれたときは、初期化の成功を待つ（「成功後にだけ公開して `null` で判定する」案は Handler が1つのため使えない。2026-10-08 に確定。設計仕様書 3.3・`download-spec.md`）。Download は Loading を参照せず、Loading の Registry・RefCount を触らない
  - `InitializeAsync()`（自動／手動の切り替え・状態・イベント。Handler に置く）と、SO で初期化を待たせる設定（`IInitializationGate` を `LoadingController` に注入する）の仕様は `initialize-spec.md` を参照（2026-10-02）。既定では読み込みは初期化を待たない
- `LateUpdate` と `SceneManager.sceneUnloaded` の通知はイベント経由で Controller 側に中継する（Controller は MonoBehaviour を継承せず、`SceneManager` も直接購読しない）
- `OnDestroy` 時は、購読の解除 → `LoadingController` の破棄（残 Entry を一括解放する）→ `InitializeController` の破棄 → URL 変換関数の復元、の順で行う（`initialize-spec.md` 2.3）。自身が `AccInstance` であれば `null` に戻す
- 設定は `AddressableRuntimeSettingsSO` を1つだけ受け取り（9章）、`Awake` でログレベルを `AddressableLogger` に反映する。`LoadingController` と `AssetLoaderServiceFactory` には Runtime SO 全体ではなく `AccLoading`（`AddressableLoadingSettings`）だけを、`InitializeController` には `AccInitialize`（`AddressableInitializeSettings`）だけを渡す

```csharp
// 要点のみ（全体は Runtime/Core/AddressableManager.cs。初期化の公開 API・イベントは Handler に置く。initialize-spec.md 2・4・7章）
public sealed class AddressableManager : MonoBehaviour
{
    [SerializeField] private AddressableRuntimeSettingsSO _settings;
    private static AddressableManager _instance;
    private CatalogCacheBustingService _catalogCacheBusting; // URL キャッシュ対策が OFF のときは null
    private InitializeController _initializeController;
    private LoadingController _loadingController;
    private IAddressableHandler _handler;

    /// <summary> 実行中の Manager </summary>
    public static AddressableManager AccInstance => _instance;
    /// <summary> 初期化・読み込み・解放の窓口（利用者が触れる唯一の入口） </summary>
    public IAddressableHandler AccHandler => _handler;

    /// <summary> Unityの毎フレーム更新を各Controllerへ中継するイベント </summary>
    private event Action<float> OnLateUpdate;
    /// <summary> シーンのアンロードを各Controllerへ中継するイベント </summary>
    private event Action<Scene> OnSceneUnloaded;

    private void Awake()
    {
        // 2つ目以降の Manager は破棄する
        if (_instance != null) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        AddressableLogger.AccLevel = _settings.AccLogLevel; // _settings が未設定なら、警告を出して既定値の SO を作る（省略）

        // Controller には自分の機能の設定だけを渡す。Model は Manager で生成して注入する
        var initialize_settings = _settings.AccInitialize;
        if (initialize_settings.AccEnableCatalogCacheBusting) { /* URL キャッシュ対策を適用（initialize-spec 6章） */ }
        _initializeController = new InitializeController(initialize_settings, new InitializeStateModel(), new AddressablesInitializeService());

        var loading_settings = _settings.AccLoading;
        _loadingController = new LoadingController(
            loading_settings,
            new AssetLoaderServiceFactory(loading_settings),
            new LabelLocationService(),
            new AssetLifetimeWatcherBinder(),
            _initializeController, // Loading は IInitializationGate としてしか知らない
            new AssetRegistryModel(),
            new IssuedTicketRegistryModel(),
            new OwnerWatchRegistryModel()
#if UNITY_EDITOR
            , leak_registry // リークを蓄積する Editor 限定の Model。Editor の窓口にも同じものを渡す（editor-debug-window-spec 2.1）
            , owner_reclaim_registry // Owner の回収経路を蓄積する Editor 限定の Model。窓口にも同じものを渡す（同 8.3.3）
#endif
            );
        _handler = new AddressableHandler(_initializeController, _loadingController);

        // 購読は、初期化の自動開始より前に行う（同期で完了してもイベントを取りこぼさないため。initialize-spec 2.1）
        OnLateUpdate += _initializeController.Tick;
        OnLateUpdate += _loadingController.Tick;
        OnSceneUnloaded += _loadingController.HandleSceneUnloaded;
        SceneManager.sceneUnloaded += RelaySceneUnloaded;
        // 自動モードでの初期化の開始（省略。initialize-spec 2.1）。初期化イベントは Handler が Controller へ直接つなぐため、Manager は購読しない（initialize-spec 7.7）
    }

    // 当該フレーム中のDestroyを取りこぼさないため Update ではなく LateUpdate を使用する。
    // 渡す経過時間は、ポーズ（timeScale = 0）の間も進む unscaledDeltaTime にする（5.7）
    private void LateUpdate() => OnLateUpdate?.Invoke(Time.unscaledDeltaTime);

    private void RelaySceneUnloaded(Scene scene) => OnSceneUnloaded?.Invoke(scene);

    private void OnDestroy()
    {
        if (_instance != this) return; // 重複で破棄された Manager は何も持っていない

        // 順序：購読の解除 → Loading の破棄 → Initialize の破棄 → 変換関数の復元
        SceneManager.sceneUnloaded -= RelaySceneUnloaded;
        OnSceneUnloaded -= _loadingController.HandleSceneUnloaded;
        OnLateUpdate -= _loadingController.Tick;
        OnLateUpdate -= _initializeController.Tick;
        _loadingController.Dispose(); // 残 Entry をすべて強制解放する
        _initializeController.Dispose();
        _catalogCacheBusting?.Restore();
        _handler = null;
        _instance = null;
    }
}
```

設計上の要点：
- `LateUpdate` を使用する。`Update` では同一フレーム内で後から実行された `Destroy` を取りこぼす可能性があるため、フレーム内の更新処理が終わった後に走らせる
- `LateUpdate` から中継する経過時間は `Time.unscaledDeltaTime`（`Time.deltaTime` ではない）。ポーリングは「Watcher が取りこぼした分」の回収経路で、ゲーム内の時間に従う理由がなく、ポーズ中に回収が止まるとメモリの保持が延びるため（5.7）
- Manager は Unity ライフサイクルの中継役に徹する。`LateUpdate` や `sceneUnloaded` のハンドラに処理を書かず、イベントを発火するだけに留める
- 購読の解除（`OnDestroy` での `-=`）を忘れると、破棄済み Controller が呼ばれ続けるため必ず対で行う
- Controller 側は `Tick(float deltaTime)` / `HandleSceneUnloaded(Scene scene)` のような純粋なメソッドとして実装するため、Unity のループに依存せずテスト可能（任意の値を渡して駆動できる）

## 3. LoadingController

実行時ロードの中核。何度でも並行に呼ばれ続け、アプリ生存中ずっと状態を保持する。

責務：
- `(AssetKey, 型)`（`AssetEntryKey`）による重複ロード判定（dedup）。同じキーでも型が違えば別のエントリになる（7.1）
- key ごとの参照カウント（RefCount）管理と、寿命スコープごとの確保内訳（CountByScope）管理
- 発行済みチケットの管理（通し番号による二重解放の防止）
- in-flight 中の重複呼び出しを LoadTask の共有で吸収する（Service が handle を内部に隠すため、handle 共有ではなくタスク共有で対応）
- 寿命スコープ終了時の一括回収（`ReleaseLifetime`）
- Label 一括ロードの組み立て（ロケーションの解決・全件の確保・待機・巻き戻し）とまとめチケットの管理（13章）
- `AssetRegistryModel` / `IssuedTicketRegistryModel` / `OwnerWatchRegistryModel`（いずれも Model）の読み書き。Editor では、リークを蓄積する `LeakRegistryModel`（10章）と、Owner の回収経路を蓄積する `OwnerReclaimRegistryModel`（5.7）への書き込みも行う
- `Service`（実ロード担当）の起動・破棄（生成は `IAssetLoaderServiceFactory` 経由）

Controller自身は上記Modelへの委譲と、寿命解決・Service起動といった手順の組み立てのみを担う。発行済みチケット・ポーリング対象ownerといった状態データはModel側が保持する（Modelの役割は一時データ・状態データ・SOから取得したデータの保持であり、7.3参照）。

### 3.1 `LoadAsync<T>` の手順

1. 破棄済みなら `ObjectDisposedException`
2. `key` が未初期化（`default`。`AccValue` が null／空）なら `ArgumentException`。**寿命の解決より前に確認する**。後で例外になると、Owner の Watcher だけが、チケットの無いまま残るため
   - （2026-10-02 追加）初期化ゲート（`IInitializationGate`）が閉じていれば、ここで初期化の完了を待ち、待った後にもう一度破棄済みを確認する。初期化に失敗していれば、設定に関係なくゲートは閉じており、ここで `AddressableInitializeException` になる（Addressables は呼ばない）。既定（`PassThrough`）で失敗していないときや、初期化済みのときは `await` を挟まない。待つ位置を寿命の解決・RefCount の加算より前にするのは、待機中のキャンセル・破棄で巻き戻すものを残さないため（`initialize-spec.md` 5章）
3. 寿命を `LifetimeScopeId` に解決する（Owner が破棄済み・Scene が未ロードなら `ArgumentException`。5章参照）
4. エントリを探す（判定キーは `(AssetKey, T)` の組）。無ければ Worker を生成して実ロードを1回だけ起動する。同じ `AssetKey` でも型が違えば別のエントリになる（例外にはならない）
5. RefCount と CountByScope を **await の前に加算**し、通し番号を振ったチケットを発行済みとして登録する
6. `LoadTask` を待つ。失敗・キャンセル時は `ReleaseInternal` で巻き戻して例外を再送出する
7. await の後、そのスコープがすでに回収済み（ロード中に Owner が破棄された等）なら、巻き戻して `OperationCanceledException`
8. チケットに**受け渡し済みの印を立て**（`MarkDelivered`）、`AssetTicket<T>` を返す。印を立てるのは、`await` を挟まず、返す直前に行う（3.2・10章）

```csharp
internal sealed class LoadingController : IDisposable
{
    private readonly AssetRegistryModel _registry;          // Entryの状態データ（7.3参照）。Managerから注入
    private readonly IssuedTicketRegistryModel _tickets;    // 発行済みチケットの状態データ（7.3参照）。Managerから注入
    private readonly OwnerWatchRegistryModel _ownerWatch;   // ポーリング対象ownerの状態データ（7.3参照）。Managerから注入
#if UNITY_EDITOR
    private readonly LeakRegistryModel _leaks;              // リークの蓄積（7.3参照。Editor 限定）。Managerから注入
    private readonly OwnerReclaimRegistryModel _ownerReclaims; // Owner の回収経路の蓄積（7.3参照。Editor 限定）。Managerから注入
#endif
    private readonly IAssetLoaderServiceFactory _serviceFactory;
    private readonly IInitializationGate _initializationGate;  // 初期化ゲート（initialize-spec 7.4）。Managerから注入

    // Modelはすべてコンストラクタで注入する（Modelは複数のControllerから参照される前提のため、Controller内でnewしない）
    internal LoadingController(
        AddressableLoadingSettings settings, IAssetLoaderServiceFactory service_factory, ILabelLocationService label_location_service,
        IAssetLifetimeWatcherBinder watcher_binder, IInitializationGate initialization_gate,
        AssetRegistryModel registry, IssuedTicketRegistryModel tickets, OwnerWatchRegistryModel owner_watch
#if UNITY_EDITOR
        , LeakRegistryModel leaks, OwnerReclaimRegistryModel owner_reclaims
#endif
        )
    {
        _registry = registry;
        _tickets = tickets;
        _ownerWatch = owner_watch;
#if UNITY_EDITOR
        _leaks = leaks;
        _ownerReclaims = owner_reclaims;
#endif
        _initializationGate = initialization_gate; // 読み込んでよいかを問い合わせるだけ（Initialize 機能は参照しない。initialize-spec 7.4）
        // ...(settings/serviceFactory/labelLocationService/watcherBinderの代入は省略)
    }

#if ADDRESSABLE_UNITASK_SUPPORT
    internal async UniTask<AssetTicket<T>> LoadAsync<T>(AssetKey key, AssetLifetime lifetime, CancellationToken ct = default)
#else
    internal async Task<AssetTicket<T>> LoadAsync<T>(AssetKey key, AssetLifetime lifetime, CancellationToken ct = default)
#endif
        where T : UnityEngine.Object
    {
        ThrowIfDisposed();
        // 寿命の解決（Owner の Watcher 登録）より前に確認する。後で例外になると、チケットの無い監視だけが残るため
        if (string.IsNullOrEmpty(key.AccValue))
            throw new ArgumentException("key が未初期化（default）です。AssetKey.FromPath / FromReference で作成してください。", nameof(key));

        // 初期化ゲートが閉じていれば、ここで待つ（寿命の解決・RefCount の加算より前。initialize-spec 5.1）。開いているときは await しない
        if (!_initializationGate.AccIsOpen)
            await WaitForInitializationGateAsync(lifetime, ct); // 待機中の破棄は ObjectDisposedException、寿命の終了は OperationCanceledException

        var scope_id = ResolveScope(lifetime); // Owner指定ならWatcher登録もここで行う

        // 確保（同期）：エントリの取得または作成・RefCount の加算・チケットの登録を、await の前にまとめて行う
        var entry_key = new AssetEntryKey(key, typeof(T)); // 判定キーは (AssetKey, 型)。型が違えば別のエントリになる
        if (!_registry.TryGetEntry(entry_key, out var entry))
            entry = AddAndStartEntry(entry_key, _serviceFactory.Create<T>(key)); // 単体の Load は Create<T> を直接呼ぶ（リフレクションを通さない）
        var serial = RegisterTicket(entry, entry_key, scope_id, null); // null は「まとめチケットに属さない」

        try
        {
            await entry.AccLoadTask.WithCancellation(ct); // 共有ロードタスクは常にTask。6章参照
            if (!_tickets.Contains(serial))
                throw new OperationCanceledException("ロード中に寿命が終了しました");
        }
        catch
        {
            ReleaseBySerial(serial);
            throw;
        }
        _tickets.MarkDelivered(serial); // この後は await を挟まず返す。以降に寿命が終わった分は、Release の呼び忘れ（リーク）
        return new AssetTicket<T>(key, (T)entry.AccWorker.AccAsset, scope_id, serial);
    }

    /// <summary> 新しいエントリを Registry に登録し、実ロードを1回だけ起動する </summary>
    private AssetEntryModel AddAndStartEntry(AssetEntryKey entry_key, IAssetLoaderWorker worker)
    {
        var entry = _registry.AddEntry(entry_key, worker);
        entry.StartLoad(OnLoadFailed); // 失敗したらエントリを即削除する（Registry にあるエントリが同じ場合のみ）
        return entry;
    }

    /// <summary> RefCount と CountByScope を加算し、通し番号を振ったチケットを発行済みとして登録する </summary>
    private long RegisterTicket(AssetEntryModel entry, AssetEntryKey entry_key, LifetimeScopeId scope_id, AssetGroupTicket group)
    {
        var serial = _tickets.IssueSerial();
        entry.AddCount(scope_id); // await前に加算（キャンセル/解放との競合対策）
        _tickets.Register(serial, entry_key, entry, scope_id, group);
        return serial;
    }

    /// <summary> 明示解放（主経路） </summary>
    internal void Release<T>(AssetTicket<T> ticket) where T : UnityEngine.Object
    {
        // 未発行・解放済み・default のチケットは弾く（二重解放防止）
        if (!TryRemoveTicket(ticket.AccSerial, out var record))
        {
            AddressableLogger.LogWarning($"解放済みまたは無効なチケットです：{ticket.AccKey}");
            return;
        }
        ReleaseInternal(record);
    }

    /// <summary> 明示解放も自動回収も巻き戻しも、すべてこの1経路を通す。解放は記録（record）が持つエントリを起点にする </summary>
    private void ReleaseInternal(IssuedTicketRecord record)
    {
        // Registry を引き直さない。ロード失敗で削除された後に、同じキーで別のエントリができていても、誤ってそちらを減らさないため
        var entry = record.AccEntry;
        if (!entry.TryDecrementScope(record.AccScopeId))
            return; // 既に解放済みの枠（二重解放防止）

        // RefCount が 0 になり、かつ Registry にまだ同じエントリが登録されているときだけ取り除く。
        // ロード失敗ですでに削除・破棄された後なら何もしない（Worker の二重破棄を防ぐ）
        if (entry.AccRefCount <= 0 && _registry.RemoveEntryIfSame(record.AccEntryKey, entry))
            entry.DisposeWorkerWhenCompleted(); // ロード中ならロード完了を待ってから解放する
    }

    /// <summary> 寿命スコープ終了時の一括回収（セーフティネット） </summary>
    internal void ReleaseLifetime(LifetimeScopeId scope_id)
    {
        // IssuedTicketRegistryModelが内部の再利用バッファでスナップショットを返す（8章参照）。
        // TryRemoveTicketでの本体変更中も、このスナップショットは安全に列挙できる
        var serials = _tickets.SnapshotSerialsForScope(scope_id);

        foreach (var serial in serials)
        {
            // 明示解放済みの分は既に取り除かれているので、残っている分だけが解放漏れ
            if (!TryRemoveTicket(serial, out var record))
                continue;

            // リーク警告は、呼び出し元にチケットを返し終えた分（AccIsDelivered）だけに出す（10章）。
            // 返す前（ロード中）に寿命が終わった分は、Release を呼びようがなく、OperationCanceledException が届くだけなので、Log に留める
            var group = record.AccGroup;
            if (group == null)
            {
                if (record.AccIsDelivered)
                    AddressableLogger.LogWarning(CreateLeakMessage(record.AccKey, record.AccStackTrace)); // 10章参照。実際は #if UNITY_EDITOR で、Editor のときだけ AccStackTrace を渡す（それ以外は null）
                else
                    AddressableLogger.Log($"{record.AccKey} は、ロード中に寿命が終了したため回収しました。");
            }
            else if (!group.AccIsReleased)
            {
                // まとめチケットは、1枚につき1件だけ警告する（13.7）。最初に回収した1件が、警告と無効化を行う
                if (record.AccIsDelivered)
                    AddressableLogger.LogWarning(CreateLeakMessage(group, group.AccStackTrace)); // 同上（Editor 以外は null）
                else
                    AddressableLogger.Log($"Label「{group.AccLabel}」のまとめチケットは、ロード中に寿命が終了したため回収しました。");
                group.Invalidate();
            }
            ReleaseInternal(record);
        }

        // チケットが1件も無いまま監視だけが残った Owner（確保の途中で例外が出た等）も、ここで外す。
        // チケットを取り除いた場合は TryRemoveTicket が外し済みなので、ここでは何もしない（5.4）
        UnwatchOwnerIfNeeded(scope_id);
    }

    /// <summary> チケットを取り除く。そのスコープのチケットが尽きたらWatcher登録も外す </summary>
    private bool TryRemoveTicket(long serial, out IssuedTicketRecord record)
    {
        if (!_tickets.TryRemove(serial, out record, out var scope_exhausted))
            return false;
        if (scope_exhausted)
            UnwatchOwnerIfNeeded(record.AccScopeId); // 5.4参照
        return true;
    }
}
```

設計上の要点：
- **RefCount は await の前に加算する**（await 中に他から解放されても自分の分は保持される。キャンセル / 失敗時は catch で巻き戻す）
- **明示解放と自動回収は同じ経路（`ReleaseInternal`）を通す**。明示解放済みの分は `CountByScope` から減算済みのため、寿命回収時に二重解放は起きない。回収されるのは「解放し忘れた分」のみ
- **解放は、チケットの記録（`IssuedTicketRecord`）が持つエントリへの参照を起点にする**。ロード失敗でエントリが Registry から削除された後に、同じキーで新しいエントリができても、巻き戻しが新しいエントリの RefCount を誤って減らさないため。取り除くのは、Registry にあるエントリが記録のものと同じ場合だけ（`RemoveEntryIfSame`）。`Release<T>`・`ReleaseBySerial`・`ReleaseLifetime`・`Release(AssetGroupTicket)` のすべてがこの形にそろう
- **チケットは通し番号で一意にする**。同じ key・同じ寿命で2回 Load するとチケットの中身（key と scope）が同じになり、同じチケットを2回 `Release` すると別のロード分まで解放されてしまうため。`Release` は発行済みの通し番号を取り除けたときだけ `ReleaseInternal` に進む
- **発行済みチケットは `IssuedTicketRegistryModel` 内で寿命ごとにも索引する**。`ReleaseLifetime` は key ではなくスコープを起点に「残っているチケット」を探す必要があるため（シリアルをキーにした Dictionary だけでは全件走査になる）
- **判定キーは `(AssetKey, 型)`（`AssetEntryKey`）**。同じキーでも型が違えば別のエントリになり、結果を共有しない（キャストの失敗や、意図しない型のアセットが返ることは起きない）。`AssetKey` 自体は型を含めない（7.1）。単体の `LoadAsync` で型を取り違えても例外にならない点は、型が混ざった Label を扱うために受け入れた（13章。設計仕様書5.3・6章）
- **リーク警告は、呼び出し元にチケットを返し終えた分だけに出す**。ロード中に寿命が終わった分は、呼び出し元がまだチケットを受け取っておらず、`Release` を呼びようがない（`OperationCanceledException` が届くだけ）。これを「Release の呼び忘れ」と警告すると誤検知になるため、`IssuedTicketRecord.AccIsDelivered` で区別する（3.2・10章）
- **`ReleaseLifetime` は、チケットが無くても最後に必ず `UnwatchOwnerIfNeeded` を呼ぶ**。確保の途中で例外が出て、チケットの無いまま Owner の監視だけが残っても、Owner の破棄（Watcher・ポーリング）で外れて、以後の走査の対象に残らない（5.4）
- **ロード失敗時はエントリを即座に削除する**。待機中の呼び出し元は全員同じ `AssetLoadException` を受け取り、次の Load は新しく実ロードを始める（失敗したタスクを使い回して再試行できなくなるのを防ぐ）
- **ロード中に RefCount が 0 になった場合**（呼び出し元が全員キャンセルした等）、エントリはすぐ削除するが、Worker はロード完了を待ってから `Addressables.Release` する。完了前の handle は解放しない
- **Dispose 後は新規の Load を受け付けない**（`ObjectDisposedException`）。Dispose 後の `Release` と Watcher からの通知は何もしない
- メインスレッド前提であればロック不要（Addressables の継続もメインスレッド前提のため）
- `Instantiate` 系の API は提供しない。`LoadAsync<GameObject>` は Prefab アセット本体を返し、インスタンス化は実装者の責務とする（4章参照）
- 一括回収時の一時退避に `ToArray()` を使わない（GC アロケーション対策。8章参照）。`IssuedTicketRegistryModel`／`OwnerWatchRegistryModel` は自身が使う再利用バッファを内部に持ち、スナップショットを返す

### 3.2 `IssuedTicketRecord`

`IssuedTicketRegistryModel` の値。発行済みチケット1件分の記録。

```csharp
internal readonly struct IssuedTicketRecord
{
    internal AssetKey AccKey { get; }
    internal AssetEntryKey AccEntryKey { get; }  // Registry の判定キー（AssetKey と型の組）
    internal AssetEntryModel AccEntry { get; }   // 解放時に起点にするエントリ。Registry を引き直さない（3.1）
    internal LifetimeScopeId AccScopeId { get; }
    internal AssetGroupTicket AccGroup { get; }  // Label 一括ロード由来のときだけ。単体の Load では null（13章）
    internal bool AccIsDelivered { get; }        // 呼び出し元にチケット（結果）を返し終えたか。発行時は false。リーク警告は true の分だけに出す（10章）
    internal IssuedTicketRecord AsDelivered();   // 印だけを立てた複製を返す（readonly struct のため。IssuedTicketRegistryModel.MarkDelivered が差し替える）
#if UNITY_EDITOR
    internal string AccStackTrace { get; } // 確保時のスタックトレース。リーク警告用（7章参照）
#endif
}
```

## 4. 戻り値のルール（Instantiate 系を提供しない）

`LoadAsync<T>` は常に「読み込んだアセットそのもの」を返す。型によって戻り値の意味が変わることはない。

- `LoadAsync<Sprite>` → Sprite アセット
- `LoadAsync<GameObject>` → **Prefab アセット本体**（シーン上に生成されたインスタンスではない）

`Addressables.InstantiateAsync` に相当する API（`InstantiateAsync` / `ReleaseInstance`）は Manager では提供しない。インスタンス化は実装者が `Object.Instantiate` で行い、そのインスタンスの破棄も実装者が `Object.Destroy` で行う。

### 採用理由

- 戻り値のルールを一貫させるため。「Load したら読み込んだものが返る」という単一のルールで説明でき、実装者が型ごとの挙動差（アセットが返るのかインスタンスが返るのか）を覚える必要がなくなる
- Manager の責務が「アセットのロードと解放」だけに絞られ、参照カウントも `AssetKey` 単位で一元管理できる。実体を共有する Load 系と、呼ぶたびに別物になる Instantiate 系という性質の異なる2系統を抱え込まずに済む

### 実装者が注意すべき点（Manager 管理外の領域）

`Object.Instantiate` で生成したインスタンスは Manager の管理対象外であり、寿命による自動回収も効かない。

- `Release` してもインスタンスは残る：Prefab アセットを解放しても、生成済みのインスタンスは破棄されない。インスタンスは別途 `Object.Destroy` する必要がある
- インスタンスが生きているうちに `Release` してはいけない：Prefab アセットが解放されると、そのアセットに依存するリソース（マテリアル、テクスチャ等）が失われ、生成済みインスタンスの表示が壊れる可能性がある
- 実務上は、インスタンスを保持する GameObject を owner に指定して Load するか、インスタンスと同じ寿命（Scene 等）を指定するのが安全

## 5. Lifetime（寿命指定と自動回収）

### 5.1 方針

- 読み込み・解放は明示的に行うのが主経路（`LoadAsync` → `Release(ticket)`）
- Load 時に寿命を指定し、その寿命の終了時点で解放されていないものは自動的に回収される
- 自動回収はあくまでセーフティネットであり、常用させない（7章のリーク警告で検出する）

### 5.2 寿命の種類

`Manual / Scene / Owner` の3種とする。`Phase`（明示フェーズの終了で回収）は `Scene` や `Owner` と違い回収タイミングを Unity が保証してくれず、実質的に手動管理と変わらないため、独立した種類とはせず `Manual` に統合する。

```csharp
public enum LifetimeKind
{
    Manual,   // 自動回収なし。永続アセット・フェーズ単位の管理などはこちら。Manager.Dispose時のみ強制解放
    Scene,    // Load時のシーンのUnloadで回収
    Owner,    // 指定したGameObjectの破棄で回収
}

public readonly struct AssetLifetime
{
    public static AssetLifetime Manual { get; }
    public static AssetLifetime Scene();            // 呼び出し時点のアクティブシーン
    public static AssetLifetime Scene(Scene scene);
    public static AssetLifetime Owner(GameObject go);
    public static AssetLifetime Owner(Component component); // 内部で component.gameObject に解決
}
```

各寿命は内部で `LifetimeScopeId` に解決され、`AssetEntryModel.CountByScope` に紐づく。スコープ終了時に `LoadingController.ReleaseLifetime(scopeId)` が呼ばれる（`Manual` は自動発火するトリガーがないため、`Manager.Dispose` 時の強制解放のみが対象）。

### 5.3 寿命の解決時の扱い

| 状況 | 扱い |
|---|---|
| Load 時点で Owner がすでに破棄されている | `ArgumentException` |
| ロード中に Owner が破棄された | await の後で回収済みを検出し、確保を巻き戻して `OperationCanceledException`（3.1参照） |
| 未ロードのシーンを指定した | `ArgumentException` |
| `DontDestroyOnLoad` シーンを指定した | アンロードされないため、警告を出したうえで実質 `Manual` として扱う |

Scene 寿命の終了は、Manager が `SceneManager.sceneUnloaded` を中継し、`LoadingController.HandleSceneUnloaded(scene)` で受け取る（2章参照）。Controller は `SceneManager` を直接購読しない。

**`Addressables.LoadSceneAsync` で読み込んだシーンにも対応する**：`AssetLifetime.Scene(Scene scene)` はロード方法を問わず `Scene` 値を受け取るだけであり、検知も `SceneManager.sceneUnloaded` の1経路に一本化されている。Addressables でロードしたシーンも `Addressables.UnloadSceneAsync` で解放すれば内部的に Unity の通常のシーン管理を経由するため、同じイベントで検知できる。利用者は `Addressables.LoadSceneAsync` の戻り値 `SceneInstance.Scene` を `AssetLifetime.Scene(...)` に渡せばよい。

### 5.4 Owner（GameObject 単位）の破棄検知

- owner に指定できるのは `UnityEngine.Object`（GameObject / Component）のみとし、型で強制する
- 検知は内部専用の監視 Component `AssetLifetimeWatcher` を `AddComponent` して行う。インターフェースを外部公開せず、実装者は何も継承・実装しない

```csharp
[DisallowMultipleComponent]
[AddComponentMenu("")] // メニューから誤って追加できないようにする
internal sealed class AssetLifetimeWatcher : MonoBehaviour
{
    private readonly List<LifetimeScopeId> _scopes = new();
    private Action<LifetimeScopeId> _onDestroyed;

    internal void Register(LifetimeScopeId scope, Action<LifetimeScopeId> onDestroyed)
    {
        _scopes.Add(scope);
        _onDestroyed = onDestroyed; // Controller がフィールドにキャッシュした delegate（8章。Editor では HandleOwnerDestroyed。5.7）
    }

    internal void Unregister(LifetimeScopeId scope) => _scopes.Remove(scope);

    /// <summary> Controller の破棄時に呼ぶ。破棄済みの Controller を呼ばないようにする </summary>
    internal void Unbind() => _onDestroyed = null;

    private void OnDestroy()
    {
        // 通知（ReleaseLifetime）の中から Unregister されても壊れないよう、1件ずつ先に取り除いてから通知する。
        // 列挙しない・ToArray() を使わないので、配列の確保も無い（11章）
        while (_scopes.Count > 0)
        {
            var last = _scopes.Count - 1;
            var scope = _scopes[last];
            _scopes.RemoveAt(last);
            _onDestroyed?.Invoke(scope);
        }
    }
}

// Lifetime層：Controller は AddComponent を直接呼ばず、この interface 経由で Watcher を扱う（テストで差し替えるため）
internal interface IAssetLifetimeWatcherBinder
{
    void Bind(GameObject owner, LifetimeScopeId scope, Action<LifetimeScopeId> onDestroyed);
    void Unbind(GameObject owner, LifetimeScopeId scope);
    void UnbindAll(); // Manager の破棄時に、貼ったすべての Watcher を Unbind する
}

// 実装側：同一GameObjectへの複数Loadでも Watcher は1つに集約する
private AssetLifetimeWatcher GetOrAddWatcher(GameObject go)
    => go.TryGetComponent<AssetLifetimeWatcher>(out var w) ? w : go.AddComponent<AssetLifetimeWatcher>();

// Bind 中のスコープと Watcher の対応。Owner 寿命は1 GameObject につき1スコープなので、スコープが鍵になる
private readonly Dictionary<LifetimeScopeId, AssetLifetimeWatcher> _boundWatchers = new();
// Bind：Watcher.Register の後に辞書へ登録する
// Unbind：owner が破棄済みでも、先に辞書から取り除く（その後、owner が生きていれば Watcher.Unregister）
// UnbindAll：辞書を具体型のまま foreach し、破棄済みの Watcher は飛ばして Watcher.Unbind を呼ぶ
```

**インターフェース公開を採用しない理由**：
- 実装者が `IAssetLifetimeOwner` 等を実装する形にすると、実装漏れ・イベント発火漏れが起こりうる。これは `Release` の書き忘れと失敗モードが同じであり、セーフティネットとして機能しない
- `OnDestroy` は Unity 側が呼び出しを保証するため、安全網たりうる

**Watcher への登録解除のタイミング**：owner がまだ生きたまま、そのスコープのチケットがすべて明示 `Release` された場合は、`OwnerWatchRegistryModel`（7.3参照）と Watcher 側の登録（`Unregister`）の両方から外す（`IssuedTicketRegistryModel` が発行済みチケットの索引でスコープごとのチケット数を数えており、0 になった瞬間に判定する）。owner の破棄やポーリングで回収された場合は、その時点で owner はすでに破棄済み（または破棄中）なので、`IAssetLifetimeWatcherBinder.Unbind` は Watcher 側の登録解除を行わない（Binder の `_boundWatchers` の記録だけは、owner が破棄済みでも必ず取り除く。Owner の生成と破棄を繰り返しても、破棄済みの Watcher の記録が溜まり続けないようにするため）。

`ReleaseLifetime` は、チケットが1件も無くても最後に必ず `UnwatchOwnerIfNeeded` を呼ぶ。確保の途中で例外が出て、`ResolveScope` の後・チケット登録の前に失敗した場合に、チケットの無い Owner の監視が残っても、Owner の破棄（Watcher の `OnDestroy`・ポーリング）の回収で外れ、以後の走査の対象に残らないようにするため（3.1）。

### 5.5 ピュア C# クラスの扱い

ピュア C# クラスは破棄検知の確実な手段がない（GC 依存で回収タイミングが不定）ため、owner 指定の対象外とする。代替：
- `Scene` 寿命を使う
- 関連する View（MonoBehaviour）の GameObject を owner に指定する
- `IDisposable` を実装し `Dispose` 内で `Release(ticket)` を呼ぶ

### 5.6 多重の安全網

1. **Watcher の `OnDestroy`**（即時・主経路）
2. **低頻度ポーリング**：登録済み owner を定期走査し、偽 null になったものを回収（Watcher の取りこぼし対策）
3. **`Manager.Dispose` 時の全 Entry 強制解放**（アプリ終了・全体リセット時の最終防衛線）

### 5.7 ポーリング設定

- 既定値：3秒間隔
- 間隔は `AddressableLoadingSettings`（Runtime SO が保持。9章）で変更可能。ポーリング自体を無効化する設定も持たせる
- 経過時間は `Time.unscaledDeltaTime` で進める（Manager が `LateUpdate` で渡す。2章）。`timeScale = 0`（ポーズ中など）でも、解放の遅れを許容できる実時間で回収が進む。ポーリングは Watcher の取りこぼしの保険であり、ゲーム内の時間に従う理由が無い

ポーリング対象owner・経過時間はどちらも `OwnerWatchRegistryModel`（7.3参照）が状態データとして保持する。`LoadingController` はモデルへの委譲のみ行う。

**回収経路の記録（Editor 限定）**：Owner の回収は、Watcher の `OnDestroy` の通知とポーリングの2つの入口から `ReleaseLifetime` に入る。Editor のデバッグウィンドウ（「Owner」タブ）で、どちらの入口で回収されたかを区別できるよう、入口で `ReleaseLifetime` の直前に `OwnerReclaimRegistryModel` へ記録する（`#if UNITY_EDITOR`）。Watcher に渡す delegate は、Editor では `HandleOwnerDestroyed`（記録 → `ReleaseLifetime`）で、本番ビルドでは従来どおり `ReleaseLifetime` そのもの。解放の経路（`ReleaseLifetime` → `ReleaseInternal`）は1つのまま変えない。詳細は `specs/editor-debug-window-spec.md` 8.3。

```csharp
internal sealed class LoadingController
{
    private readonly AddressableLoadingSettings _settings;
    private readonly OwnerWatchRegistryModel _ownerWatch; // Managerから注入

    /// <summary> ManagerのLateUpdateイベントから毎フレーム呼ばれる。Unity非依存のためテストからも駆動可能 </summary>
    internal void Tick(float delta_time)
    {
        if (!_settings.AccEnableOwnerPolling) return;
        if (_ownerWatch.Count == 0) return; // owner寿命の登録が無い間は何もしない（8章参照）
        if (!_ownerWatch.AdvanceElapsed(delta_time, _settings.AccOwnerPollingIntervalSec)) return;

        PollDestroyedOwners(); // 登録済みownerを走査し、偽nullのものをReleaseLifetimeで回収
    }

    private void PollDestroyedOwners()
    {
        foreach (var scope_id in _ownerWatch.CollectDestroyedScopes())
        {
#if UNITY_EDITOR
            RecordOwnerReclaim(OwnerReclaimRoute.Polling, scope_id); // 回収経路の記録（解放の前。Editor 限定）
#endif
            ReleaseLifetime(scope_id);
        }
    }
}
```

## 6. Service（実ロード担当）

別プロジェクトで作成済みの `AddressableLoaderBase<T>` を移植・改修してベースクラスとし、それを継承した `AssetLoaderService<T>` が実ロードを担う。リトライ、ロギング、キャンセル処理、handle 解放を担当。dedup や RefCount は関知しない。

- Controller から呼べるよう、非ジェネリックの共通インターフェース `IAssetLoaderWorker` を噛ませる
- **Service は `AssetKey` を受け取るが、中身の `AccValue`（string）しか使わない**。`AssetReference` は `AssetKey.FromReference` の時点で `RuntimeKey` の文字列（サブアセットなら `guid[名前]`）に変換されるため、Service は入口（Path / Reference）の違いを一切知らない。`AssetKey` を丸ごと渡すのは、リトライを使い切ったときの `AssetLoadException` に正しい種別のキーを持たせるため。ロードは `Addressables.LoadAssetAsync<T>(string)` のみを使う（`AssetReference.LoadAssetAsync` は handle を参照側に抱え込み、2回目の呼び出しでエラーになるため使わない）
- Worker の生成は `IAssetLoaderServiceFactory` 経由にする。EditMode テストで偽の Worker に差し替えるため
- リトライ回数・待機時間は `AddressableLoadingSettings`（Runtime SO が保持。9章）で設定する。リトライの待機は `AsyncCompat.Delay` で、ポーズ中（`timeScale = 0`）も実時間で待つ（6.2）。リトライを使い切ったら `AssetLoadException`（`AssetKey` を持ち、最後の例外を `InnerException` に入れる）を投げる
- **回復の見込みがない失敗は、リトライせずに即座に `AssetLoadException` にする**。例外（とその `InnerException`）に `InvalidKeyException`（アドレスの書き間違い）を含む場合が対象で、待ってもやり直しても結果が変わらず、既定の設定（3回・1000ms 間隔）では、エラーが出るまで約2秒待たされるため。それ以外の例外（ネットワーク等の一時的な失敗を含む）は、従来どおりリトライする
- **ロード中に `Dispose` された Worker は、完了後に自分で handle を解放して、`OperationCanceledException` で抜ける**。`Dispose()` は、handle の完了待ちの間（`_isLoading`）は handle を解放せず、破棄済みの印（`_isDisposed`）だけを付ける。`LoadAsync` は、各試行の開始時・await の直後・リトライ待機の直前で、破棄済みなら `OperationCanceledException` を投げる。try の中で投げた分は、既存のキャンセル経路（`catch (OperationCanceledException)` → `ReleaseHandle`）が handle を解放する。ここで即座に解放すると、再開した `LoadAsync` が失敗とみなしてリトライし、新しい handle を作って、誰も解放しないままリークする（Manager の破棄で、ロード中の Worker が `Dispose` される場合）。キャンセル扱いにするのは、共有ロードタスクを Faulted にせず、未観測の例外を出さないため。2回目以降の `Dispose` は何もしない

```csharp
internal interface IAssetLoaderWorker : IDisposable
{
#if ADDRESSABLE_UNITASK_SUPPORT
    UniTask LoadAsync(CancellationToken token);
#else
    Task LoadAsync(CancellationToken token);
#endif
    UnityEngine.Object AccAsset { get; }
    Type AccAssetType { get; }
    AsyncOperationStatus AccStatus { get; }
}

internal interface IAssetLoaderServiceFactory
{
    IAssetLoaderWorker Create<T>(AssetKey key) where T : UnityEngine.Object;
    IAssetLoaderWorker Create(AssetKey key, Type type); // 実行時にしかわからない型から生成する（Label 一括ロード用。13.5）
}
```

### 6.1 移植元からの改修内容

| 移植元の状態 | 改修後 |
|---|---|
| UniTask を直接 `using` している | `#if ADDRESSABLE_UNITASK_SUPPORT` で出し分ける（6.2参照） |
| 外部の `AppLogger` / `FeatureKind` に依存 | 内部用の薄い `AddressableLogger`（`Debug.Log` を包むだけ）に置き換える。ログレベルは `AddressableRuntimeSettingsSO` で設定する |
| `IDestroyable` / `OnManagedDestroy` / `_isRequestingDestroy` を持つ | 削除する（寿命管理は Controller が担う） |
| `AssetReferenceT<T>` 1件を受け取る | string キーを受け取る |
| 「既に読み込まれています」で早期リターンする | 削除する（重複判定は Controller が担う） |
| 失敗時に例外経路へ回った handle、キャンセル時の handle が解放されない | どの経路でも handle を解放する |
| パスが null のとき何も返さず終わる／リトライ回数が 0 以下だと1回もロードせず正常終了する | どちらも例外にする |
| リトライを使い切ると `TimeoutException` | `AssetLoadException` にする（時間切れ以外の失敗も Timeout 扱いになるのを避ける） |

複数アセットを扱う `AddressableMultiLoaderBase<T>` は移植しない（重複判定と RefCount 管理が Controller と役割が重なるため）。Label 一括ロードは、Controller が1件ずつの Load を組み合わせて行う（13章）。

### 6.2 非同期実装の切り替え（UniTask が正、無ければ Task）

方針と理由は設計仕様書 3.5.1 を正とする。ここでは Loading での実装に関わる点だけを書く。

- **正（UniTask 導入時）**：コンパイルシンボル `ADDRESSABLE_UNITASK_SUPPORT` が定義され、`UniTask` / `UniTask<T>` を使う
- **UniTask が無い場合の代替**：`System.Threading.Tasks.Task` / `Task<T>`
- 切り替えは `#if` によるシグネチャの出し分けで行う（`#if` 側に UniTask 版、`#else` 側に Task 版）。呼び出し側は `await` で使う限り違いを意識しない
- **共有ロードタスク（`AssetEntryModel.AccLoadTask`）は、安全性を優先して UniTask 導入時も `Task` で持つ**。重複ロードの呼び出し元・Label 一括ロード・Worker の後始末が同時に待ち、Entry の存続中は完了後にも後から来た Load が待つため。`Task` は何度 `await` しても安全だが、`async UniTask` の戻り値は2回目の `await` で例外になるか、プールで再利用された別の処理の状態を読む恐れがある
- UniTask にするのは、1回だけ `await` される経路（公開 API の戻り値、`LoadingController.LoadAsync` / `LoadByLabelAsync`、Worker の `LoadAsync`、`LabelLocationService.ResolveAsync`）である

| 区分 | UniTask 導入時 | UniTask が無い場合 |
|---|---|---|
| 公開 API・Controller の `LoadAsync` / `LoadByLabelAsync` | `UniTask<T>` | `Task<T>` |
| Worker の `LoadAsync`・`ILabelLocationService.ResolveAsync` | `UniTask` | `Task` |
| 共有ロードタスク `AccLoadTask`・Label の待機（`WaitAllOrFirstFault`） | `Task` | `Task` |
| リトライの待機（`AsyncCompat.Delay`） | `UniTask.Delay(ignoreTimeScale: true)` | `Task.Delay` |

#### キャンセル付き待機のヘルパー

共有ロードタスクはシンボルに関係なく `Task` のため、**`Task` 版の `WithCancellation` は常にコンパイルする**。UniTask 版は、1回だけ待つ UniTask に使う。`AssetEntryModel.AccLoadTask` は結果を持たない型のため、非ジェネリック版を用意する。

```csharp
internal static class AsyncCompat
{
#if ADDRESSABLE_UNITASK_SUPPORT
    internal static UniTask WithCancellation(this UniTask task, CancellationToken ct)
        => task.AttachExternalCancellation(ct);
    internal static UniTask<T> WithCancellation<T>(this UniTask<T> task, CancellationToken ct)
        => task.AttachExternalCancellation(ct);
    // timeScale = 0（ポーズ中）でも待機が終わるよう、実時間で待つ
    internal static UniTask Delay(int milliseconds, CancellationToken ct)
        => UniTask.Delay(milliseconds, ignoreTimeScale: true, cancellationToken: ct);
#else
    internal static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken ct) { /* 下と同様 */ }
    internal static Task Delay(int milliseconds, CancellationToken ct) => Task.Delay(milliseconds, ct);
#endif

    // 共有ロードタスク用。シンボルに関係なく常にコンパイルする
    internal static async Task WithCancellation(this Task task, CancellationToken ct)
    {
        if (!ct.CanBeCanceled) { await task; return; }
        var tcs = new TaskCompletionSource<bool>();
        using (ct.Register(CANCEL_WAIT, tcs)) // static にキャッシュした delegate に状態を渡す（11章）
        {
            if (task != await Task.WhenAny(task, tcs.Task))
                throw new OperationCanceledException(ct);
        }
        await task; // 失敗時の例外をそのまま伝える
    }
}
```

キャンセルで止まるのは呼び出し元の待機だけであり、実ロードは止めない（他の呼び出し元と共有しているため）。

時間待ちは必ず `AsyncCompat.Delay` を通す。`UniTask.Delay` を直接呼ぶと、既定では `Time.timeScale` の影響を受け、ポーズ中に失敗したロードのリトライ待機が終わらなくなる。

#### AsyncOperationHandle からのブリッジ（Task 版）

`AsyncOperationHandle.Completed` イベントを `TaskCompletionSource<T>` で待ち受けてブリッジする（UniTask 版は `handle.ToUniTask()` を使う）。

```csharp
internal static Task<T> ToTask<T>(this AsyncOperationHandle<T> handle)
{
    var tcs = new TaskCompletionSource<T>();
    if (handle.IsDone) { SetResult(tcs, handle); return tcs.Task; }
    handle.Completed += h => SetResult(tcs, h);
    return tcs.Task;
}

// 成功時は結果を、失敗時は OperationException を伝える
private static void SetResult<T>(TaskCompletionSource<T> tcs, AsyncOperationHandle<T> handle)
{
    if (handle.Status == AsyncOperationStatus.Succeeded)
        tcs.TrySetResult(handle.Result);
    else
        tcs.TrySetException(handle.OperationException ?? new Exception("Addressables のロードに失敗しました"));
}
```

## 7. Model

### 7.1 AssetKey

Path 入口と AssetReference 入口を両立させるための複合キー。

- `AssetReference.RuntimeKey`（GUID 文字列）と Path のアドレス文字列は値が一致しないため、単純な文字列比較では同一アセットでも別キー扱いになる
- 完全な実体同一判定（ロケーション解決による突合）はコストが高く割に合わないため、「入口の種別＋値」が一致した場合のみ同一とみなす（同一アセットを Path と Reference 両方で要求した場合、二重ロードされる可能性は残るが許容範囲とする）
- `FromReference` の時点で `RuntimeKey` を文字列にするため、Service にはどちらの入口でも string が渡る（6章）
- `AssetKey` 自体は型を含めない。型は、Registry の判定キー `AssetEntryKey`（`(AssetKey, 型)`）の側で持つ。同じキーでも型が違えば別のエントリになる（3章）

```csharp
public readonly struct AssetKey : IEquatable<AssetKey>
{
    public enum Kind { Path, Reference }
    public Kind AccKind { get; }
    public string AccValue { get; }

    public static AssetKey FromPath(string path) => new(Kind.Path, path);
    public static AssetKey FromReference(AssetReference reference) => new(Kind.Reference, reference.RuntimeKey.ToString());
    // Equals / GetHashCode 実装
}

/// <summary> Registry の判定キー。(AssetKey, 型) の組が一致した場合のみ、同じエントリとみなす </summary>
internal readonly struct AssetEntryKey : IEquatable<AssetEntryKey>
{
    internal AssetKey AccKey { get; }
    internal Type AccAssetType { get; }
    // Equals / GetHashCode 実装
}
```

### 7.2 `AssetTicket<T>`

Load 1回ごとに発行される解放用チケットで、読み込んだアセットも持つ。`key` だけでは「どの寿命枠からの確保を解放するのか」が一意に定まらないため、チケット方式を採用する。

- 通し番号で一意にする。key と寿命スコープだけでは、同じ key・同じ寿命で2回 Load したときに中身が同じチケットが2枚でき、二重解放を防げないため
- 通し番号 0 は無効とし、`default(AssetTicket<T>)` の解放を弾けるようにする

```csharp
public readonly struct AssetTicket<T> where T : UnityEngine.Object
{
    /// <summary> 読み込んだアセットのキー </summary>
    public AssetKey AccKey { get; }
    /// <summary> 読み込んだアセット本体 </summary>
    public T AccAsset { get; }
    internal LifetimeScopeId AccScopeId { get; }
    internal long AccSerial { get; } // 0 は無効（default のチケット）
}
```

Label 一括ロードで発行するまとめチケット `AssetGroupTicket` は 13.3 を参照。

### 7.3 Model一覧（AssetEntryModel / AssetRegistryModel / IssuedTicketRegistryModel / OwnerWatchRegistryModel / LeakRegistryModel / OwnerReclaimRegistryModel）

Model の役割は「一時データ・状態データ・SO から取得したデータの保持」である。`LoadingController` はこれらへの委譲と手順の組み立てに徹し、状態そのものは持たない。

- `AssetEntryModel`：1件分の状態（Worker, LoadTask, RefCount, Status, CountByScope, AssetType）
  - `CountByScope`：寿命スコープごとの確保数。寿命回収時に「解放し忘れた分」だけを回収するために必要
  - `AssetType`：このエントリがロードした型。判定キー `AssetEntryKey` の一部（7.1）
- `AssetRegistryModel`：Entry の集合（Dictionary のラッパー）。Editor 拡張が読み取り専用で参照する先にもなる。判定キーは `AssetEntryKey`
  - `RemoveEntryIfSame(entry_key, entry)`：Registry にあるエントリが引数と同じ場合だけ取り除き、取り除けたら `true` を返す。ロード失敗や解放のときに、別のエントリを誤って消さないため
- `IssuedTicketRegistryModel`：発行済みで未解放のチケット（`IssuedTicketRecord`）と、寿命スコープごとの索引を保持する状態データ。通し番号の発行（`IssueSerial`）もここが担う。record は、解放時に起点にするエントリへの参照と、まとめチケットへの参照（Label 一括ロード由来のときだけ）を持つ（3.2）
- `OwnerWatchRegistryModel`：Owner 寿命のポーリング対象 owner と、ポーリングの経過時間を保持する状態データ。Editor では、登録時の Owner 名（`TryGetOwnerName`）と、デバッグウィンドウ用のコピー用メソッド（`CopyOwnersTo`）も持つ（`#if UNITY_EDITOR`）
- `LeakRegistryModel`：明示 `Release` されずに寿命回収されたチケット（リーク）を、確保元ごとに集約して溜める状態データ。**Editor 限定**（Model 全体が `#if UNITY_EDITOR`）。書き込むのは `LoadingController`（`ReleaseLifetime`）で、Editor の窓口が読む（10章・`specs/editor-debug-window-spec.md` 8.2）
- `OwnerReclaimRegistryModel`：Owner 寿命の回収を、回収経路（`OnDestroy` / `Polling`）と Owner 名ごとに集約して溜める状態データ。**Editor 限定**（Model 全体が `#if UNITY_EDITOR`）。書き込むのは `LoadingController`（Watcher の通知とポーリングの入口）で、Editor の窓口が読む（5.7・`specs/editor-debug-window-spec.md` 8.3）
- public フィールドは使わず、`Acc` 付きのアクセサで公開する（[`conventions.md`](../conventions.md) 1章）

```csharp
internal sealed class AssetEntryModel
{
    private readonly Dictionary<LifetimeScopeId, int> _countByScope = new();

    internal AssetKey AccKey { get; }
    internal AssetEntryKey AccEntryKey { get; } // Registry の判定キー
    internal IAssetLoaderWorker AccWorker { get; }
    internal Type AccAssetType { get; }
    internal Task AccLoadTask { get; private set; } // 複数の呼び出し元が待つため、UniTask導入時も常にTask（6.2参照）
    internal int AccRefCount { get; private set; }
}
```

## 8. Handler（IAddressableHandler）

- 責務は呼び出し関数のみ。状態を持たず、Controller への転送に徹する。**利用者が触れる操作の入口はこの Handler 1つ**（初期化・読み込み・解放、今後はダウンロードも）
- Manager 経由で取得する（`AddressableManager.AccInstance.AccHandler`）。素の static ハンドラーは採用しない（LoadingController は状態を持つため、状態はインスタンスに閉じてリセット可能にしておく必要がある）
- 取得方法は `GetHandler()` のようなメソッドではなく `AccHandler` のようなアクセサにする
- 公開する型は具象クラスでなくインターフェース（`IAddressableHandler`）にする。名前は、対象が「アセット」から「Addressables の操作全体」に広がったため `IAssetHandler` から改めた
- 実装クラス `AddressableHandler` は `Runtime/Core/Handler/`（namespace `MAH.AddressableManager.Core`）に置く。InitializeController と LoadingController の両方を知るため、どちらの機能フォルダにも置かない（機能フォルダ同士の相互参照を避ける）。コンストラクタには両 Controller を受け取り、読み込み・解放は LoadingController へ、初期化は InitializeController へ転送する
- 初期化のメンバー（`InitializeAsync` / `AccInitializeState` / `AccIsInitialized` / `OnInitialized` / `OnInitializeFailed`）の仕様は `initialize-spec.md` 4章。イベントは Handler にフィールドを持たせず、アクセサで Controller のイベントへ直接つなぐ（同 7.7）

```csharp
public interface IAddressableHandler
{
    // 初期化のメンバーは initialize-spec.md 4章。ここでは読み込み・解放のメンバーを示す
    // 非同期型(Task/UniTask)の切り替え方針は6.2参照
    Task<AssetTicket<T>> LoadAsync<T>(AssetKey key, AssetLifetime lifetime, CancellationToken ct = default)
        where T : UnityEngine.Object;
    void Release<T>(AssetTicket<T> ticket) where T : UnityEngine.Object;

    // Label 一括ロード（13章）。Load 時に型は指定しない。型は AssetGroupTicket から取り出すときに指定する
    Task<AssetGroupTicket> LoadByLabelAsync(string label, AssetLifetime lifetime, CancellationToken ct = default);
    void Release(AssetGroupTicket ticket);
}
```

利用例：

```csharp
var ticket = await AddressableManager.AccInstance.AccHandler.LoadAsync<Sprite>(_iconKey, AssetLifetime.Owner(this), token);
_image.sprite = ticket.AccAsset;
// 要らなくなったら
AddressableManager.AccInstance.AccHandler.Release(ticket);
```

Label 一括ロードの利用例は 13.8 を参照。

## 9. 設定（AddressableRuntimeSettingsSO / AddressableLoadingSettings）

設定 SO は機能ごとに分けず、実行時用の `AddressableRuntimeSettingsSO` に集約する（全体方針は `addressable-manager-design.md` 3.1.1）。Loading 機能の設定は SO ではない `[Serializable]` クラス `AddressableLoadingSettings` として Runtime SO が保持する。

```csharp
// Runtime/Core/AddressableRuntimeSettingsSO.cs（namespace MAH.AddressableManager.Core）
[CreateAssetMenu(menuName = "MAH/Addressable/RuntimeSettings", fileName = "AddressableRuntimeSettings")]
internal sealed class AddressableRuntimeSettingsSO : ScriptableObject
{
    [Header("ログ設定")]
    [SerializeField] private AddressableLogLevel _logLevel = AddressableLogLevel.Warning;

    [Header("読み込み・解放")]
    [SerializeField] private AddressableLoadingSettings _loading = new();

    internal AddressableLogLevel AccLogLevel => _logLevel;
    internal AddressableLoadingSettings AccLoading => _loading;

    private void OnValidate()
    {
        _loading ??= new AddressableLoadingSettings();
        _loading.ClampValues();
    }
}

// Runtime/Core/Loading/Data/AddressableLoadingSettings.cs（namespace MAH.AddressableManager.Core.Loading）
[Serializable]
internal sealed class AddressableLoadingSettings
{
    [SerializeField] private bool _enableOwnerPolling = true;
    [SerializeField] private float _ownerPollingIntervalSec = 3f;
    [SerializeField] private int _maxRetry = 3;
    [SerializeField] private int _retryDelayMs = 1000;

    internal bool AccEnableOwnerPolling => _enableOwnerPolling;
    internal float AccOwnerPollingIntervalSec => _ownerPollingIntervalSec;
    internal int AccMaxRetry => _maxRetry;
    internal int AccRetryDelayMs => _retryDelayMs;

    public AddressableLoadingSettings() { }  // Unity のシリアライズ用（既定値）
    internal AddressableLoadingSettings(bool enable_owner_polling, float owner_polling_interval_sec, int max_retry, int retry_delay_ms) { ... } // テスト用

    /// <summary> 設定値を有効な範囲に補正する（Runtime SO の OnValidate から呼ばれる） </summary>
    internal void ClampValues() { ... } // ポーリング間隔 0.1秒以上・リトライ1回以上・待機0ミリ秒以上
}
```

- `LoadingController` と `AssetLoaderServiceFactory` には `AddressableLoadingSettings` だけを渡す。EditMode テストではコンストラクタで値を指定して `new` するだけで済む
- Runtime SO は Loading 機能の型を参照するが、これは機能の設定を束ねる合成ルート（Manager 層）だからであり、機能同士の相互参照には当たらない。`AddressableLoadingSettings` 側は Runtime SO を知らない

**ポーリング間隔の決定根拠**：この値は CPU 負荷ではなく「解放が遅延して許容できるメモリ保持時間」で決める。走査コストは owner が200件程度あっても偽 null 判定200回＝数マイクロ秒で、1秒間隔にしても実測差は現れない（8章参照）。3秒は「最大3秒間メモリに残っても支障がない」という許容範囲として設定したものであり、負荷対策として延ばす意味は薄い。

**PlayerLoop 挿入を採用しない理由**：PlayerLoop に挿入する場合も、結局はイベント購読で Controller を駆動する形になり、Controller 側の実装（`Tick`）は変わらない。本モジュールでイベント駆動する処理は3秒間隔のポーリング1件のみであり、実行順の精度を必要としないため、PlayerLoop 側のコスト（`PlayerLoopSystem` の組み替え、ドメインリロード対応）に見合わない。

## 10. リーク警告（エディタ限定）

自動回収が働けばリークは起きないが、回収に依存した実装の存在自体は検出して是正すべきであるため、黙って回収せず警告を出す。

- 寿命回収時に「明示 `Release` されずに回収された ticket」を、確保時のスタックトレース付きで記録し、`AddressableLogger` で警告を出す（`IssuedTicketRecord`。3.2参照）
- **警告の対象は、呼び出し元にチケットを返し終えた分（`AccIsDelivered`）だけ**。ロード中（返す前）に寿命が終わった分は、呼び出し元が `Release` を呼びようがなく、`OperationCanceledException` が届くだけなので、リークではない。警告せず、`Log`（Verbose）に留める。印は、`LoadAsync` が結果を返す直前に `MarkDelivered` で立てる（`LoadByLabelAsync` は `Fill` の後に全通し番号へ立てる）
- スタックトレースは `IssuedTicketRecord` のコンストラクタで `Environment.StackTrace` を使って記録する（発行のたびに記録するため、リーク調査目的以外でのコストが気になる場合は将来 `Detailed` 計測レベルとの連動を検討する。7章参照）
- `#if UNITY_EDITOR` で囲み、本番ビルドではコストをかけない
- リーク警告は、ログに出すだけでなく、確保元ごとに集約して Editor 限定の `LeakRegistryModel` に蓄積し、デバッグウィンドウの「リーク」タブで一覧する（`ReleaseLifetime` の `LogWarning(CreateLeakMessage(...))` の隣で記録する。判定は同じ。`specs/editor-debug-window-spec.md` 8.2）。Registry と発行済みチケット（スタックトレース付き）の一覧は、「実行時状態」タブで表示できる（同 8.1）

## 11. GC アロケーション対策

- `ReleaseLifetime` の一時退避には `_releaseTargetBuffer`（`List<long>`）を使い、`ToArray()` / `ToList()` は使わない。フィールドに保持して `Clear()` で使い回す
- Watcher へのコールバックはフィールドにキャッシュした delegate（`_onOwnerDestroyed`）を使い、ラムダを毎回生成しない
- `AssetLifetimeWatcher.OnDestroy` は、`ToArray()` を使わず、`_scopes` を末尾から1件ずつ取り除きながら通知する（大量の GameObject の同時破棄で、件数分の配列が確保されるのを避ける）
- `AsyncCompat.WithCancellation` の `CancellationToken.Register` は、`static` にキャッシュした delegate（`CANCEL_WAIT`）に状態（`TaskCompletionSource`）を引数で渡し、クロージャを毎回生成しない
- `Dictionary` は具体型のまま `foreach` する（`IEnumerable` にアップキャストするとボックス化する）
- `Tick` の先頭で owner 登録数が0件なら即リターンする（owner 寿命を使わないプロジェクトでは実質何も走らない）
- Label 一括ロードのロケーションのバッファ（`List<LabelLocation>`）は、`await` をまたぐため、上記の使い回しバッファとは違い**呼び出しごとに確保する**。フィールドで使い回すと、並行する `LoadByLabelAsync` の間で中身が混ざる（13.9）
- `AssetGroupTicket.GetAssets<T>()` は型ごとの結果をキャッシュし、2回目以降は確保しない。`IReadOnlyList<T>` の `foreach` は列挙子がボックス化されるため、`for` と `Count`／インデクサで回す（13.3）
- `AddressableLogger.Log` / `LogWarning` / `LogException` には `[Conditional("UNITY_ASSERTIONS")]` を付け、リリースビルドでは呼び出し自体をコンパイル時に取り除く（文字列生成の GC アロケーションごと消える）。`UNITY_ASSERTIONS` は Editor・開発ビルドでのみ定義される Unity 標準シンボルで、リリース（Master）ビルドでは外れる。`LogError` だけは致命的な失敗をリリースでも追えるよう Conditional を付けない

## 12. テスト方針

- **EditMode**（`Tests/Editor/`）：`IAssetLoaderServiceFactory`・`IAssetLifetimeWatcherBinder`・`ILabelLocationService`・`IInitializationGate` を偽の実装に差し替え、`LoadingController` を単体テストする（Label 一括ロードを含む）。偽のゲート（`FakeInitializationGate`）は既定で常に開いており、初期化の有無に関係なく Loading だけを動かせる。ゲートが閉じているときの待機は `LoadingControllerInitializationGateTests` で確認する
- **PlayMode**（`Tests/Runtime/`、Editor 上で実行）：実際の Addressables を使い、読み込み・明示解放・Scene / Owner / Manager 破棄による自動回収を確認する
- 詳細な検証項目は実装計画書を参照（本仕様書には テスト項目の一覧までは持たない）

## 13. Label 一括ロード

Label を指定して、その Label が付いたアセットをまとめて読み込み、1回の呼び出しでまとめて解放する。**Label にどの型のアセットが入っているかを、利用者が把握していなくても使える**ことを目的とする（中〜大規模のプロジェクトでは、Label ごとの型を把握しきれないため）。

### 13.1 API

`IAddressableHandler`（8章）に次の2つを追加する。

- `LoadByLabelAsync(string label, AssetLifetime lifetime, CancellationToken ct = default)`：`AssetGroupTicket`（まとめチケット）を返す。**Load のときに型は指定しない**
- `Release(AssetGroupTicket ticket)`：まとめチケットの中身をすべて解放する

型は、まとめチケットから取り出すときに指定する（13.3）。

### 13.2 `LoadByLabelAsync` の手順

1. 破棄済みなら `ObjectDisposedException`。`label` が null または空なら `ArgumentException`
   - （2026-10-02 追加）初期化ゲートが閉じていれば、ここで初期化の完了を待ち、待った後にもう一度破棄済みを確認する（3.1 の手順2の補足と同じ。`initialize-spec.md` 5章）
2. `ILabelLocationService` で Label のロケーションを解決する（13.4）。結果は (PrimaryKey, ResourceType) の組の一覧で、呼び出しごとに確保した `List` に詰める（13.9）。ここで `await` する。**寿命の解決（3.1の手順3）は、この後に行う**。`ResolveScope` は Owner の Watcher を登録するため、`await` を挟んだままにすると、チケットが登録されないまま Watcher だけが残るおそれがあるため
3. `await` の後で、もう一度破棄済みを確認する（`ObjectDisposedException`）。組が0件なら、警告ログを出し、空の `AssetGroupTicket` を返す
4. 寿命を `LifetimeScopeId` に解決する
5. **確保（同期）**：全件について、3.1の手順4〜5（エントリの取得または作成・RefCount の加算・チケットの登録）を行う。ここまで `await` を挟まないため、全件の通し番号がこの時点でそろう。record には、まとめチケットへの参照を持たせる
6. **待機**：全件の `AccLoadTask` を、最初の失敗で打ち切る形で待つ（下記）。`AsyncCompat.WithCancellation` で ct を付ける
7. 失敗・キャンセル・寿命の終了のいずれかが起きたら、全件の通し番号を巻き戻して（`ReleaseBySerial`）例外を投げる（13.10）。残りのロードは待たない。ロード中のエントリは、RefCount が 0 になると Registry から外れ、Worker はロード完了後に `DisposeWorkerWhenCompleted` が解放する
8. 全件が成功したら、まとめチケットに中身（アドレス・型・アセット・通し番号）を詰め、全通し番号に受け渡し済みの印を立てて（`MarkDelivered`。`await` を挟まない）返す

「確保」と「待機」を分けるのは、失敗の時点で全件の通し番号がわかっていて、残りを待たずにすぐ巻き戻せるようにするため。共有ロードタスクは常に `Task` なので、待機は `Task` で統一でき、`#if` の出し分けも増えない。

**待機に `Task.WhenAll` は使わない**。`Task.WhenAll` は全件の完了を待つため、1件が失敗しても、最も遅いロードが終わるまで待ち続けてしまう。`AsyncCompat` に、全件の完了、または最初の失敗で完了する `Task` を返すヘルパー（`WaitAllOrFirstFault`）を置く。

```csharp
#if ADDRESSABLE_UNITASK_SUPPORT
internal async UniTask<AssetGroupTicket> LoadByLabelAsync(string label, AssetLifetime lifetime, CancellationToken ct = default)
#else
internal async Task<AssetGroupTicket> LoadByLabelAsync(string label, AssetLifetime lifetime, CancellationToken ct = default)
#endif
{
    ThrowIfDisposed();
    if (string.IsNullOrEmpty(label))
        throw new ArgumentException("Label が null または空文字です。", nameof(label));

    if (!_initializationGate.AccIsOpen)
        await WaitForInitializationGateAsync(lifetime, ct); // ロケーションの解決より前に待つ（3.1 と同じ）

    var locations = new List<LabelLocation>(); // 呼び出しごとに確保する（13.9）
    await _labelLocationService.ResolveAsync(label, locations, ct);
    ThrowIfDisposed(); // await の間に破棄されている可能性がある

    var group = new AssetGroupTicket(label, locations.Count); // 件数は生成時に渡す。中身は、待機の後に Fill で詰める
    if (locations.Count == 0)
    {
        AddressableLogger.LogWarning($"Label「{label}」に該当するアセットがありません。");
        return group;
    }

    var scope_id = ResolveScope(lifetime); // 確保の直前に解決する（Watcher の登録とチケット登録の間に await を挟まない）

    // まとめチケットの中身。アドレスと型は確保のときに、アセットは待機の後に詰め、そのまま Fill に渡す（Fill がコピーせず保持する）
    var addresses = new string[locations.Count];
    var asset_types = new Type[locations.Count];
    var entries = new AssetEntryModel[locations.Count]; // 待機の後に、読み込んだアセットを取り出すために持つ
    var serials = new long[locations.Count];
    var tasks = new System.Threading.Tasks.Task[locations.Count]; // 共有ロードタスクは常に Task（UniTask 版では using が無いため完全修飾で書く）
    var reserved = 0; // 確保が済んだ件数。途中で例外が出ても、確保済みの分だけを巻き戻す
    try
    {
        // 確保（同期）：全件の通し番号を、await の前に決める
        for (; reserved < locations.Count; reserved++)
        {
            var key = AssetKey.FromPath(locations[reserved].AccPrimaryKey);
            var entry_key = new AssetEntryKey(key, locations[reserved].AccResourceType);
            if (!_registry.TryGetEntry(entry_key, out var entry))
                entry = AddAndStartEntry(entry_key, _serviceFactory.Create(key, entry_key.AccAssetType)); // 実行時の型から生成する（13.5）
            addresses[reserved] = key.AccValue;
            asset_types[reserved] = entry_key.AccAssetType;
            entries[reserved] = entry;
            serials[reserved] = RegisterTicket(entry, entry_key, scope_id, group);
            tasks[reserved] = entry.AccLoadTask;
        }

        // 待機：最初の失敗で打ち切る（Task.WhenAll は全件の完了を待つため使わない）
        await AsyncCompat.WaitAllOrFirstFault(tasks).WithCancellation(ct);
        if (!ContainsAll(serials, reserved))
            throw new OperationCanceledException("ロード中に寿命が終了しました");
    }
    catch
    {
        var lifetime_ended = !ContainsAll(serials, reserved); // 巻き戻しでチケットが無くなる前に調べる
        for (int i = 0; i < reserved; i++)
            ReleaseBySerial(serials[i]);

        // 例外の優先順位（13.10）
        ct.ThrowIfCancellationRequested();                                              // 1. ct のキャンセル
        if (lifetime_ended)
            throw new OperationCanceledException("ロード中に寿命が終了しました");         // 2. 寿命の終了
        throw;                                                                           // 3. 最初の AssetLoadException
    }

    var assets = new UnityEngine.Object[entries.Length];
    for (int i = 0; i < entries.Length; i++)
        assets[i] = entries[i].AccWorker.AccAsset;
    group.Fill(addresses, asset_types, assets, serials); // 中身（アドレス・型・アセット・通し番号）を詰める
    for (int i = 0; i < serials.Length; i++)
        _tickets.MarkDelivered(serials[i]); // この後は await を挟まず返す。以降に寿命が終わった分は、Release の呼び忘れ（リーク）
    return group;
}

/// <summary> まとめチケットの解放。中身を1件ずつの解放と同じ経路で解放する </summary>
internal void Release(AssetGroupTicket group)
{
    if (_isDisposed) return;
    if (group == null || group.AccIsReleased)
    {
        AddressableLogger.LogWarning($"解放済みまたは無効なまとめチケットです：{group?.AccLabel}");
        return;
    }

    group.Invalidate(); // 先に無効化し、同じまとめチケットの2回目の Release を弾く
    for (int i = 0; i < group.AccCount; i++)
    {
        if (TryRemoveTicket(group.GetSerial(i), out var record))
            ReleaseInternal(record); // 1件ずつの解放と同じ経路。別の解放経路は作らない
    }
}
```

### 13.3 `AssetGroupTicket`（まとめチケット）

`LoadByLabelAsync` が返す、Label 1回分のチケット。中身は通し番号で管理され、`Release(group)` で全件が解放される。

```csharp
public sealed class AssetGroupTicket
{
    /// <summary> 読み込んだ Label </summary>
    public string AccLabel { get; }
    /// <summary> 中身の件数 </summary>
    public int AccCount { get; }
    /// <summary> 解放済み（明示解放または寿命による回収）で、中身が使えない状態か </summary>
    public bool AccIsReleased { get; private set; }

    public bool TryGetAsset<T>(string address, out T asset) where T : UnityEngine.Object;
    public IReadOnlyList<T> GetAssets<T>() where T : UnityEngine.Object;

    internal AssetGroupTicket(string label, int count); // 件数は生成時に渡す。中身は、全件の確保が終わった後に Fill で詰める
    internal void Fill(string[] addresses, Type[] asset_types, UnityEngine.Object[] assets, long[] serials); // 配列はコピーせず保持する。Loading の型を知らずに済むよう、配列で受け取る
    internal long GetSerial(int index);
    internal void Invalidate(); // AccIsReleased を true にしてキャッシュを破棄する。明示解放と寿命による回収の両方から呼ぶ
}
```

- **`sealed class` にする（struct にしない）**：取り出し結果のキャッシュと、「解放済み」の印という、変化する状態を持つため。struct だとコピーごとに状態が分かれる
- **型は取り出すときに指定する。読み込んだ型が `T` と完全一致するものだけを返す**
  - 基底型では探さない。画像は Texture2D と Sprite の2件として読まれる（13.4）ため、`Object` を指定すると同じ画像が2件返ってしまう
  - 例：`GetAssets<Sprite>()` は、Label が付いたアセットのうち、Sprite として読み込まれたものすべてを返す
- `TryGetAsset<T>`：そのアドレス（`PrimaryKey`）で、読み込んだ型が `T` と完全一致するものがあれば `true` を返す。無ければ `false` と `null`
- `GetAssets<T>()` の戻り値
  - 型ごとの初回の呼び出しで `List<T>` を作り、`ReadOnlyCollection<T>` で包んで、`Dictionary<Type, object>` にキャッシュする。2回目以降は同じインスタンスを返す（確保しない）。`List<T>` にキャストして書き換えられると、次回以降の結果も壊れるため、`ReadOnlyCollection` で包む
  - 該当が0件、または解放済み（`AccIsReleased`）のときは `Array.Empty<T>()` を返す
  - `IReadOnlyList<T>` 経由の `foreach` は列挙子がボックス化されて確保が発生するため、`for` と `Count`／インデクサで回す（13.8）
- 解放済みのまとめチケットから取り出したアセットは、使ってはいけない（1件ずつのチケットで、`Release` の後に `AccAsset` を使ってはいけないのと同じ）

### 13.4 ロケーションの解決（`ILabelLocationService`）

Service 層。Label のロケーションを、**型を絞らずに**取得する（`Addressables.LoadResourceLocationsAsync(label)`）。

```csharp
internal interface ILabelLocationService
{
    // 非同期型(Task/UniTask)の切り替え方針は6.2参照
    Task ResolveAsync(string label, List<LabelLocation> result, CancellationToken ct);
}

internal readonly struct LabelLocation
{
    internal string AccPrimaryKey { get; }  // アドレス（Include Addresses in Catalog が無効だと、アドレスにならない場合がある。13.11）
    internal Type AccResourceType { get; }
}
```

- 結果は `result` に、(PrimaryKey, ResourceType) の組を、重複を除いて詰める
- **`UnityEngine.Object` ではない型は除外し、警告ログを出す**（`typeof(UnityEngine.Object).IsAssignableFrom(ResourceType)` で判定する）。Label は Scene のエントリにも付けられ、その場合の ResourceType は `SceneInstance` になる。`AssetLoaderService<T>` の型制約（`where T : UnityEngine.Object`）に違反し、Worker の生成（13.5）が失敗するため
- 1つのアドレスに複数のアセットが付いている（アドレスの重複）場合は、警告ログを出す。`Addressables.LoadAssetAsync(key)` は先頭の1件しか読まないため、区別できない
- ロケーションの handle は、キャンセルされた場合も含めて必ず解放する（完了前にキャンセルされた場合は、完了を待ってから解放する）
- 結果の順序は保証しない（Addressables の Play Mode Script によって変わる。13.12）。順序に依存しない
- 解決に失敗した場合は `AssetLoadException` を投げる。`AccKey` には、Label を `AssetKey.FromPath` で包んだキーが入る（Label 専用の入口種別は作らない）。ロケーションの解決はリトライしない
- Label が存在しない場合は失敗ではなく、0件の成功になる（13.12）

### 13.5 型ごとの読み込みと判定キー

(PrimaryKey, ResourceType) の組ごとに、その型で1件ずつ読み込む。重複ロードの判定キーは `(AssetKey, 型)`（`AssetEntryKey`。7.1）なので、次のようになる。

- Label で読んだ Sprite と、単体の `LoadAsync<Sprite>(address)` は、**同じエントリを共有する**（実ロードは1回）
- 同じアドレスの Texture2D と Sprite は、**別のエントリ**になる。handle は別だが、中身は同じバンドル内のオブジェクトなので、メモリ上で二重には持たない
- 単体の `LoadAsync` で、同じキーを別の型で要求しても例外にならない（以前は `InvalidOperationException`）。型の取り違えを例外で検出できなくなる点は、方針として受け入れた（設計仕様書5.3・6章）

**Worker は実行時の型から生成する**。型は実行時にしかわからないため、`IAssetLoaderServiceFactory.Create(AssetKey key, Type type)`（6章）が、次の方法で生成する。

- `MethodInfo.MakeGenericMethod(type)` と `Delegate.CreateDelegate` で、`Create<T>` を呼ぶ delegate を作る。**型ごとに delegate をキャッシュ**し、初回の呼び出しだけがリフレクションのコストを払う
- 単体の `LoadAsync<T>` は `Create<T>` を直接呼び、リフレクションを通さない
- 型引数は参照型（`UnityEngine.Object` の派生）なので、IL2CPP でも共有ジェネリックとして動く
- 次の方法は使わない。`Activator.CreateInstance`（internal なコンストラクタを呼ぶ形になり、扱いにくい）、`Expression.Compile`（IL2CPP ではインタプリタ実行になり、ストリッピングの影響も受けやすい）

### 13.6 解放

- `Release(group)`：中の通し番号ごとに、1件ずつの解放と同じ経路（`TryRemoveTicket` → `ReleaseInternal`）を呼び、最初に `group.Invalidate()` を呼ぶ（13.2のコード参照）。別の解放経路は作らない
- `null` のまとめチケット、または解放済み（`AccIsReleased`）のまとめチケットは、警告ログを出すだけで何もしない
- すべての解放は、チケットの記録（`IssuedTicketRecord`）を起点にする（3.1・3.2）

### 13.7 寿命による回収

- Scene / Owner 寿命の Label ロードは、寿命が終わると、全件が同じスコープの `ReleaseLifetime` に回収される。同じ `lifetime` で確保した全件は同じスコープに入っているため、必ず全件がまとめて回収される
- `ReleaseLifetime` は、record がまとめチケットへの参照を持つ場合、最初に回収した1件で、まとめチケットを無効にする（`Invalidate()`）。回収後のまとめチケットは `AccIsReleased == true` になり、`GetAssets<T>()` は空を返す。**回収後のまとめチケットの中身は使ってはいけない**
- **リーク警告は、呼び出し元にまとめチケットを返し終えた分だけに出す**。ロード中に寿命が終わった分は、呼び出し元がまだまとめチケットを受け取っておらず、`Release` を呼びようがないため、警告せず `Log`（Verbose）に留める。まとめチケットは、受け取り前の分も `Invalidate()` する（10章）
- **リーク警告は、まとめチケット1枚につき1件にまとめる**（label と件数を出す）。件数分の警告とスタックトレースが出るのを避けるため。Editor ではスタックトレースを付けるが、取得はまとめチケット1枚につき1回だけにする（`#if UNITY_EDITOR`）
- ロード中に寿命が終わった場合は、`await` の後で、全件の通し番号のうち1件でも残っていなければ（回収済み）、残りを巻き戻して `OperationCanceledException` を投げる（単体の Load と同じメッセージ）

### 13.8 利用例

```csharp
var handler = AddressableManager.AccInstance.AccHandler;
var group = await handler.LoadByLabelAsync("Manual", AssetLifetime.Owner(this), token);

// Label の中身の型を知らなくても読み込める。使いたい型を、取り出すときに指定する
if (group.TryGetAsset<Sprite>("icon_sword", out var sprite))
    _image.sprite = sprite;

var clips = group.GetAssets<AudioClip>();
for (int i = 0; i < clips.Count; i++) // IReadOnlyList の foreach はボックス化されるため、for で回す
    Register(clips[i]);

// 要らなくなったら、まとめて解放する。解放後は、取り出したアセットを使ってはいけない
handler.Release(group);
```

### 13.9 並行呼び出しとバッファ

- 同じ Label に並行して `LoadByLabelAsync` を呼んでも、まとめチケットは別々に発行される。同じエントリを共有して RefCount が加算され、それぞれ独立に解放できる
- ロケーションのバッファ（`List<LabelLocation>`）は、**呼び出しごとに確保する**。`ResolveAsync` は `await` をまたぐため、フィールドで使い回すと、並行する呼び出しの間で中身が混ざる。`ReleaseLifetime` の使い回しバッファ（11章）とは前提が違う
- 確保（同期）で使う配列と、まとめチケットの中身は、呼び出しごとに確保する（毎フレーム呼ぶ処理ではないため許容する）

### 13.10 例外の優先順位

失敗・キャンセル・寿命の終了が重なったときは、次の優先順位で例外を投げる。**どの場合も、全件を巻き戻してから投げる**。

| 優先 | 状況 | 例外 |
|---|---|---|
| 1 | ct がキャンセルされた | `OperationCanceledException` |
| 2 | ロード中に寿命が終わった（全件のチケットが残っていない） | `OperationCanceledException`（単体の Load と同じメッセージ） |
| 3 | いずれかのロードに失敗した | 最初の `AssetLoadException` |

ロケーションの解決の失敗は、手順2の段階の `AssetLoadException`（13.4）。この時点では、まだ確保していないため、巻き戻すものは無い。

### 13.11 制限事項

- Sprite Mode が Multiple の画像や Sprite Atlas は、1つのアドレスにつき最初の1枚だけが読まれる（`LoadAssetAsync<Sprite>` の挙動）。中身をすべて取り出すことは、今回の対象外
- Texture Type が Default の画像（Sprite として設定されていない画像）は、Texture2D としてだけ返る。Sprite としては取り出せない
- アドレスの重複：同じアドレスに複数のアセットが付いていると、先頭の1件だけが読まれる。警告ログで検出する（13.4）
- 「Include Addresses in Catalog」が無効だと、`PrimaryKey` がアドレスにならない場合がある。`TryGetAsset` に渡すアドレスは `PrimaryKey` と一致する必要がある
- `UnityEngine.Object` ではない型（`SceneInstance` など）は読み込めない。除外して警告を出す（13.4）
- ロケーションの順序は保証されない。結果を順序に依存して使ってはいけない

### 13.12 Addressables の挙動の確認結果

型を絞らずに `LoadResourceLocationsAsync(label)` で取得したロケーションを、サンプルプロジェクト（2026-10-01）の Label `Manual` / `Scene` / `Owner` で確認した。「Use Asset Database」と「Use Existing Build」で、型と件数は同じだった。

| 対象 | 取得されたロケーション |
|---|---|
| 画像（Sprite 設定のテクスチャ） | Texture2D と Sprite の2件（同じ PrimaryKey） |
| Prefab | GameObject の1件 |
| AudioClip | AudioClip の1件 |
| 存在しない Label | 成功で0件（例外にならない） |

- 違いは**並び順**と Provider だけだった（Asset Database は `AssetDatabaseProvider`、Existing Build は `BundledAssetProvider`）。並び順は、モードによって変わる
- Existing Build の確認には、Addressables のコンテンツビルドが必要になる。コンテンツビルドは、プレイヤー向けにコンパイルされるすべてのスクリプトが通る必要がある（Runtime テストの asmdef に `defineConstraints: ["UNITY_INCLUDE_TESTS"]` が必要だった）
