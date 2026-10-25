using System;

namespace MAH.AddressableManager
{
    /// <summary>
    /// Addressables の初期化に失敗したことを示す例外。
    /// 初期化に失敗するとアプリを再起動するまで Addressables 全体が使えないため、利用者は再起動を促す等の対応をする。
    /// Addressables が出した例外（OperationException、または InitializeAsync が同期で投げた例外）は InnerException に入る。
    /// </summary>
    public sealed class AddressableInitializeException : Exception
    {
        public AddressableInitializeException(string message, Exception inner_exception)
            : base(message, inner_exception)
        {
        }
    }
}
