namespace Hnsw
{
    using System;

    /// <summary>
    /// Unified storage provider that combines vector node storage, layer assignment
    /// storage, and lifecycle management into a single interface.
    /// Implementations must be thread-safe.
    /// </summary>
    public interface IStorageProvider : IHnswStorage, IHnswLayerStorage, IDisposable
    {
    }
}
