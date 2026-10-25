using System;
using System.Collections.Generic;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace MAH.AddressableManager.Tests.Editor
{
    /// <summary>
    /// IResourceLocation の偽実装。InternalId だけを持つ。
    /// ResourceLocationBase は空・null の id を受け付けない（ArgumentNullException）ため、空・null の InternalId を渡すテストで使う。
    /// </summary>
    internal sealed class FakeResourceLocation : IResourceLocation
    {
        public string InternalId { get; }
        public string ProviderId => "fake-provider";
        public IList<IResourceLocation> Dependencies => Array.Empty<IResourceLocation>();
        public int DependencyHashCode => 0;
        public bool HasDependencies => false;
        public object Data => null;
        public string PrimaryKey => "fake";
        public Type ResourceType => typeof(object);

        internal FakeResourceLocation(string internal_id)
        {
            InternalId = internal_id;
        }

        public int Hash(Type result_type) => 0;
    }
}
