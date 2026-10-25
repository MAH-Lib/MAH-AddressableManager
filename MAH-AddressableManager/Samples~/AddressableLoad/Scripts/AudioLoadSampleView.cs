using UnityEngine;

namespace MAH.AddressableManager.Samples
{
    /// <summary>
    /// 音（AudioClip）を読み込んで再生するサンプル。
    /// 基本の Load → Release の流れは <see cref="AssetLoadSampleViewBase{T}"/> を参照。
    /// このクラスは「読み込んだ AudioClip をどう使うか」だけを書いている。
    /// <para>
    /// 寿命ごとに別の音を読み込み、別の AudioSource で再生する。
    /// Release すると Manual 側の音だけが止まり、Scene 側の音は別シーンへ遷移するまで鳴り続ける。
    /// </para>
    /// </summary>
    public sealed class AudioLoadSampleView : AssetLoadSampleViewBase<AudioClip>
    {
        [Header("再生先（寿命ごと）")]
        [SerializeField] private AudioSource _manualAudioSource;
        [SerializeField] private AudioSource _sceneAudioSource;

        [Header("再生設定")]
        // BGM はループ、SE は1回だけ鳴らす。同じシーンに別アドレスの View を並べて、アセットごとに独立して読み込める
        [SerializeField] private bool _loop = true;

        protected override void OnAssetLoaded(AudioClip asset, SampleLifetimeKind kind)
        {
            var source = kind == SampleLifetimeKind.Manual ? _manualAudioSource : _sceneAudioSource;
            if (source == null) return;

            // 読み込んだ AudioClip を AudioSource に割り当てて再生する。
            // 2回目以降の Load では同じアセットが返るため、再生中なら何もしない
            // （ループしない SE は鳴り終わっていれば、Load のたびにもう一度鳴る）
            if (source.clip == asset && source.isPlaying) return;

            source.clip = asset;
            source.loop = _loop;
            source.Play();
        }

        protected override void OnAssetReleased(AudioClip asset, bool is_last)
        {
            // 保持中のチケットが残っている間は、アセットはまだ有効なので再生を続ける
            if (!is_last || _manualAudioSource == null) return;

            // ⚠ 最後のチケットを Release するとアセットがアンロードされる。
            //    再生中の AudioSource が参照したままだと、解放済みのアセットを再生し続けることになる。
            //    そのため Release の「前に」停止して参照を外す（基底クラスが Release の直前に呼ぶ）。
            // Release の対象は Manual 用のアセットだけなので、Scene 側の AudioSource は触らない
            _manualAudioSource.Stop();
            _manualAudioSource.clip = null;
        }
    }
}
