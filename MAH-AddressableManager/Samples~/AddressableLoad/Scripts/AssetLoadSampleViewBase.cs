using System;
using System.Collections.Generic;
using MAH.AddressableManager.Attribute;
using MAH.AddressableManager.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MAH.AddressableManager.Samples
{
    /// <summary>
    /// 3つのサンプルシーン（Sprite / AudioClip / Prefab）で共通の操作をまとめた基底クラス。
    /// アセットの種類ごとの表示・再生・生成は派生クラスが <see cref="OnAssetLoaded"/> / <see cref="OnAssetReleased"/> で行う。
    /// <para>
    /// 寿命ごとに別のアセットを読み込む（Manual 用と Scene 用でアドレスが異なる）。
    /// アセットの見た目や音が寿命ごとに違うので、どの寿命のものが残り、どれが自動回収されたかを見分けられる。
    /// </para>
    /// <para>
    /// 【このクラスで確かめられること】
    /// <list type="bullet">
    /// <item>Load（Manual）：Manual 用のアセットを読み込む。2回押しても実ロードは1回だけ（重複ロードの共有）</item>
    /// <item>Release：明示解放。保持中のチケットを1枚ずつ返す。Manual 用のアセットだけが対象</item>
    /// <item>同じチケットを再 Release：二重解放は警告ログだけで例外にならない</item>
    /// <item>Load（Scene 寿命）：Scene 用のアセットを読み込む。チケットを保持しない。別シーンへ遷移すると自動回収され、リーク警告が出る</item>
    /// </list>
    /// </para>
    /// <para>
    /// 【実装例として真似る部分】Load 〜 Release の流れと、<see cref="OnDestroy"/> の後始末。
    /// <c>// [検証用]</c> と書かれた処理は動作確認のためだけのもので、実装では不要。
    /// </para>
    /// </summary>
    public abstract class AssetLoadSampleViewBase<T> : MonoBehaviour where T : UnityEngine.Object
    {
        // [検証用] Console 上で、モジュール本体のログ（[AddressableManager]）とサンプルのログを見分けるためのタイトル
        protected const string LOG_TITLE = "AddressableSample";

        [Header("読み込むアドレス（寿命ごとに別のアセット）")]
        // Inspector のプルダウンから Addressables のアドレスを選べる。文字列の打ち間違いを防げる
        [SerializeField, AddressablePath] private string _manualAddress;
        [SerializeField, AddressablePath] private string _sceneAddress;

        [Header("操作ボタン")]
        // Manual 寿命で Load する（Manual 用アドレスのアセットを読み込み、チケットを保持する）
        [SerializeField] private Button _loadManualButton;
        // 保持中の Manual チケットを1枚 Release する（後に Load したものから順に返す）
        [SerializeField] private Button _releaseButton;
        // [検証用] 直前に Release したチケットをもう一度 Release する（二重解放が安全なことの確認）
        [SerializeField] private Button _releaseAgainButton;
        // Scene 寿命で Load する（Scene 用アドレスのアセットを読み込み、チケットは保持しない。別シーンへ遷移すると自動回収される）
        [SerializeField] private Button _loadSceneButton;

        [Header("状態表示")]
        [SerializeField] private Text _statusText;

        // Manual 寿命で読み込んだチケット。Release するまで保持する必要がある
        private readonly List<AssetTicket<T>> _manualTickets = new();

        // [検証用] 二重解放を試すために、直前に解放したチケットを覚えておく
        private AssetTicket<T> _lastReleasedTicket;
        private bool _hasLastReleasedTicket;
        // [検証用] Scene 寿命で読み込んだ回数と、最後の操作結果（画面表示用）
        private int _sceneLifetimeLoadCount;
        private string _lastResult = "None";

        // ボタンに渡す delegate は Awake で1度だけ作って保持する（購読・解除を対で書くため）
        private UnityAction _onLoadManualClicked;
        private UnityAction _onReleaseClicked;
        private UnityAction _onReleaseAgainClicked;
        private UnityAction _onLoadSceneClicked;

        /// <summary> アセットを読み込んだときの処理（表示・再生・生成など）。kind は読み込んだ寿命（Manual 用か Scene 用か） </summary>
        protected abstract void OnAssetLoaded(T asset, SampleLifetimeKind kind);

        /// <summary>
        /// Manual チケットを Release する直前の処理（生成したインスタンスの破棄など）。
        /// is_last は、これで保持中の Manual チケットが 0 枚になるかどうか。
        /// </summary>
        protected abstract void OnAssetReleased(T asset, bool is_last);

        /// <summary> [検証用] 状態表示の末尾に足す行。派生クラスが、その View だけの情報を出したいときに上書きする </summary>
        protected virtual string CreateExtraStatus() => string.Empty;

        protected virtual void Awake()
        {
            _onLoadManualClicked = OnLoadManualClicked;
            _onReleaseClicked = OnReleaseClicked;
            _onReleaseAgainClicked = OnReleaseAgainClicked;
            _onLoadSceneClicked = OnLoadSceneClicked;

            AddListener(_loadManualButton, _onLoadManualClicked);
            AddListener(_releaseButton, _onReleaseClicked);
            AddListener(_releaseAgainButton, _onReleaseAgainClicked);
            AddListener(_loadSceneButton, _onLoadSceneClicked);

            RefreshStatus();
        }

        protected virtual void OnDestroy()
        {
            // イベント購読は必ず対で解除する
            RemoveListener(_loadManualButton, _onLoadManualClicked);
            RemoveListener(_releaseButton, _onReleaseClicked);
            RemoveListener(_releaseAgainButton, _onReleaseAgainClicked);
            RemoveListener(_loadSceneButton, _onLoadSceneClicked);

            // 【後始末】Manual 寿命のアセットは自動回収されないため、画面を離れるときに必ず Release する。
            // Release を忘れた場合、Manager が破棄されるまでアセットがメモリに残り続ける。
            //
            // ⚠ Play 停止時は Manager が先に破棄されていることがある。その場合は Manager の破棄時に
            //    全アセットが解放済みなので、ここでは何もしない（AccInstance が null かどうかで判断する）。
            var manager = AddressableManager.AccInstance;
            if (manager != null)
            {
                foreach (var ticket in _manualTickets)
                {
                    manager.AccHandler.Release(ticket);
                }
            }
            _manualTickets.Clear();
        }

        private void OnLoadManualClicked() => LoadAndHandle(SampleLifetimeKind.Manual);

        private void OnLoadSceneClicked() => LoadAndHandle(SampleLifetimeKind.Scene);

        // async void はボタンのハンドラのように戻り値を受け取れない入口だけに使い、中は必ず try / catch で囲む
        private async void LoadAndHandle(SampleLifetimeKind kind)
        {
            try
            {
                var handler = GetHandlerOrNull();
                if (handler == null) return;

                var is_manual = kind == SampleLifetimeKind.Manual;

                // 寿命ごとに別のアセットを読み込む。同じアセットを別の寿命で読むと、参照カウントが共有されて
                // 「どの寿命のチケットが残っているか」が見分けにくくなるため、このサンプルでは分けている。
                var key = AssetKey.FromPath(is_manual ? _manualAddress : _sceneAddress);

                // 【寿命の選び方】
                //  Manual      : 自動回収なし。Release を呼ぶまで解放されない。寿命を自分で管理できるときに使う
                //  Scene()     : 呼び出し時点のアクティブシーンがアンロードされたときに自動回収される
                //  Owner(go)   : go が破棄されたときに自動回収される（Prefab シーンで試す）
                // Scene / Owner の自動回収は「Release し忘れたときの安全網」で、リーク警告が出る。
                // 実装では、寿命が分かっているなら Release を呼ぶのが基本。
                var lifetime = is_manual ? AssetLifetime.Manual : AssetLifetime.Scene();

                // 【Load】アセットと解放用チケットが1回の呼び出しで返る。
                // destroyCancellationToken を渡すと、この View が破棄されたとき（画面遷移など）に待機をやめられる。
                // キャンセルで止まるのは待機だけで、内部の実ロードは止まらない。
                // ただしモジュール側で参照カウントが巻き戻されるため、キャンセル時に Release を呼ぶ必要はない。
                var ticket = await handler.LoadAsync<T>(key, lifetime, destroyCancellationToken);

                // ⚠ await の間に View が破棄されていたら、チケットを受け取ったここで返しておく
                if (destroyCancellationToken.IsCancellationRequested)
                {
                    ReleaseSafely(ticket);
                    return;
                }

                if (is_manual)
                {
                    // チケットは Release するまで保持する。無くすと解放できなくなる（Manager の破棄まで残る）
                    _manualTickets.Add(ticket);
                }
                else
                {
                    // [検証用] Scene 寿命のチケットを「あえて保持しない」書き方。
                    // Release しないまま別シーンへ遷移すると、自動回収とリーク警告を確認できる。
                    // ⚠ 実装では、寿命が分かっているアセットのチケットを捨てず、Release すること。
                    _sceneLifetimeLoadCount++;
                }

                // ticket.AccAsset が読み込んだアセット本体。Release した後は使ってはいけない
                OnAssetLoaded(ticket.AccAsset, kind);
                SetResult(is_manual ? "Load (Manual) succeeded" : "Load (Scene lifetime) succeeded. Moving to another scene reclaims it automatically");
            }
            catch (OperationCanceledException)
            {
                // 待機中に View が破棄された。UI はもう無いので、ログだけ出す
                AddressableLogger.Log($"{name}: 待機中にキャンセルされました。", LOG_TITLE);
            }
            catch (Exception ex)
            {
                // アドレス未登録・リトライを使い切った場合は AssetLoadException が届く
                SetResult($"Load failed: {ex.GetType().Name}");
                AddressableLogger.LogException(ex);
            }
        }

        private void OnReleaseClicked()
        {
            var handler = GetHandlerOrNull();
            if (handler == null) return;

            if (_manualTickets.Count == 0)
            {
                SetResult("No Manual ticket to release. Press Load (Manual) first");
                return;
            }

            var last_index = _manualTickets.Count - 1;
            var ticket = _manualTickets[last_index];
            _manualTickets.RemoveAt(last_index);

            // ⚠ アセットを使っているもの（生成したインスタンスなど）は、Release の前に片付ける。
            //    Release で参照カウントが 0 になるとアセットはアンロードされ、使い続けると表示が壊れる。
            OnAssetReleased(ticket.AccAsset, _manualTickets.Count == 0);

            // 【Release】チケットを渡すだけでよい。参照カウントが 0 になったときに、アセットが実際に解放される
            handler.Release(ticket);

            _lastReleasedTicket = ticket;
            _hasLastReleasedTicket = true;
            SetResult($"Released. Remaining Manual tickets: {_manualTickets.Count}");
        }

        // [検証用] 同じチケットをもう一度 Release して、二重解放が安全なことを確認する
        private void OnReleaseAgainClicked()
        {
            var handler = GetHandlerOrNull();
            if (handler == null) return;

            if (!_hasLastReleasedTicket)
            {
                SetResult("Press Release first");
                return;
            }

            // 解放済みのチケットを渡しても、参照カウントは減らない。警告ログが1件出るだけで、例外にはならない
            handler.Release(_lastReleasedTicket);
            SetResult("Released the same ticket again (one warning should appear in the Console)");
        }

        /// <summary> Manager が無ければエラーログを出して null を返す。あれば読み込み・解放の窓口を返す </summary>
        protected static IAddressableHandler GetHandlerOrNull()
        {
            var manager = AddressableManager.AccInstance;
            if (manager == null)
            {
                // Manager はシーン上の AddressableManager Prefab が生成する
                AddressableLogger.LogError("AddressableManager がシーンにありません。AddressableManager Prefab を配置してください。", LOG_TITLE);
                return null;
            }
            return manager.AccHandler;
        }

        private void ReleaseSafely(AssetTicket<T> ticket)
        {
            var manager = AddressableManager.AccInstance;
            if (manager != null) manager.AccHandler.Release(ticket);
        }

        // [検証用] 画面のステータス表示
        protected void SetResult(string result)
        {
            _lastResult = result;
            RefreshStatus();
        }

        // [検証用] 保持数などを画面に出す
        private void RefreshStatus()
        {
            if (_statusText == null) return;
            _statusText.text =
                $"Manual: {_manualAddress}\n" +
                $"  tickets held: {_manualTickets.Count}\n" +
                $"Scene: {_sceneAddress}\n" +
                $"  loaded: {_sceneLifetimeLoadCount}\n" +
                CreateExtraStatus() +
                $"Last action: {_lastResult}";
        }

        protected static void AddListener(Button button, UnityAction action)
        {
            if (button != null) button.onClick.AddListener(action);
        }

        protected static void RemoveListener(Button button, UnityAction action)
        {
            if (button != null) button.onClick.RemoveListener(action);
        }
    }
}
