using System;
using System.Text;
using System.Threading;
using MAH.AddressableManager.Attribute;
using MAH.AddressableManager.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MAH.AddressableManager.Samples
{
    /// <summary>
    /// ダウンロード機能（更新の有無の判定・更新の確認・ダウンロードと進捗）を確かめるサンプル。
    /// <para>
    /// 【確かめられること】
    /// <list type="bullet">
    /// <item>Has Update：更新データがあるかだけを <c>bool</c> で返す（カタログは更新しない）</item>
    /// <item>Check Update：カタログを確認・更新し、更新されたカタログ数・ダウンロードが必要なバンドル数・合計バイト数を返す（確認ダイアログに出す値）</item>
    /// <item>Download：ダウンロードして進捗（段階・バイト数・割合・リトライ回数）を受け取る。キャンセルもできる</item>
    /// <item>状態の取得：毎フレーム <c>AccDownloadStatus</c> を取得し直して表示する（取得した値は、その時点のコピー）</item>
    /// <item>最後の通知：<c>IProgress</c> に最後に届く値（Completed / Failed / Canceled）で、結果を区別する</item>
    /// </list>
    /// </para>
    /// <para>
    /// ボタンは、初期化が成功するまで無効にしている（<see cref="HandleSpawned"/>）。ダウンロード機能は、初期化の成功を待ってから動くため、
    /// 実装でも、初期化が終わるまで操作を受け付けない作りにしておくと、手動モードで初期化が始まっていない場合の例外を避けられる。
    /// このサンプルの Manager は、自動初期化の設定（<c>DownloadSettings</c>）で生成する。
    /// </para>
    /// <para>
    /// ⚠ Play Mode Script が「Use Asset Database」のときは、バンドルが無いため、ダウンロードが必要なものは常に 0 件になる。
    /// 実際のダウンロードは、対象 Label のグループを Remote にして、ローカルの HTTP サーバーで配信する環境で確かめる（手順は download-spec.md 11.2）。
    /// Local のグループは、Use Existing Build でも、ビルド済みの手元のファイルから読むため、ダウンロードの対象にならない（0 件のまま即時に終わる）。
    /// </para>
    /// <para>
    /// 【実装例として真似る部分】<see cref="OnHasUpdateClicked"/> / <see cref="OnCheckClicked"/> / <see cref="OnDownloadClicked"/> の呼び方と例外の扱い、
    /// <see cref="Report"/>（<see cref="IProgress{T}"/> の受け取り）、<see cref="HandleSpawned"/> の初期化完了の待ち方。
    /// <c>// [検証用]</c> と書かれた処理は動作確認のためだけのもので、実装では不要。
    /// 画面（Canvas・ボタン・文字）はシーンに配置してあり、Inspector で参照を割り当てる。画面の文字は英語で、縦長（1080x1920 基準）と横長の両方に収まるよう、左上基準で縦に積んでいる。
    /// </para>
    /// </summary>
    public sealed class DownloadSampleView : MonoBehaviour, IProgress<DownloadStatus>
    {
        // [検証用] Console 上で、モジュール本体のログ（[AddressableManager]）とサンプルのログを見分けるためのタイトル
        private const string LOG_TITLE = "AddressableSample";
        // [検証用] 画面のログ欄に残す件数
        private const int LOG_CAPACITY = 10;

        [Header("Manager")]
        [Tooltip("シーンに Manager が無いときに生成する Prefab（AddressableManager_Download）")]
        [SerializeField] private AddressableManager _managerPrefab;

        [Header("対象の Label")]
        [Tooltip("空なら Runtime SO の「ダウンロード」の Label を使う（引数なし版）。入れると、その Label を引数で渡す版を使う（SO の設定を上書きする）")]
        [SerializeField, AddressableLabel] private string[] _labelsOverride = Array.Empty<string>();

        [Header("操作ボタン")]
        [SerializeField] private Button _hasUpdateButton;
        [SerializeField] private Button _checkButton;
        [SerializeField] private Button _downloadButton;
        [SerializeField] private Button _cancelButton;
        // [検証用] ダウンロードしたバンドルのキャッシュを消す。もう一度ダウンロードの動作を確かめるために使う（初期化の完了を待たず、いつでも押せる）
        [SerializeField] private Button _clearCacheButton;

        [Header("状態表示")]
        [SerializeField] private Text _statusText;
        [SerializeField] private Text _resultText;
        // [検証用] 実機には Console が無いため、操作と結果を画面にも出す
        [SerializeField] private Text _logText;
        [Tooltip("進捗バーの塗り。親の幅に対する割合（Anchor Max の X）で、進捗を表す")]
        [SerializeField] private RectTransform _barFill;

        // ボタンとイベントに渡す delegate は Awake で1度だけ作って保持する（購読・解除を対で書くため）
        private UnityAction _onHasUpdateClicked;
        private UnityAction _onCheckClicked;
        private UnityAction _onDownloadClicked;
        private UnityAction _onCancelClicked;
        private UnityAction _onClearCacheClicked;
        private Action _onInitialized;
        private Action<AddressableInitializeException> _onInitializeFailed;

        // 実行中の処理のキャンセル。View の破棄（destroyCancellationToken）とも連動させる
        private CancellationTokenSource _currentCts;

        // 購読している Handler。購読を解除するときに、同じ Handler から外す
        private IAddressableHandler _subscribedHandler;

        // [検証用] 画面表示用の記録
        private string _hasUpdateText = "-";
        private string _checkText = "-";
        private DownloadStatus _lastNotified;
        private int _notifiedCount;
        private DownloadPhase _lastLoggedPhase = DownloadPhase.None;
        private DownloadStatus _lastResult; // IProgress に最後に届いた、結果（Completed / Failed / Canceled）の値
        private bool _hasLastResult;
        private readonly string[] _logs = new string[LOG_CAPACITY];
        private int _logCount;
        private int _logNext;
        private readonly StringBuilder _builder = new();

        private void Awake()
        {
            _onHasUpdateClicked = OnHasUpdateClicked;
            _onCheckClicked = OnCheckClicked;
            _onDownloadClicked = OnDownloadClicked;
            _onCancelClicked = OnCancelClicked;
            _onClearCacheClicked = OnClearCacheClicked;
            _onInitialized = HandleInitialized;
            _onInitializeFailed = HandleInitializeFailed;
            _hasUpdateButton.onClick.AddListener(_onHasUpdateClicked);
            _checkButton.onClick.AddListener(_onCheckClicked);
            _downloadButton.onClick.AddListener(_onDownloadClicked);
            _cancelButton.onClick.AddListener(_onCancelClicked);
            _clearCacheButton.onClick.AddListener(_onClearCacheClicked);

            // 初期化が成功するまで、ボタンを無効にする
            SetButtonsInteractable(false);
            RefreshTexts(default);

            // Manager が無ければ生成する。Manager は Awake で設定を読み、DontDestroyOnLoad になる
            if (AddressableManager.AccInstance == null && _managerPrefab != null)
                Instantiate(_managerPrefab);

            HandleSpawned();
        }

        private void OnDestroy()
        {
            // イベント購読は必ず対で解除する
            _hasUpdateButton.onClick.RemoveListener(_onHasUpdateClicked);
            _checkButton.onClick.RemoveListener(_onCheckClicked);
            _downloadButton.onClick.RemoveListener(_onDownloadClicked);
            _cancelButton.onClick.RemoveListener(_onCancelClicked);
            _clearCacheButton.onClick.RemoveListener(_onClearCacheClicked);

            if (_subscribedHandler != null)
            {
                _subscribedHandler.OnInitialized -= _onInitialized;
                _subscribedHandler.OnInitializeFailed -= _onInitializeFailed;
                _subscribedHandler = null;
            }

            // 実行中の処理があれば、待機をやめる（Manager が先に破棄されていても、キャンセルは安全）
            _currentCts?.Cancel();
        }

        private void Update()
        {
            // 【状態の取得】AccDownloadStatus は、取得した時点の値のコピー。値は後から変わらないため、最新の値が必要なときは毎回取得し直す。
            // IProgress を渡していない場所（この Update のように、呼び出しとは別の場所）から、処理中の状態を参照したいときに使う。
            // 真似るのは「毎回取得し直す」ところだけ。RefreshTexts が毎フレーム文字列を作るのは [検証用] で、実際の実装では、値が変わったときだけ表示を更新する
            var handler = GetHandler();
            var status = handler != null ? handler.AccDownloadStatus : default;
            RefreshTexts(status);
        }

        private void HandleSpawned()
        {
            // 【購読の罠】Auto では、Manager の生成の途中（Awake の中）で初期化が終わることがある（成功も失敗も）。
            // その場合は購読した時点で OnInitialized / OnInitializeFailed は二度と来ないため、購読したらすぐ状態も確かめる
            var handler = GetHandler();
            if (handler == null)
            {
                AddLog("Manager is not found. Assign the Manager prefab");
                return;
            }

            _subscribedHandler = handler;
            handler.OnInitialized += _onInitialized;
            handler.OnInitializeFailed += _onInitializeFailed;
            if (handler.AccIsInitialized)
                HandleInitialized();
            else if (handler.AccInitializeState == InitializeState.Failed)
                AddLog("Initialize failed before subscribing. Restart the app"); // 購読前の失敗は、例外を受け取れない
        }

        private void HandleInitialized()
        {
            // 初期化が成功したら、ボタンを有効にする
            SetButtonsInteractable(true);
            AddLog("Initialized. Buttons are enabled");
        }

        private void HandleInitializeFailed(AddressableInitializeException ex)
        {
            // 初期化の失敗は致命的で、Addressables はアプリを再起動するまでやり直せない。ボタンは無効のままにする。
            // 実装では、ここで再起動を促すダイアログなどを出す。InnerException に Addressables の元の例外が入っている
            AddLog($"Initialize failed ({(ex.InnerException != null ? ex.InnerException.GetType().Name : ex.GetType().Name)}). Restart the app");
        }

        private void SetButtonsInteractable(bool interactable)
        {
            _hasUpdateButton.interactable = interactable;
            _checkButton.interactable = interactable;
            _downloadButton.interactable = interactable;
            _cancelButton.interactable = interactable;
        }

        /// <summary>
        /// 【進捗の受け取り】DownloadAsync に渡した IProgress に、段階が変わるたび・バイト数が変わるたび（毎フレーム）届く。
        /// 処理の終わりには、通知先が外れる前に1回だけ、結果（Completed / Failed / Canceled）が届く。受け取る側はこれで、成功・失敗・キャンセルを区別できる。
        /// 届く値は、その時点のコピー。Progress&lt;T&gt; やラムダを使わず、確保を増やさない
        /// </summary>
        public void Report(DownloadStatus value)
        {
            _lastNotified = value;
            _notifiedCount++;

            // [検証用] 段階が変わったときだけログに残す
            if (value.AccPhase != _lastLoggedPhase)
            {
                _lastLoggedPhase = value.AccPhase;
                AddLog($"Notified: {value.AccPhase}");
            }

            if (value.AccPhase == DownloadPhase.Completed
                || value.AccPhase == DownloadPhase.Failed
                || value.AccPhase == DownloadPhase.Canceled)
            {
                _lastResult = value;
                _hasLastResult = true;
            }
        }

        // async void はボタンのハンドラのように戻り値を受け取れない入口だけに使い、中は必ず try / catch で囲む。
        // 戻り値が UniTask でも Task でも、await の書き方は同じ

        private async void OnHasUpdateClicked()
        {
            var cts = BeginOperation();
            try
            {
                var handler = GetHandler();
                if (handler == null) return;

                // 【更新の有無の判定】カタログは更新しない。更新があるか（bool）だけを知りたいときに使う
                var labels = GetLabelsOverride();
                var has_update = labels == null
                    ? await handler.HasUpdateAsync(cts.Token)
                    : await handler.HasUpdateAsync(labels, cts.Token);

                _hasUpdateText = has_update.ToString();
                AddLog($"HasUpdate: {has_update}");
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
            finally
            {
                EndOperation(cts);
            }
        }

        private async void OnCheckClicked()
        {
            var cts = BeginOperation();
            try
            {
                var handler = GetHandler();
                if (handler == null) return;

                // 【更新の確認】SO の方針（None / CheckOnly / CheckAndUpdate）に従ってカタログを確認・更新し、件数と容量を受け取る。
                // この値を確認ダイアログに出して、同意されたら DownloadAsync を呼ぶ
                var labels = GetLabelsOverride();
                var result = labels == null
                    ? await handler.CheckUpdateAsync(cts.Token)
                    : await handler.CheckUpdateAsync(labels, cts.Token);

                _checkText = $"catalogs {result.AccCatalogUpdateCount} / bundles {result.AccBundleCount} / {FormatBytes(result.AccTotalBytes)} / pending {result.AccIsCatalogUpdatePending}";
                AddLog($"Check: {_checkText}");
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
            finally
            {
                EndOperation(cts);
            }
        }

        private async void OnDownloadClicked()
        {
            var cts = BeginOperation();
            try
            {
                var handler = GetHandler();
                if (handler == null) return;

                // 【ダウンロード】進捗は IProgress<DownloadStatus>（この View）で受け取る。カタログは更新しない（更新は Check Update）。
                // ct のキャンセルで止まるのは、この呼び出しの待機だけ。開始済みの通信は、止まらない場合がある
                _hasLastResult = false;
                var labels = GetLabelsOverride();
                if (labels == null)
                    await handler.DownloadAsync(this, cts.Token);
                else
                    await handler.DownloadAsync(labels, this, cts.Token);

                AddLog("Download: finished");
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
            finally
            {
                EndOperation(cts);
            }
        }

        private void OnCancelClicked()
        {
            // 実行中の処理の待機をやめる。段階は Canceled になり、最後の通知が届く
            if (_currentCts == null)
            {
                AddLog("Cancel: nothing is running");
                return;
            }
            _currentCts.Cancel();
        }

        // [検証用] ダウンロードしたバンドルのキャッシュを消す。モジュールの API ではなく、Unity の Caching.ClearCache を呼ぶだけ。
        // 一度ダウンロードしたバンドルはキャッシュに残り、次の確認・ダウンロードでは 0 件になるため、繰り返し確かめるときに使う。
        // ダウンロード中に消すと壊れる恐れがあるため、処理の実行中は受け付けない。読み込み中（使用中）のバンドルがあると、消せず false になる
        private void OnClearCacheClicked()
        {
            var handler = GetHandler();
            if (handler != null && handler.AccDownloadStatus.AccIsBusy)
            {
                AddLog("Clear Cache: rejected while busy");
                return;
            }

            var cleared = Caching.ClearCache();
            _hasUpdateText = "-";
            _checkText = "-";
            _hasLastResult = false;
            AddLog(cleared ? "Clear Cache: done" : "Clear Cache: failed (bundles are in use)");
        }

        // 【例外の扱い】呼び出しで起きる例外と、その意味
        private void HandleException(Exception ex)
        {
            switch (ex)
            {
                case OperationCanceledException:
                    // キャンセル（ct、または Manager の破棄）。View が破棄されている場合は、UI はもう無い
                    AddLog("Canceled");
                    break;
                case AddressableDownloadException download_ex:
                    // リトライを使い切って失敗した。失敗した段階とリトライ回数を持ち、元の例外が InnerException に入っている
                    AddLog($"Failed at {download_ex.AccPhase} (retry {download_ex.AccRetryCount}): {download_ex.InnerException?.Message}");
                    break;
                case AddressableInitializeException:
                    // 初期化に失敗している。Addressables はアプリを再起動するまでやり直せない
                    AddLog("Initialize failed. Restart the app");
                    break;
                case InvalidOperationException:
                    // 別の処理が実行中、Label が空、手動モードで初期化が始まっていない、のいずれか
                    AddLog($"Rejected: {ex.Message}");
                    break;
                default:
                    AddLog($"{ex.GetType().Name}: {ex.Message}");
                    AddressableLogger.LogException(ex);
                    break;
            }
        }

        // [検証用] 実行中の処理があるときに押したら、InvalidOperationException になることを確かめるため、実行中のキャンセルは差し替えない
        private CancellationTokenSource BeginOperation()
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            var handler = GetHandler();
            if (_currentCts == null && (handler == null || !handler.AccDownloadStatus.AccIsBusy))
                _currentCts = cts;
            return cts;
        }

        private void EndOperation(CancellationTokenSource cts)
        {
            if (_currentCts == cts)
                _currentCts = null;
            cts.Dispose();
        }

        private IAddressableHandler GetHandler()
        {
            var manager = AddressableManager.AccInstance;
            return manager != null ? manager.AccHandler : null;
        }

        // 空なら null（引数なし版を使う）。入っていれば、その Label を引数で渡す版を使う
        private string[] GetLabelsOverride() => _labelsOverride != null && _labelsOverride.Length > 0 ? _labelsOverride : null;

        // ================= [検証用] 画面 =================

        private void RefreshTexts(DownloadStatus status)
        {
            var handler = GetHandler();
            _builder.Clear();
            _builder.Append("Init: ").AppendLine(handler != null ? handler.AccInitializeState.ToString() : "No manager");
            _builder.Append("Phase: ").Append(status.AccPhase)
                .Append("  (busy: ").Append(status.AccIsBusy).Append(", downloading: ").Append(status.AccIsDownloading).AppendLine(")");
            _builder.Append("Bytes: ").Append(FormatBytes(status.AccDownloadedBytes)).Append(" / ").Append(FormatBytes(status.AccTotalBytes))
                .Append("  (").Append((status.AccPercent * 100f).ToString("0")).AppendLine("%)");
            _builder.Append("Retry: ").AppendLine(status.AccRetryCount.ToString());
            _builder.Append("Notified: ").Append(_lastNotified.AccPhase).Append(" #").AppendLine(_notifiedCount.ToString());
            _builder.Append("Labels: ").Append(GetLabelsOverride() != null ? string.Join(", ", _labelsOverride) + " (override)" : "Runtime SO");
            _statusText.text = _builder.ToString();

            _builder.Clear();
            _builder.Append("Last result (IProgress): ");
            if (_hasLastResult)
                _builder.Append(_lastResult.AccPhase).Append("  ").Append(FormatBytes(_lastResult.AccDownloadedBytes)).Append(" / ").Append(FormatBytes(_lastResult.AccTotalBytes));
            else
                _builder.Append("-");
            _builder.AppendLine();
            _builder.Append("Has Update: ").AppendLine(_hasUpdateText);
            _builder.Append("Check: ").Append(_checkText);
            _resultText.text = _builder.ToString();

            // 進捗バー：塗りの幅を、親の幅に対する割合にする
            _barFill.anchorMax = new Vector2(Mathf.Clamp01(status.AccPercent), 1f);

            RefreshLogText();
        }

        private void AddLog(string message)
        {
            AddressableLogger.Log(message, LOG_TITLE);
            _logs[_logNext] = message;
            _logNext = (_logNext + 1) % LOG_CAPACITY;
            if (_logCount < LOG_CAPACITY) _logCount++;
        }

        private void RefreshLogText()
        {
            _builder.Clear();
            var start = _logCount < LOG_CAPACITY ? 0 : _logNext;
            for (int i = 0; i < _logCount; i++)
            {
                if (i > 0) _builder.AppendLine();
                _builder.Append(_logs[(start + i) % LOG_CAPACITY]);
            }
            _logText.text = _builder.ToString();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024L) return $"{bytes} B";
            var kilo = bytes / 1024.0;
            if (kilo < 1024.0) return $"{kilo:0.#} KB";
            var mega = kilo / 1024.0;
            if (mega < 1024.0) return $"{mega:0.#} MB";
            return $"{mega / 1024.0:0.##} GB";
        }
    }
}
