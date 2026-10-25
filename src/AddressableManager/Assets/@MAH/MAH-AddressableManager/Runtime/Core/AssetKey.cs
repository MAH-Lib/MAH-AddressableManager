using System;
using UnityEngine.AddressableAssets;

namespace MAH.AddressableManager
{
    /// <summary>
    /// Path入口とAssetReference入口を両立させるための複合キー。
    /// 「入口の種別＋値」が一致した場合のみ同一とみなす（完全なロケーション解決による同一判定は行わない）。
    /// </summary>
    public readonly struct AssetKey : IEquatable<AssetKey>
    {
        /// <summary> キーの入口の種別 </summary>
        public enum Kind
        {
            Path,
            Reference,
        }

        private readonly Kind _kind;
        private readonly string _value;

        /// <summary> 入口の種別 </summary>
        public Kind AccKind => _kind;
        /// <summary> 入口の値（Pathならアドレス文字列、ReferenceならRuntimeKeyの文字列） </summary>
        public string AccValue => _value;

        private AssetKey(Kind kind, string value)
        {
            _kind = kind;
            _value = value;
        }

        /// <summary> Addressableのアドレス文字列からキーを作る </summary>
        public static AssetKey FromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("アドレスが null または空文字です。", nameof(path));

            return new AssetKey(Kind.Path, path);
        }

        /// <summary> AssetReferenceからキーを作る。RuntimeKeyの時点で文字列化するため、Service側は入口の違いを意識しない </summary>
        public static AssetKey FromReference(AssetReference reference)
        {
            if (reference == null)
                throw new ArgumentException("参照が null です。", nameof(reference));
            if (!reference.RuntimeKeyIsValid())
                throw new ArgumentException("参照が無効です（RuntimeKeyIsValid() が false）。", nameof(reference));

            return new AssetKey(Kind.Reference, reference.RuntimeKey.ToString());
        }

        public bool Equals(AssetKey other) => _kind == other._kind && _value == other._value;

        public override bool Equals(object obj) => obj is AssetKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_kind, _value);

        public override string ToString() => $"{_kind}:{_value}";

        public static bool operator ==(AssetKey left, AssetKey right) => left.Equals(right);

        public static bool operator !=(AssetKey left, AssetKey right) => !left.Equals(right);
    }
}
