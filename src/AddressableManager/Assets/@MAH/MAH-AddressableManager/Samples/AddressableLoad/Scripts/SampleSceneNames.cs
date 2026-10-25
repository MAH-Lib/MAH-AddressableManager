namespace MAH.AddressableManager.Samples
{
    /// <summary>
    /// サンプルの4シーン名。Build Settings に登録したシーン名と一致させる。
    /// </summary>
    public static class SampleSceneNames
    {
        /// <summary> 画像（Sprite）の読み込みを試すシーン </summary>
        public const string SPRITE_SCENE = "SpriteLoadScene";
        /// <summary> 音（AudioClip）の読み込みを試すシーン </summary>
        public const string AUDIO_SCENE = "AudioLoadScene";
        /// <summary> GameObject（Prefab）の読み込みを試すシーン </summary>
        public const string PREFAB_SCENE = "PrefabLoadScene";
        /// <summary> Label を指定して、まとめて読み込み・解放（Label 一括ロード）を試すシーン </summary>
        public const string LABEL_SCENE = "LabelLoadScene";
    }
}
