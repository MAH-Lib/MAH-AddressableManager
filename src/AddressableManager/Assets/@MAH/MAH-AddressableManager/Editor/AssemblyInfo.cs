using System.Runtime.CompilerServices;

// internal な設定クラスをテストから直接検証できるようにする。公開する相手はテスト用アセンブリのみ。
[assembly: InternalsVisibleTo("MAH.AddressableManager.Editor.Tests")]
