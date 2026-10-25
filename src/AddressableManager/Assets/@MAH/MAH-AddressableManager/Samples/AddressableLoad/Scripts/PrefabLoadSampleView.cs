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
    /// GameObject（Prefab）を読み込んで画面に生成するサンプル。
    /// 基本の Load → Release の流れは <see cref="AssetLoadSampleViewBase{T}"/> を参照。
    /// <para>
    /// 【重要】<c>LoadAsync&lt;GameObject&gt;</c> が返すのは Prefab「アセット本体」であり、シーン上のインスタンスではない。
    /// インスタンスの生成（<c>Instantiate</c>）と破棄（<c>Destroy</c>）は、アセットの Load / Release とは別に、実装者が行う。
    /// </para>
    /// <para>
    /// 寿命ごとに別の Prefab を読み込む。Prefab の体の色が寿命ごとに違い、置く場所も行ごとに分けているので、
    /// どの寿命のものが残り、どれが自動回収されたかを見分けられる。上から順に次の行に並ぶ（同じ寿命が複数あれば横に並ぶ）。
    /// <list type="number">
    /// <item>owner（白）：Owner 寿命の持ち主。1つだけ</item>
    /// <item>Owner 寿命（緑）：owner の1つ下の行。owner を Destroy すると一緒に消える</item>
    /// <item>Scene 寿命（水色）：その1つ下の行。別シーンへ遷移すると消える</item>
    /// <item>Manual 寿命（黄）：その1つ下の行。Release を押すと消える</item>
    /// </list>
    /// </para>
    /// <para>
    /// 基底クラスの操作に加えて、Owner 寿命を試すボタンを持つ。
    /// Owner 寿命は「指定した GameObject が破棄されたときに自動回収する」もので、
    /// 生成したインスタンスを owner の子にしておくと、インスタンスとアセットの寿命を揃えられる。
    /// </para>
    /// </summary>
    public sealed class PrefabLoadSampleView : AssetLoadSampleViewBase<GameObject>
    {
        [Header("配置（Prefab の大きさに合わせて調整する）")]
        // 同じ寿命のインスタンスを横に並べる間隔
        [SerializeField] private float _spawnInterval = 1.5f;
        // Owner 寿命のインスタンスを、owner（白）から見てどれだけ下の行に置くか。負の値で下になる
        // owner の子にしておくので、owner を Destroy すると一緒に消える
        [SerializeField] private float _ownerLifetimeRowOffsetY = -2.5f;

        [Header("Owner 寿命で読み込むアドレス")]
        [SerializeField, AddressablePath] private string _ownerAddress;

        [Header("owner の見た目（Addressables には登録しない）")]
        [SerializeField] private GameObject _ownerObjectPrefab;

        [Header("生成先（寿命ごと）")]
        [SerializeField] private Transform _manualRoot;
        [SerializeField] private Transform _sceneRoot;
        [SerializeField] private Transform _ownerRoot;

        [Header("Owner 寿命のボタン")]
        // Owner 寿命で Load する（owner を生成し、Owner 用アドレスのアセットを読み込む。チケットは保持しない）
        [SerializeField] private Button _loadOwnerButton;
        // owner を Destroy する（読み込んだアセットが自動回収され、リーク警告が出る）
        [SerializeField] private Button _destroyOwnerButton;

        // Manual 寿命で生成したインスタンス。チケットと同じ順（後入れ先出し）で破棄する
        private readonly Stack<GameObject> _manualInstances = new();

        // Owner 寿命の検証用。この GameObject を Destroy すると、読み込んだアセットが自動回収される。
        // Load (Owner lifetime) は何回でも押せる。2回目以降は同じ owner を使い回し、1つの owner に複数のチケットが紐づく
        private GameObject _owner;
        // 現在の owner の下に生成したインスタンスの数（[検証用] の表示と、配置位置のずらしに使う）
        private int _ownerInstanceCount;

        private UnityAction _onLoadOwnerClicked;
        private UnityAction _onDestroyOwnerClicked;

        protected override void Awake()
        {
            base.Awake();

            _onLoadOwnerClicked = OnLoadOwnerClicked;
            _onDestroyOwnerClicked = OnDestroyOwnerClicked;
            AddListener(_loadOwnerButton, _onLoadOwnerClicked);
            AddListener(_destroyOwnerButton, _onDestroyOwnerClicked);
        }

        protected override void OnDestroy()
        {
            RemoveListener(_loadOwnerButton, _onLoadOwnerClicked);
            RemoveListener(_destroyOwnerButton, _onDestroyOwnerClicked);
            base.OnDestroy();
        }

        // [検証用] 状態表示に、Owner 寿命の情報を足す
        protected override string CreateExtraStatus()
        {
            return $"Owner: {_ownerAddress}\n" +
                   $"  owner exists: {_owner != null}  instances: {_ownerInstanceCount}\n";
        }

        protected override void OnAssetLoaded(GameObject asset, SampleLifetimeKind kind)
        {
            var is_manual = kind == SampleLifetimeKind.Manual;
            var root = is_manual ? _manualRoot : _sceneRoot;

            // asset は Prefab アセットそのもの。シーンに出すには Instantiate が必要
            var instance = Instantiate(asset, root);
            instance.transform.localPosition = CreateSpawnPosition(root);

            // Manual 寿命の分は、Release の前に破棄できるよう覚えておく。
            // Scene 寿命の分は、シーンのアンロードでインスタンスもアセットも一緒に片付くため覚えなくてよい
            if (is_manual) _manualInstances.Push(instance);
        }

        protected override void OnAssetReleased(GameObject asset, bool is_last)
        {
            if (_manualInstances.Count == 0) return;

            // ⚠ アセットを Release する「前に」、そのアセットから生成したインスタンスを破棄する。
            //    先にアセットが解放されると、インスタンスのメッシュやマテリアルが失われて表示が壊れる。
            //    （基底クラスが Release の直前に呼ぶため、ここで破棄すれば順序が守られる）
            Destroy(_manualInstances.Pop());
        }

        private void OnLoadOwnerClicked() => LoadWithOwnerAndHandle();

        // async void はボタンのハンドラのように戻り値を受け取れない入口だけに使い、中は必ず try / catch で囲む
        private async void LoadWithOwnerAndHandle()
        {
            try
            {
                var handler = GetHandlerOrNull();
                if (handler == null) return;

                // 【Owner 寿命】この GameObject が Destroy されたとき、読み込んだアセットが自動回収される。
                // 「このオブジェクトが生きている間だけ使うアセット」を読み込むときに向いている。
                // 生成したインスタンスを owner の子にしておくと、owner の破棄でインスタンスも一緒に消える。
                // owner の見た目には、Addressables で読み込まない普通の Prefab（白い体）を使う。
                //
                // 同じ owner に対して何回でも Load できる。2回目以降は既にある owner を使い回すので、
                // 1つの owner に複数のチケットが紐づき、owner を Destroy すると全部まとめて自動回収される。
                var created_here = _owner == null;
                if (created_here)
                {
                    _owner = _ownerObjectPrefab != null ? Instantiate(_ownerObjectPrefab, _ownerRoot) : new GameObject("PrefabOwner");
                    _owner.transform.SetParent(_ownerRoot, false);
                    _owner.transform.localPosition = CreateSpawnPosition(_ownerRoot);
                }
                var owner = _owner;

                var key = AssetKey.FromPath(_ownerAddress);
                var ticket = await handler.LoadAsync<GameObject>(key, AssetLifetime.Owner(owner), destroyCancellationToken);

                // ロード中に owner が破棄された場合は、LoadAsync が OperationCanceledException を投げるため、ここには来ない。
                // 念のため、Load を始めたときの owner が無くなっていたら何もしない
                if (owner == null) return;

                // owner の1つ下の行に、横へずらして並べる（複数 Load したときに重ならないようにするための [検証用] の処理）
                var instance = Instantiate(ticket.AccAsset, owner.transform);
                instance.transform.localPosition = new Vector3(_ownerInstanceCount * _spawnInterval, _ownerLifetimeRowOffsetY, 0f);
                _ownerInstanceCount++;

                // [検証用] Owner 寿命のチケットを「あえて保持しない」書き方。
                // owner を Destroy すると、Release されていないアセットが自動回収され、リーク警告が出る。
                // ⚠ 実装では、owner の破棄より前に解放できるなら、チケットを保持して Release すること。
                SetResult($"Load (Owner lifetime) succeeded. Instances under the owner: {_ownerInstanceCount}. \"Destroy owner\" reclaims all of them at once");
            }
            catch (OperationCanceledException)
            {
                // 待機中に View や owner が破棄された。UI はもう無いので、ログだけ出す
                AddressableLogger.Log($"{name}: 待機中にキャンセルされました。", LOG_TITLE);
            }
            catch (Exception ex)
            {
                // 失敗したとき、空の owner だけが残らないように破棄する（既にインスタンスが載っている owner は残す）
                if (_owner != null && _ownerInstanceCount == 0)
                {
                    Destroy(_owner);
                    _owner = null;
                }
                SetResult($"Load failed: {ex.GetType().Name}");
                AddressableLogger.LogException(ex);
            }
        }

        private void OnDestroyOwnerClicked()
        {
            if (_owner == null)
            {
                SetResult("No owner. Press Load (Owner lifetime) first");
                return;
            }

            // owner を破棄すると、子のインスタンスも全部消える。
            // アセット側は、Watcher が破棄を検知して、この owner に紐づく未解放のチケットをまとめて自動回収する
            // （次のフレームまでに回収される。チケット1枚ごとにリーク警告が出る）
            var destroyed_count = _ownerInstanceCount;
            Destroy(_owner);
            _owner = null;
            _ownerInstanceCount = 0;
            SetResult($"Destroyed the owner with {destroyed_count} instance(s) (one leak warning per ticket should appear in the Console)");
        }

        // 生成位置を、その寿命の置き場に既にある子の数だけ横にずらす（重ならないように並べるための [検証用] の処理）
        private Vector3 CreateSpawnPosition(Transform root)
        {
            var count = root != null ? root.childCount : 0;
            return new Vector3(count * _spawnInterval, 0f, 0f);
        }
    }
}
