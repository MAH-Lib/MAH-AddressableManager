using System;
using System.Collections.Generic;
using System.Threading;
using MAH.AddressableManager;
using MAH.AddressableManager.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ADDRESSABLE_UNITASK_SUPPORT
using Cysharp.Threading.Tasks;
#else
using System.Threading.Tasks;
#endif

namespace MAH.AddressableManager.Core.Loading
{
    /// <summary>
    /// 実行時ロードの中核。何度でも並行に呼ばれ続け、アプリ生存中ずっと状態を保持する。
    /// Registry（Model）と Service の両方を知ってよいのはこの Controller だけ。
    /// Label 一括ロードは、ロケーションの解決（ILabelLocationService）と、1件ずつの Load と同じ確保・解放の経路を組み合わせて行う。
    /// 初期化との関わりは IInitializationGate（Core 直下の共通インターフェース）だけで、Initialize 機能は参照しない。
    /// </summary>
    internal sealed class LoadingController : IDisposable
    {
        private readonly AddressableLoadingSettings _settings;
        private readonly AssetRegistryModel _registry; // Entryの状態データ（Managerから注入）
        private readonly IssuedTicketRegistryModel _tickets; // 発行済みチケットの状態データ（Managerから注入）
        private readonly OwnerWatchRegistryModel _ownerWatch; // ポーリング対象ownerの状態データ（Managerから注入）
        private readonly IAssetLoaderServiceFactory _serviceFactory;
        private readonly ILabelLocationService _labelLocationService;
        private readonly IAssetLifetimeWatcherBinder _watcherBinder;
        private readonly IInitializationGate _initializationGate; // 読み込んでよいかを問い合わせる窓口（Managerから注入）
        private readonly Action<AssetEntryModel> _onLoadFailed; // クロージャを毎回作らないようキャッシュ
        private readonly Action<LifetimeScopeId> _onOwnerDestroyed; // Watcherに渡す、キャッシュ済みのdelegate
#if UNITY_EDITOR
        private readonly LeakRegistryModel _leaks; // リークを確保元ごとに溜める状態データ（Managerから注入。Editorのデバッグウィンドウ用）
        private readonly OwnerReclaimRegistryModel _ownerReclaims; // Ownerの回収を経路（OnDestroy/ポーリング）ごとに溜める状態データ（Managerから注入。Editorのデバッグウィンドウ用）
        private string _unloadingSceneName; // HandleSceneUnloaded の間だけ設定する、アンロード中のシーン名（リークの記録に使う）
#endif
        private bool _isDisposed;

        internal LoadingController(
            AddressableLoadingSettings settings,
            IAssetLoaderServiceFactory service_factory,
            ILabelLocationService label_location_service,
            IAssetLifetimeWatcherBinder watcher_binder,
            IInitializationGate initialization_gate,
            AssetRegistryModel registry,
            IssuedTicketRegistryModel tickets,
            OwnerWatchRegistryModel owner_watch
#if UNITY_EDITOR
            , LeakRegistryModel leaks
            , OwnerReclaimRegistryModel owner_reclaims
#endif
            )
        {
            _settings = settings;
            _serviceFactory = service_factory;
            _labelLocationService = label_location_service;
            _watcherBinder = watcher_binder;
            _initializationGate = initialization_gate;
            _registry = registry;
            _tickets = tickets;
            _ownerWatch = owner_watch;
#if UNITY_EDITOR
            _leaks = leaks ?? throw new ArgumentNullException(nameof(leaks));
            _ownerReclaims = owner_reclaims ?? throw new ArgumentNullException(nameof(owner_reclaims));
#endif
            _onLoadFailed = OnLoadFailed;
#if UNITY_EDITOR
            _onOwnerDestroyed = HandleOwnerDestroyed; // 回収経路（OnDestroy）を記録してから解放する。本番ビルドでは ReleaseLifetime を直接渡す
#else
            _onOwnerDestroyed = ReleaseLifetime;
#endif
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

            // 初期化ゲートが閉じていれば、ここで待つ。入力の検証の後・寿命の解決と RefCount の加算より前なので、待機中にキャンセル・破棄されても巻き戻すものが無い。
            // 開いているときは await も確保も起きない（bool を1つ見るだけ）
            if (!_initializationGate.AccIsOpen)
                await WaitForInitializationGateAsync(lifetime, ct);

            var scope_id = ResolveScope(lifetime); // Owner指定ならWatcher登録もここで行う

            // 確保（同期）：エントリの取得または作成・RefCount の加算・チケットの登録を、await の前にまとめて行う
            var entry_key = new AssetEntryKey(key, typeof(T)); // 判定キーは (AssetKey, 型)。型が違えば別のエントリになる
            if (!_registry.TryGetEntry(entry_key, out var entry))
                entry = AddAndStartEntry(entry_key, _serviceFactory.Create<T>(key)); // 単体の Load は Create<T> を直接呼ぶ（リフレクションを通さない）
            var serial = RegisterTicket(entry, entry_key, scope_id, null); // null は「まとめチケットに属さない」

            try
            {
                await entry.AccLoadTask.WithCancellation(ct); // 共有ロードタスクは常にTaskのため、UniTask使用時もTask版のヘルパーを使う
                if (!_tickets.Contains(serial))
                    throw new OperationCanceledException("ロード中に寿命が終了しました");
            }
            catch
            {
                ReleaseBySerial(serial);
                throw;
            }
            _tickets.MarkDelivered(serial); // この後は await を挟まず、呼び出し元にチケットを返す。以降に寿命が終わった分は、Release の呼び忘れ（リーク）
            return new AssetTicket<T>(key, (T)entry.AccWorker.AccAsset, scope_id, serial);
        }

        /// <summary> 明示解放（主経路） </summary>
        internal void Release<T>(AssetTicket<T> ticket) where T : UnityEngine.Object
        {
            if (_isDisposed) return; // Dispose後のReleaseは何もしない

            // 未発行・解放済み・default のチケットは弾く（二重解放防止）
            if (!TryRemoveTicket(ticket.AccSerial, out var record))
            {
                AddressableLogger.LogWarning($"解放済みまたは無効なチケットです：{ticket.AccKey}");
                return;
            }
            ReleaseInternal(record);
        }

        /// <summary>
        /// Label 一括ロード。Load のときに型は指定せず、Label のロケーションを型を絞らずに取得して、(PrimaryKey, 型) の組ごとに読み込む。
        /// 「確保（同期）」と「待機」の2段階に分け、失敗・キャンセル・寿命の終了では、残りを待たずに全件をすぐ巻き戻す。
        /// </summary>
#if ADDRESSABLE_UNITASK_SUPPORT
        internal async UniTask<AssetGroupTicket> LoadByLabelAsync(string label, AssetLifetime lifetime, CancellationToken ct = default)
#else
        internal async Task<AssetGroupTicket> LoadByLabelAsync(string label, AssetLifetime lifetime, CancellationToken ct = default)
#endif
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(label))
                throw new ArgumentException("Label が null または空文字です。", nameof(label));

            // 初期化ゲートが閉じていれば、ロケーションの解決より前に待つ（LoadAsync と同じ理由・同じ位置づけ）
            if (!_initializationGate.AccIsOpen)
                await WaitForInitializationGateAsync(lifetime, ct);

            // 呼び出しごとに確保する。ResolveAsync は await をまたぐため、フィールドで使い回すと、並行する呼び出しの間で中身が混ざる
            var locations = new List<LabelLocation>();
            await _labelLocationService.ResolveAsync(label, locations, ct);
            ThrowIfDisposed(); // await の間に破棄されている可能性がある

            var group = new AssetGroupTicket(label, locations.Count);
            if (locations.Count == 0)
            {
                AddressableLogger.LogWarning($"Label「{label}」に該当するアセットがありません。");
                return group;
            }

            // 寿命は確保の直前に解決する（Owner の Watcher 登録とチケット登録の間に await を挟まないため）
            var scope_id = ResolveScope(lifetime);

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
                        entry = AddAndStartEntry(entry_key, _serviceFactory.Create(key, entry_key.AccAssetType)); // 実行時の型から生成する
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

                // 例外の優先順位：1. ct のキャンセル  2. 寿命の終了  3. 最初の AssetLoadException
                ct.ThrowIfCancellationRequested();
                if (lifetime_ended)
                    throw new OperationCanceledException("ロード中に寿命が終了しました");
                throw;
            }

            var assets = new UnityEngine.Object[entries.Length];
            for (int i = 0; i < entries.Length; i++)
                assets[i] = entries[i].AccWorker.AccAsset;
            group.Fill(addresses, asset_types, assets, serials);
            for (int i = 0; i < serials.Length; i++)
                _tickets.MarkDelivered(serials[i]); // この後は await を挟まず、呼び出し元にまとめチケットを返す
            return group;
        }

        /// <summary> まとめチケットの解放。中身を、1件ずつの解放と同じ経路（TryRemoveTicket → ReleaseInternal）で解放する </summary>
        internal void Release(AssetGroupTicket group)
        {
            if (_isDisposed) return; // Dispose後のReleaseは何もしない

            // null・解放済み（明示解放済み、または寿命による回収済み）のまとめチケットは弾く（二重解放防止）
            if (group == null || group.AccIsReleased)
            {
                AddressableLogger.LogWarning($"解放済みまたは無効なまとめチケットです：{group?.AccLabel}");
                return;
            }

            group.Invalidate(); // 先に無効化し、同じまとめチケットの2回目の Release を弾く
            for (int i = 0; i < group.AccCount; i++)
            {
                if (TryRemoveTicket(group.GetSerial(i), out var record))
                    ReleaseInternal(record); // 別の解放経路は作らない
            }
        }

        /// <summary> 寿命スコープ終了時の一括回収（セーフティネット）。Scene/Owner寿命の実装ステップから呼ばれる </summary>
        internal void ReleaseLifetime(LifetimeScopeId scope_id)
        {
            var serials = _tickets.SnapshotSerialsForScope(scope_id);
#if UNITY_EDITOR
            // リークの記録用に、Owner の参照だけ先に取る（最後のチケットを取り除くと Watcher の登録が外れるため。名前は記録するときだけ読む）
            _ownerWatch.TryGetOwner(scope_id, out var leak_owner);
#endif

            // 明示解放済みの分は既に取り除かれているので、残っている分だけが解放漏れ
            foreach (var serial in serials)
            {
                if (!TryRemoveTicket(serial, out var record))
                    continue;

                // リーク警告は、呼び出し元にチケットを返し終えた分だけに出す。
                // 返す前（ロード中）に寿命が終わった分は、呼び出し元が Release を呼びようがなく、OperationCanceledException が届くだけなので、警告しない
                var group = record.AccGroup;
                if (group == null)
                {
                    if (record.AccIsDelivered)
                    {
#if UNITY_EDITOR
                        AddressableLogger.LogWarning(CreateLeakMessage(record.AccKey, record.AccStackTrace));
                        _leaks.RecordTicketLeak(record.AccKey, record.AccEntryKey.AccAssetType, scope_id, ResolveLeakScopeName(scope_id, leak_owner), record.AccStackTrace);
#else
                        AddressableLogger.LogWarning(CreateLeakMessage(record.AccKey, null)); // 組み立ては引数の中で行う（Conditional が呼び出しごと取り除くため、リリースビルドに文字列の生成が残らない）
#endif
                    }
                    else
                    {
                        AddressableLogger.Log($"{record.AccKey} は、ロード中に寿命が終了したため回収しました。");
                    }
                }
                else if (!group.AccIsReleased)
                {
                    // まとめチケットは、1枚につき1件だけ警告する。最初に回収した1件が、警告と無効化を行う
                    if (record.AccIsDelivered)
                    {
#if UNITY_EDITOR
                        AddressableLogger.LogWarning(CreateLeakMessage(group, group.AccStackTrace));
                        _leaks.RecordGroupLeak(group.AccLabel, group.AccCount, scope_id, ResolveLeakScopeName(scope_id, leak_owner), group.AccStackTrace);
#else
                        AddressableLogger.LogWarning(CreateLeakMessage(group, null));
#endif
                    }
                    else
                    {
                        AddressableLogger.Log($"Label「{group.AccLabel}」のまとめチケットは、ロード中に寿命が終了したため回収しました。");
                    }
                    group.Invalidate(); // 回収したまとめチケットは使えなくする（返す前の分も、中身は使われないので無効にしてよい）
                }
                ReleaseInternal(record);
            }

            // チケットが1件も無いまま監視だけが残ったOwner（確保の途中で例外が出た等）も、ここで外す。
            // チケットを取り除いた場合は TryRemoveTicket が外し済みなので、ここでは何もしない
            UnwatchOwnerIfNeeded(scope_id);
        }

        /// <summary> リーク警告の文言（単体）。stack_trace は Editor 限定で確保時に記録したもの。無ければ付けない </summary>
        private static string CreateLeakMessage(AssetKey key, string stack_trace)
        {
            var message = $"{key} が明示 Release されずに寿命回収されました（リーク）。Release の呼び忘れがないか確認してください。";
            return string.IsNullOrEmpty(stack_trace) ? message : $"{message}\n確保元のスタックトレース:\n{stack_trace}";
        }

        /// <summary> リーク警告の文言（まとめチケット）。中身の件数分ではなく、まとめチケット1枚につき1件を出す </summary>
        private static string CreateLeakMessage(AssetGroupTicket group, string stack_trace)
        {
            var message = $"Label「{group.AccLabel}」のまとめチケット（{group.AccCount}件）が明示 Release されずに寿命回収されました（リーク）。Release の呼び忘れがないか確認してください。";
            return string.IsNullOrEmpty(stack_trace) ? message : $"{message}\n確保元のスタックトレース:\n{stack_trace}";
        }

        /// <summary> ManagerのLateUpdateイベントから毎フレーム呼ばれる。Unity非依存のためテストからも駆動可能 </summary>
        internal void Tick(float delta_time)
        {
            if (!_settings.AccEnableOwnerPolling) return;
            if (_ownerWatch.Count == 0) return; // owner寿命の登録が無い間は何もしない
            if (!_ownerWatch.AdvanceElapsed(delta_time, _settings.AccOwnerPollingIntervalSec)) return;

            PollDestroyedOwners();
        }

        /// <summary> 登録済みownerを走査し、偽nullのものをReleaseLifetimeで回収する（Watcherの取りこぼし対策） </summary>
        private void PollDestroyedOwners()
        {
            foreach (var scope_id in _ownerWatch.CollectDestroyedScopes())
            {
#if UNITY_EDITOR
                RecordOwnerReclaim(OwnerReclaimRoute.Polling, scope_id); // Watcher が取りこぼした分。解放の前に記録する（回収でチケットと監視が外れるため）
#endif
                ReleaseLifetime(scope_id);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Watcher の OnDestroy からの通知（主経路）。回収経路を記録してから、従来どおり ReleaseLifetime で解放する。
        /// Dispose 後は、監視が外れている（IsWatching が false）ため記録されず、ReleaseLifetime も空振りする（本番ビルドと同じ流れ）。
        /// </summary>
        private void HandleOwnerDestroyed(LifetimeScopeId scope_id)
        {
            RecordOwnerReclaim(OwnerReclaimRoute.OnDestroy, scope_id);
            ReleaseLifetime(scope_id);
        }

        /// <summary>
        /// Owner の回収を記録する。監視中の Owner だけが対象で、明示 Release で監視が外れた Owner や、回収済みの Owner への通知は記録しない（二重記録の防止）。
        /// 名前は登録時のもの。チケット数は、ReleaseLifetime が取り除く前の件数。
        /// </summary>
        private void RecordOwnerReclaim(OwnerReclaimRoute route, LifetimeScopeId scope_id)
        {
            if (!_ownerWatch.IsWatching(scope_id)) return;
            _ownerWatch.TryGetOwnerName(scope_id, out var owner_name);
            var ticket_count = _tickets.CountForScope(scope_id);
            _ownerReclaims.Record(route, scope_id.AccValue, owner_name, ticket_count);
        }
#endif

        /// <summary> Managerが中継するsceneUnloadedから呼ばれる </summary>
        internal void HandleSceneUnloaded(Scene scene)
        {
#if UNITY_EDITOR
            // リークの記録にシーン名を使うため、回収の間だけ保持する（ReleaseLifetime は同期のため、他の呼び出しと交差しない）
            _unloadingSceneName = scene.name;
            try
            {
                ReleaseLifetime(new LifetimeScopeId(LifetimeKind.Scene, scene.handle));
            }
            finally
            {
                _unloadingSceneName = null;
            }
#else
            ReleaseLifetime(new LifetimeScopeId(LifetimeKind.Scene, scene.handle));
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// リーク発生時のスコープ名。寿命の終了時に起きるため、後から引くと破棄済みになるので、ここで文字列にして記録する。
        /// 取れないとき（ポーリングで見つかった破棄済みの Owner など）は空文字
        /// </summary>
        private string ResolveLeakScopeName(LifetimeScopeId scope_id, GameObject owner)
        {
            switch (scope_id.AccKind)
            {
                case LifetimeKind.Owner:
                    return owner != null ? owner.name : string.Empty;
                case LifetimeKind.Scene:
                    return _unloadingSceneName ?? string.Empty;
                default:
                    return string.Empty;
            }
        }
#endif

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _watcherBinder.UnbindAll(); // 貼ったWatcherをすべてUnbindする
            _registry.DisposeAllAndClear(); // 残Entryをすべて強制解放する（最終防衛線）
            _tickets.Clear();
            _ownerWatch.Clear();
#if UNITY_EDITOR
            _leaks.Clear();
            _ownerReclaims.Clear();
#endif
        }

        /// <summary>
        /// 初期化ゲートが開くまで待つ。失敗している場合は AddressableInitializeException、未開始（WaitForInitialize）なら InvalidOperationException がそのまま届く。
        /// 共有タスクは常に Task のため、UniTask 版でもこのメソッドは Task で書く（UniTask 版では using が無いため完全修飾で書く）。
        /// </summary>
        private async System.Threading.Tasks.Task WaitForInitializationGateAsync(AssetLifetime lifetime, CancellationToken ct)
        {
            // 待機の前に寿命が有効かを確認しておく。待機の前から無効なら、これまでどおり後続の寿命の解決が ArgumentException にする
            var was_lifetime_alive = IsLifetimeAlive(lifetime);
            try
            {
                await _initializationGate.WaitUntilOpenAsync(ct);
            }
            catch (OperationCanceledException) when (_isDisposed && !ct.IsCancellationRequested)
            {
                // Manager の破棄でゲートの待機が終わった。ゲートは Loading の破棄を知らないため、ここで破棄後の Load と同じ例外にそろえる
                throw new ObjectDisposedException(nameof(LoadingController));
            }

            ThrowIfDisposed(); // await を挟むため、もう一度破棄済みを確認する
            // 待っている間に寿命が終わった場合は、引数の誤り（ArgumentException）ではなく、ロード中の寿命の終了として扱う
            if (was_lifetime_alive && !IsLifetimeAlive(lifetime))
                throw new OperationCanceledException("ロード中に寿命が終了しました");
        }

        /// <summary> 寿命がまだ有効か（owner が破棄されていない・シーンがロード済み）。Manual は常に有効 </summary>
        private static bool IsLifetimeAlive(AssetLifetime lifetime)
        {
            switch (lifetime.AccKind)
            {
                case LifetimeKind.Owner:
                    return lifetime.AccOwner != null; // 破棄済みの GameObject は偽 null になる
                case LifetimeKind.Scene:
                    return lifetime.AccScene.isLoaded;
                default:
                    return true;
            }
        }

        private LifetimeScopeId ResolveScope(AssetLifetime lifetime)
        {
            switch (lifetime.AccKind)
            {
                case LifetimeKind.Manual:
                    return LifetimeScopeId.Manual;
                case LifetimeKind.Scene:
                    return ResolveSceneScope(lifetime.AccScene);
                case LifetimeKind.Owner:
                    return ResolveOwnerScope(lifetime.AccOwner);
                default:
                    throw new NotSupportedException($"{lifetime.AccKind} 寿命はまだ実装されていません");
            }
        }

        private LifetimeScopeId ResolveOwnerScope(GameObject owner)
        {
            if (owner == null)
                throw new ArgumentException("owner がすでに破棄されています。", nameof(owner));

            var scope_id = new LifetimeScopeId(LifetimeKind.Owner, owner.GetInstanceID());
            if (!_ownerWatch.IsWatching(scope_id))
            {
                _ownerWatch.Watch(scope_id, owner);
                _watcherBinder.Bind(owner, scope_id, _onOwnerDestroyed);
            }
            return scope_id;
        }

        private LifetimeScopeId ResolveSceneScope(Scene scene)
        {
            if (!scene.isLoaded)
                throw new ArgumentException("未ロードのシーンが指定されました。", nameof(scene));

            if (IsDontDestroyOnLoadScene(scene))
            {
                // DontDestroyOnLoad シーンはアンロードされないため、実質 Manual として扱う
                AddressableLogger.LogWarning("DontDestroyOnLoad シーンが Scene 寿命に指定されました。このシーンはアンロードされないため、実質 Manual 寿命として扱います。");
                return LifetimeScopeId.Manual;
            }

            return new LifetimeScopeId(LifetimeKind.Scene, scene.handle);
        }

        // Unity には「DontDestroyOnLoad か」を直接判定する公開APIが無いため、固定の名前で判定する
        private const string DONT_DESTROY_ON_LOAD_SCENE_NAME = "DontDestroyOnLoad";

        private static bool IsDontDestroyOnLoadScene(Scene scene) => scene.name == DONT_DESTROY_ON_LOAD_SCENE_NAME;

        private void OnLoadFailed(AssetEntryModel entry)
        {
            // 失敗したエントリは即座に削除し、次のLoadで新しく実ロードを始められるようにする。
            // Registry にあるエントリが同じ場合だけ取り除く（同じキーで新しいエントリができていても消さない）
            _registry.RemoveEntryIfSame(entry.AccEntryKey, entry);
            entry.AccWorker.Dispose();
        }

        /// <summary> 新しいエントリを Registry に登録し、実ロードを1回だけ起動する </summary>
        private AssetEntryModel AddAndStartEntry(AssetEntryKey entry_key, IAssetLoaderWorker worker)
        {
            var entry = _registry.AddEntry(entry_key, worker); // Worker は AssetKey.AccValue を string として使う
            entry.StartLoad(_onLoadFailed); // 実ロードは1回だけ起動。失敗したらエントリを即削除する
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

        /// <summary> 明示解放も自動回収も巻き戻しも、すべてこの1経路を通す。解放は、記録（record）が持つエントリを起点にする </summary>
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

        /// <summary> 先頭から count 件の通し番号が、すべて発行済み（未解放）のままか </summary>
        private bool ContainsAll(long[] serials, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!_tickets.Contains(serials[i]))
                    return false;
            }
            return true;
        }

        private void ReleaseBySerial(long serial)
        {
            if (!TryRemoveTicket(serial, out var record))
                return;
            ReleaseInternal(record);
        }

        /// <summary> チケットを取り除く。そのスコープのチケットが尽きたらWatcher登録も外す </summary>
        private bool TryRemoveTicket(long serial, out IssuedTicketRecord record)
        {
            if (!_tickets.TryRemove(serial, out record, out var scope_exhausted))
                return false;
            if (scope_exhausted)
                UnwatchOwnerIfNeeded(record.AccScopeId);
            return true;
        }

        private void UnwatchOwnerIfNeeded(LifetimeScopeId scope_id)
        {
            if (!_ownerWatch.TryGetOwner(scope_id, out var owner))
                return; // Owner寿命以外のscope_idはここに登録されていない

            _ownerWatch.Unwatch(scope_id);
            _watcherBinder.Unbind(owner, scope_id);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(LoadingController));
        }
    }
}
