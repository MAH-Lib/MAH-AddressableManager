using System;
using System.Collections.Generic;
using System.Text;
using MAH.AddressableManager.Attribute;
using MAH.AddressableManager.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MAH.AddressableManager.Samples
{
    /// <summary>
    /// Label を指定して、その Label が付いたアセットを「まとめて」読み込み、「まとめて」解放するサンプル（Label 一括ロード）。
    /// <para>
    /// 【Label 一括ロードの要点】
    /// <list type="bullet">
    /// <item>Load のときに型は指定しない。Label にどの型のアセットが入っているかを知らなくても、1回の呼び出しで全件を読み込める</item>
    /// <item>型は、まとめチケット（<see cref="AssetGroupTicket"/>）から取り出すときに指定する（<c>TryGetAsset&lt;T&gt;</c> / <c>GetAssets&lt;T&gt;</c>）。読み込んだ型と完全一致するものだけが返る</item>
    /// <item>解放は、まとめチケットを <c>Release</c> に渡す1回だけ。中身がすべて解放される</item>
    /// </list>
    /// </para>
    /// <para>
    /// 【画面の見方】Prefab シーンと同じく、寿命ごとに行を分ける。上が Manual、その下が Scene。
    /// 1回読み込むごとに、その行へ「画像（Sprite）と Prefab のインスタンス」の組が1つ、右に追加される。
    /// BGM と SE は、Manual 側と Scene 側の両方を、Play BGM / Play SE で鳴らせる。
    /// </para>
    /// <para>
    /// 【このクラスで確かめられること】
    /// <list type="bullet">
    /// <item>Load label (Manual)：Manual Label の全件（Sprite・AudioClip・Prefab）を読み込む。Manual の行に、画像と Prefab のインスタンスが追加される。2回押しても実ロードは共有される</item>
    /// <item>Release group：まとめチケットを1枚ずつ返す。最後に読み込んだ組（画像・インスタンス）が消える。すべて返すと、Manual の音も止まる</item>
    /// <item>Release same group again：二重解放は警告ログだけで例外にならない</item>
    /// <item>Load label (Scene lifetime)：Scene Label の全件を読み込む。Scene の行に追加される。まとめチケットを保持しない。別シーンへ遷移すると自動回収され、リーク警告が<b>まとめチケット1枚につき1件</b>だけ出る</item>
    /// <item>Load missing label：該当するアセットが無い Label。警告が出て、空のまとめチケットが返る（例外にならない）</item>
    /// <item>Play BGM / Play SE：Manual と Scene の読み込みで取り出した AudioClip を再生する</item>
    /// </list>
    /// </para>
    /// <para>
    /// 【実装例として真似る部分】Load 〜 取り出し 〜 Release の流れと、<see cref="OnDestroy"/> の後始末。
    /// <c>// [検証用]</c> と書かれた処理は動作確認のためだけのもので、実装では不要。
    /// </para>
    /// </summary>
    public sealed class LabelLoadSampleView : MonoBehaviour
    {
        // [検証用] Console 上で、モジュール本体のログ（[AddressableManager]）とサンプルのログを見分けるためのタイトル
        private const string LOG_TITLE = "AddressableSample";

        /// <summary>
        /// Label の中から取り出したいアセットのアドレス。型を指定して取り出すために使う。
        /// （Label にどの型が入っているかを知らなくても Load はできる。ここは「取り出したいものが何か」を、サンプルが知っているだけ）
        /// </summary>
        [Serializable]
        private sealed class LabelAddresses
        {
            [SerializeField, AddressablePath] private string _sprite;
            [SerializeField, AddressablePath] private string _prefab;
            [SerializeField, AddressablePath] private string _bgm;
            [SerializeField, AddressablePath] private string _se;

            internal string AccSprite => _sprite;
            internal string AccPrefab => _prefab;
            internal string AccBgm => _bgm;
            internal string AccSe => _se;

            internal LabelAddresses(string sprite, string prefab, string bgm, string se)
            {
                _sprite = sprite;
                _prefab = prefab;
                _bgm = bgm;
                _se = se;
            }
        }

        /// <summary> 1回の読み込みで行に追加された、画像とインスタンスの組 </summary>
        private sealed class LoadedSlot
        {
            internal AssetGroupTicket Group; // Manual のときだけ保持する（Scene は保持しない）
            internal GameObject Instance;
            internal GameObject ImageObject;
        }

        [Header("読み込む Label（寿命ごとに別のアセットの集まり）")]
        [SerializeField, AddressableLabel] private string _manualLabel = "Manual";
        [SerializeField, AddressableLabel] private string _sceneLabel = "Scene";
        // [検証用] 該当するアセットが無い Label。警告が出て、空のまとめチケットが返ることの確認に使う。
        // 存在しない Label をあえて指定するため、AddressableLabel は付けない（付けると Inspector に常に警告が出る）
        [SerializeField] private string _missingLabel = "NoSuchLabel";

        [Header("Label の中から取り出すアセットのアドレス（寿命ごと）")]
        [SerializeField] private LabelAddresses _manualAddresses = new("manual_sprite_load_test", "manual_object_load_test", "manual_audio_load_test_bgm", "manual_audio_load_test_se");
        [SerializeField] private LabelAddresses _sceneAddresses = new("scene_sprite_load_test", "scene_object_load_test", "scene_audio_load_test_bgm", "scene_audio_load_test_se");

        [Header("操作ボタン")]
        // Manual 寿命で Label 一括ロードする（まとめチケットを保持する）
        [SerializeField] private Button _loadManualButton;
        // 保持中の Manual のまとめチケットを1枚 Release する（後に Load したものから順に返す）
        [SerializeField] private Button _releaseButton;
        // [検証用] 直前に Release したまとめチケットをもう一度 Release する（二重解放が安全なことの確認）
        [SerializeField] private Button _releaseAgainButton;
        // Scene 寿命で Label 一括ロードする（まとめチケットは保持しない。別シーンへ遷移すると自動回収される）
        [SerializeField] private Button _loadSceneButton;
        // [検証用] 該当するアセットが無い Label を読み込む
        [SerializeField] private Button _loadMissingButton;
        // 読み込んだ BGM を再生する（Manual と Scene の両方）。Release 後は再生できないことの確認にも使う
        [SerializeField] private Button _playBgmButton;
        // 読み込んだ SE を再生する（Manual と Scene の両方）
        [SerializeField] private Button _playSeButton;

        [Header("表示（寿命ごとの行。1回読み込むごとに、右へ1組ずつ追加する）")]
        // 行の左上（Canvas 上の位置）。ここから右へ、画像と Prefab のインスタンスの組を並べる
        [SerializeField] private RectTransform _manualRow;
        [SerializeField] private RectTransform _sceneRow;
        // Sprite を表示する Image の元。非表示のまま置いておき、読み込むたびに複製する
        [SerializeField] private Image _slotImageTemplate;
        // 1回の読み込みごとに、右へずらす幅（Canvas 上のユニット）
        [SerializeField] private float _slotStep = 260f;
        // 行の左上から、Prefab のインスタンスの中心までのずれ（Canvas 上のユニット。下向きが負）
        [SerializeField] private Vector2 _prefabOffset = new(190f, -125f);
        // ワールド 1 ユニットが、Canvas 上で何ユニットか（Main Camera の SampleCanvasAlignedCameraView と同じ値にする）
        [SerializeField] private float _canvasUnitsPerWorldUnit = 120f;

        [Header("再生先（寿命ごと）")]
        [SerializeField] private AudioSource _manualBgmSource;
        [SerializeField] private AudioSource _manualSeSource;
        [SerializeField] private AudioSource _sceneBgmSource;
        [SerializeField] private AudioSource _sceneSeSource;

        [Header("状態表示")]
        [SerializeField] private Text _statusText;

        // Manual 寿命で読み込んだ分。まとめチケットは Release するまで保持する必要がある。後入れ先出しで返す
        private readonly List<LoadedSlot> _manualSlots = new();

        // [検証用] 二重解放を試すために、直前に解放したまとめチケットを覚えておく
        private AssetGroupTicket _lastReleasedGroup;
        // [検証用] Scene 寿命で読み込んだ回数（行の並び位置にも使う）・寿命ごとの直近の読み込み内容・最後の操作結果（画面表示用）
        private int _sceneLifetimeLoadCount;
        private string _manualLoadedSummary = "-";
        private string _sceneLoadedSummary = "-";
        private string _lastResult = "None";

        // ボタンに渡す delegate は Awake で1度だけ作って保持する（購読・解除を対で書くため）
        private UnityAction _onLoadManualClicked;
        private UnityAction _onReleaseClicked;
        private UnityAction _onReleaseAgainClicked;
        private UnityAction _onLoadSceneClicked;
        private UnityAction _onLoadMissingClicked;
        private UnityAction _onPlayBgmClicked;
        private UnityAction _onPlaySeClicked;

        private void Awake()
        {
            _onLoadManualClicked = () => LoadAndHandle(SampleLifetimeKind.Manual);
            _onReleaseClicked = OnReleaseClicked;
            _onReleaseAgainClicked = OnReleaseAgainClicked;
            _onLoadSceneClicked = () => LoadAndHandle(SampleLifetimeKind.Scene);
            _onLoadMissingClicked = OnLoadMissingClicked;
            _onPlayBgmClicked = () => PlayAudio(_manualBgmSource, _sceneBgmSource, "BGM", restart: false);
            _onPlaySeClicked = () => PlayAudio(_manualSeSource, _sceneSeSource, "SE", restart: true);

            AddListener(_loadManualButton, _onLoadManualClicked);
            AddListener(_releaseButton, _onReleaseClicked);
            AddListener(_releaseAgainButton, _onReleaseAgainClicked);
            AddListener(_loadSceneButton, _onLoadSceneClicked);
            AddListener(_loadMissingButton, _onLoadMissingClicked);
            AddListener(_playBgmButton, _onPlayBgmClicked);
            AddListener(_playSeButton, _onPlaySeClicked);

            // 元の Image は、複製するための見本。読み込むまで（複製が出るまで）は隠しておく
            if (_slotImageTemplate != null) _slotImageTemplate.gameObject.SetActive(false);
            RefreshStatus();
        }

        private void OnDestroy()
        {
            // イベント購読は必ず対で解除する
            RemoveListener(_loadManualButton, _onLoadManualClicked);
            RemoveListener(_releaseButton, _onReleaseClicked);
            RemoveListener(_releaseAgainButton, _onReleaseAgainClicked);
            RemoveListener(_loadSceneButton, _onLoadSceneClicked);
            RemoveListener(_loadMissingButton, _onLoadMissingClicked);
            RemoveListener(_playBgmButton, _onPlayBgmClicked);
            RemoveListener(_playSeButton, _onPlaySeClicked);

            // 【後始末】Manual 寿命のアセットは自動回収されないため、画面を離れるときに必ず Release する。
            // Release を忘れた場合、Manager が破棄されるまでアセットがメモリに残り続ける。
            // 生成したインスタンスは、このシーンのアンロードで一緒に破棄される。
            //
            // ⚠ Play 停止時は Manager が先に破棄されていることがある。その場合は Manager の破棄時に
            //    全アセットが解放済みなので、ここでは何もしない（AccInstance が null かどうかで判断する）。
            var manager = AddressableManager.AccInstance;
            if (manager != null)
            {
                foreach (var slot in _manualSlots)
                {
                    manager.AccHandler.Release(slot.Group);
                }
            }
            _manualSlots.Clear();
        }

        // async void はボタンのハンドラのように戻り値を受け取れない入口だけに使い、中は必ず try / catch で囲む
        private async void LoadAndHandle(SampleLifetimeKind kind)
        {
            try
            {
                var handler = GetHandlerOrNull();
                if (handler == null) return;

                var is_manual = kind == SampleLifetimeKind.Manual;
                var label = is_manual ? _manualLabel : _sceneLabel;

                // 【寿命の選び方】1件ずつの Load と同じ。Manual は Release を呼ぶまで解放されない。
                // Scene() は、呼び出し時点のアクティブシーンがアンロードされたときに、全件がまとめて自動回収される（セーフティネット）
                var lifetime = is_manual ? AssetLifetime.Manual : AssetLifetime.Scene();

                // 【Label 一括ロード】Label に入っている型を知らなくても、1回の呼び出しで全件を読み込める。
                // 返るのは、中身をまとめて持つ「まとめチケット」。Load のときに型は指定しない。
                // destroyCancellationToken を渡すと、この View が破棄されたとき（画面遷移など）に待機をやめられる。
                // キャンセル・失敗のときは、モジュール側で全件が巻き戻されるため、呼び出し側で Release を呼ぶ必要はない。
                var group = await handler.LoadByLabelAsync(label, lifetime, destroyCancellationToken);

                // ⚠ await の間に View が破棄されていたら、まとめチケットを受け取ったここで返しておく
                if (destroyCancellationToken.IsCancellationRequested)
                {
                    ReleaseSafely(group);
                    return;
                }

                var slot = UseAssets(group, kind);
                if (is_manual)
                {
                    // まとめチケットは Release するまで保持する。無くすと解放できなくなる（Manager の破棄まで残る）
                    slot.Group = group;
                    _manualSlots.Add(slot);
                }
                else
                {
                    // [検証用] Scene 寿命のまとめチケットを「あえて保持しない」書き方。
                    // Release しないまま別シーンへ遷移すると、自動回収とリーク警告（まとめチケット1枚につき1件）を確認できる。
                    // ⚠ 実装では、寿命が分かっているアセットのまとめチケットを捨てず、Release すること。
                    _sceneLifetimeLoadCount++;
                }

                RefreshStatus();
                SetResult(is_manual
                    ? "Load label (Manual) succeeded"
                    : "Load label (Scene lifetime) succeeded. Moving to another scene reclaims it automatically");
            }
            catch (OperationCanceledException)
            {
                // 待機中に View が破棄された。UI はもう無いので、ログだけ出す
                AddressableLogger.Log($"{name}: 待機中にキャンセルされました。", LOG_TITLE);
            }
            catch (Exception ex)
            {
                // Label のロケーションの解決に失敗した場合・リトライを使い切った場合は AssetLoadException が届く
                SetResult($"Load failed: {ex.GetType().Name}");
                AddressableLogger.LogException(ex);
            }
        }

        /// <summary>
        /// 【型は取り出すときに指定する】まとめチケットから、アドレスと型を指定して、使いたいアセットを取り出して使う。
        /// 読み込んだ型と完全一致するものだけが返る（Object を指定しても、何も返らない）。
        /// 画像は Image に、Prefab は Instantiate して、音は AudioSource に使う。
        /// </summary>
        private LoadedSlot UseAssets(AssetGroupTicket group, SampleLifetimeKind kind)
        {
            var is_manual = kind == SampleLifetimeKind.Manual;
            var addresses = is_manual ? _manualAddresses : _sceneAddresses;
            var row = is_manual ? _manualRow : _sceneRow;
            var index = is_manual ? _manualSlots.Count : _sceneLifetimeLoadCount; // 行の何組目か（右へ並べていく）
            var slot = new LoadedSlot();

            // 画像は Texture2D と Sprite の2件として読み込まれる。Image に使えるのは Sprite
            if (group.TryGetAsset<Sprite>(addresses.AccSprite, out var sprite))
            {
                slot.ImageObject = CreateSlotImage(sprite, row, index);
            }

            // LoadAsync<GameObject> と同じく、返るのは Prefab アセット本体。シーンに出すには Instantiate が必要。
            // 生成したインスタンスは、Manager の管理対象外（Release しても消えない）。Release の前に、実装者が Destroy する
            if (group.TryGetAsset<GameObject>(addresses.AccPrefab, out var prefab))
            {
                slot.Instance = Instantiate(prefab, CreatePrefabPosition(row, index), Quaternion.identity);
            }

            // 音は、BGM（ループ）と SE（1回だけ）を、別の AudioSource に割り当てる。再生は Play BGM / Play SE ボタンで行う
            var bgm_source = is_manual ? _manualBgmSource : _sceneBgmSource;
            var se_source = is_manual ? _manualSeSource : _sceneSeSource;
            if (group.TryGetAsset<AudioClip>(addresses.AccBgm, out var bgm) && bgm_source != null)
            {
                bgm_source.clip = bgm;
                bgm_source.loop = true;
            }
            if (group.TryGetAsset<AudioClip>(addresses.AccSe, out var se) && se_source != null)
            {
                se_source.clip = se;
                se_source.loop = false;
            }

            // [検証用] 読み込んだ内容を画面に出す。型を指定して取り出した件数を数える。
            // IReadOnlyList の foreach はボックス化されるため、件数は Count で数える
            var sprites = group.GetAssets<Sprite>();
            var textures = group.GetAssets<Texture2D>();
            var clips = group.GetAssets<AudioClip>();
            var objects = group.GetAssets<GameObject>();
            var summary = $"{group.AccCount} items (Sprite {sprites.Count}, Texture2D {textures.Count}, AudioClip {clips.Count}, GameObject {objects.Count})";
            if (is_manual) _manualLoadedSummary = summary;
            else _sceneLoadedSummary = summary;
            return slot;
        }

        private GameObject CreateSlotImage(Sprite sprite, RectTransform row, int index)
        {
            if (_slotImageTemplate == null || row == null) return null;

            var image = Instantiate(_slotImageTemplate, _slotImageTemplate.transform.parent);
            image.gameObject.SetActive(true);
            image.sprite = sprite;
            image.enabled = true;
            var rect = image.rectTransform;
            rect.anchoredPosition = row.anchoredPosition + new Vector2(index * _slotStep, 0f);
            return image.gameObject;
        }

        // Canvas 上の位置を、ワールドの位置に直す（Main Camera の SampleCanvasAlignedCameraView が、Canvas の左上をワールドの原点に合わせている）
        private Vector3 CreatePrefabPosition(RectTransform row, int index)
        {
            if (row == null) return Vector3.zero;

            var canvas_position = row.anchoredPosition + new Vector2(index * _slotStep, 0f) + _prefabOffset;
            return new Vector3(canvas_position.x, canvas_position.y, 0f) / _canvasUnitsPerWorldUnit;
        }

        private void OnReleaseClicked()
        {
            var handler = GetHandlerOrNull();
            if (handler == null) return;

            if (_manualSlots.Count == 0)
            {
                SetResult("No Manual group to release. Press Load label (Manual) first");
                return;
            }

            var last_index = _manualSlots.Count - 1;
            var slot = _manualSlots[last_index];
            _manualSlots.RemoveAt(last_index);

            // ⚠ アセットを使っているもの（画像・生成したインスタンス・AudioSource など）は、Release の前に片付ける。
            //    先にアセットが解放されると、インスタンスのメッシュやマテリアルが失われて表示が壊れる。
            //    この組の画像とインスタンスは、他の組とは別のコピーなので、保持中の他のまとめチケットがあっても片付けてよい。
            if (slot.ImageObject != null) Destroy(slot.ImageObject);
            if (slot.Instance != null) Destroy(slot.Instance);

            // 最後のまとめチケットを Release すると、中身のアセットがアンロードされる。
            // AudioSource が参照したままだと、解放済みのアセットを再生することになるため、止めて参照を外す。
            // 保持中のまとめチケットが残っている間は、同じアセットがまだ有効なので、そのままにする
            if (_manualSlots.Count == 0)
            {
                StopAndClear(_manualBgmSource);
                StopAndClear(_manualSeSource);
                _manualLoadedSummary = "-";
            }

            // 【Release】まとめチケットを渡すだけでよい。中身のアセットが、1件ずつ解放される
            handler.Release(slot.Group);

            _lastReleasedGroup = slot.Group;
            RefreshStatus();
            SetResult($"Released. Remaining Manual groups: {_manualSlots.Count}");
        }

        // [検証用] 同じまとめチケットをもう一度 Release して、二重解放が安全なことを確認する
        private void OnReleaseAgainClicked()
        {
            var handler = GetHandlerOrNull();
            if (handler == null) return;

            if (_lastReleasedGroup == null)
            {
                SetResult("Press Release group first");
                return;
            }

            // 解放済みのまとめチケットを渡しても、参照カウントは減らない。警告ログが1件出るだけで、例外にはならない
            handler.Release(_lastReleasedGroup);
            SetResult("Released the same group again (one warning should appear in the Console)");
        }

        // [検証用] 該当するアセットが無い Label を読み込む。例外にならず、警告が出て、空のまとめチケットが返る
        private async void OnLoadMissingClicked()
        {
            try
            {
                var handler = GetHandlerOrNull();
                if (handler == null) return;

                var group = await handler.LoadByLabelAsync(_missingLabel, AssetLifetime.Manual, destroyCancellationToken);
                SetResult($"Label \"{_missingLabel}\": {group.AccCount} items (one warning should appear in the Console)");

                // 空のまとめチケットも、Release で解放できる（保持する必要はない）
                handler.Release(group);
            }
            catch (OperationCanceledException)
            {
                AddressableLogger.Log($"{name}: 待機中にキャンセルされました。", LOG_TITLE);
            }
            catch (Exception ex)
            {
                SetResult($"Load failed: {ex.GetType().Name}");
                AddressableLogger.LogException(ex);
            }
        }

        /// <summary>
        /// 読み込んだ AudioClip を、Manual と Scene の両方で再生する。BGM は再生中なら何もしない（ループのため）。SE は押すたびに鳴らす。
        /// 読み込まれていない側は鳴らない。
        /// </summary>
        private void PlayAudio(AudioSource manual_source, AudioSource scene_source, string kind_name, bool restart)
        {
            var played = new StringBuilder();
            AppendIfPlayed(played, "Manual", manual_source, restart);
            AppendIfPlayed(played, "Scene", scene_source, restart);

            SetResult(played.Length == 0
                ? $"No {kind_name} loaded. Press Load label (Manual / Scene lifetime) first"
                : $"{kind_name}: {played}");
        }

        private static void AppendIfPlayed(StringBuilder builder, string lifetime_name, AudioSource source, bool restart)
        {
            if (source == null || source.clip == null) return;

            // 再生中の BGM を押し直しても、最初からやり直さない
            var already_playing = !restart && source.isPlaying;
            if (!already_playing) source.Play();

            if (builder.Length > 0) builder.Append(" / ");
            builder.Append($"[{lifetime_name}] {source.clip.name}{(already_playing ? " (playing)" : string.Empty)}");
        }

        private static void StopAndClear(AudioSource source)
        {
            if (source == null) return;
            source.Stop();
            source.clip = null;
        }

        /// <summary> Manager が無ければエラーログを出して null を返す。あれば読み込み・解放の窓口を返す </summary>
        private static IAddressableHandler GetHandlerOrNull()
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

        private static void ReleaseSafely(AssetGroupTicket group)
        {
            var manager = AddressableManager.AccInstance;
            if (manager != null) manager.AccHandler.Release(group);
        }

        // [検証用] 画面のステータス表示
        private void SetResult(string result)
        {
            _lastResult = result;
            RefreshStatus();
        }

        // [検証用] 保持数などを画面に出す
        private void RefreshStatus()
        {
            if (_statusText == null) return;
            _statusText.text =
                $"Manual label: {_manualLabel}\n" +
                $"  groups held: {_manualSlots.Count}\n" +
                $"  last loaded: {_manualLoadedSummary}\n" +
                $"Scene label: {_sceneLabel}\n" +
                $"  loaded (not held): {_sceneLifetimeLoadCount}\n" +
                $"  last loaded: {_sceneLoadedSummary}\n" +
                $"Last action: {_lastResult}";
        }

        private static void AddListener(Button button, UnityAction action)
        {
            if (button != null) button.onClick.AddListener(action);
        }

        private static void RemoveListener(Button button, UnityAction action)
        {
            if (button != null) button.onClick.RemoveListener(action);
        }
    }
}
