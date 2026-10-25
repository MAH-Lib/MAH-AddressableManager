using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MAH.AddressableManager.Samples
{
    /// <summary>
    /// 4つのサンプルシーンを行き来するためのボタン。各シーンに1つ置く。
    /// <para>
    /// 遷移は <see cref="LoadSceneMode.Single"/> で行うため、遷移元のシーンはアンロードされる。
    /// このアンロードが <c>AssetLifetime.Scene()</c> で読み込んだアセットの自動回収のきっかけになる
    /// （<c>AddressableManager</c> が <c>SceneManager.sceneUnloaded</c> を中継して回収する）。
    /// </para>
    /// </summary>
    public sealed class SampleSceneNavigatorView : MonoBehaviour
    {
        [Header("遷移ボタン")]
        [SerializeField] private Button _spriteSceneButton;
        [SerializeField] private Button _audioSceneButton;
        [SerializeField] private Button _prefabSceneButton;
        [SerializeField] private Button _labelSceneButton;

        // AddListener に毎回ラムダを渡すと生成が繰り返されるため、Awake で1度だけ作ってフィールドに保持する
        private UnityAction _onSpriteSceneClicked;
        private UnityAction _onAudioSceneClicked;
        private UnityAction _onPrefabSceneClicked;
        private UnityAction _onLabelSceneClicked;

        // 遷移中の二重押し防止
        private bool _isLoading;

        private void Awake()
        {
            _onSpriteSceneClicked = () => LoadSampleScene(SampleSceneNames.SPRITE_SCENE);
            _onAudioSceneClicked = () => LoadSampleScene(SampleSceneNames.AUDIO_SCENE);
            _onPrefabSceneClicked = () => LoadSampleScene(SampleSceneNames.PREFAB_SCENE);
            _onLabelSceneClicked = () => LoadSampleScene(SampleSceneNames.LABEL_SCENE);

            // 現在のシーンへのボタンは押せなくする
            var current_scene_name = SceneManager.GetActiveScene().name;
            SetupButton(_spriteSceneButton, SampleSceneNames.SPRITE_SCENE, current_scene_name, _onSpriteSceneClicked);
            SetupButton(_audioSceneButton, SampleSceneNames.AUDIO_SCENE, current_scene_name, _onAudioSceneClicked);
            SetupButton(_prefabSceneButton, SampleSceneNames.PREFAB_SCENE, current_scene_name, _onPrefabSceneClicked);
            SetupButton(_labelSceneButton, SampleSceneNames.LABEL_SCENE, current_scene_name, _onLabelSceneClicked);
        }

        private void OnDestroy()
        {
            // イベント購読は必ず対で解除する
            RemoveListener(_spriteSceneButton, _onSpriteSceneClicked);
            RemoveListener(_audioSceneButton, _onAudioSceneClicked);
            RemoveListener(_prefabSceneButton, _onPrefabSceneClicked);
            RemoveListener(_labelSceneButton, _onLabelSceneClicked);
        }

        private static void SetupButton(Button button, string scene_name, string current_scene_name, UnityAction action)
        {
            if (button == null) return;
            button.interactable = scene_name != current_scene_name;
            button.onClick.AddListener(action);
        }

        private static void RemoveListener(Button button, UnityAction action)
        {
            if (button != null) button.onClick.RemoveListener(action);
        }

        private void LoadSampleScene(string scene_name)
        {
            if (_isLoading) return;
            _isLoading = true;

            // Single 遷移：遷移元のシーンがアンロードされ、Scene 寿命のアセットが自動回収される。
            // AddressableManager は DontDestroyOnLoad のため、遷移しても残る。
            // ⚠ シーンが Build Settings に無いと null が返る（エラーは Unity 側が Console に出す）
            var operation = SceneManager.LoadSceneAsync(scene_name, LoadSceneMode.Single);
            if (operation == null) _isLoading = false;
        }
    }
}
