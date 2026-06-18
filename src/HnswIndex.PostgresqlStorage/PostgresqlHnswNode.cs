namespace HnswIndex.PostgresqlStorage
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hnsw;

    /// <summary>
    /// PostgreSQL-backed HNSW node.
    /// </summary>
    public sealed class PostgresqlHnswNode : IHnswNode
    {
        private readonly PostgresqlStorageProvider _Provider;
        private readonly Dictionary<int, HashSet<Guid>> _Neighbors;
        private readonly List<float> _Vector;
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private string? _Name;
        private List<string>? _Labels;
        private Dictionary<string, object>? _Tags;

        internal PostgresqlHnswNode(
            PostgresqlStorageProvider provider,
            Guid id,
            List<float> vector,
            Dictionary<int, HashSet<Guid>> neighbors,
            string? name,
            List<string>? labels,
            Dictionary<string, object>? tags)
        {
            _Provider = provider;
            Id = id;
            _Vector = new List<float>(vector);
            _Neighbors = neighbors;
            _Name = name;
            _Labels = labels == null ? null : new List<string>(labels);
            _Tags = tags == null ? null : new Dictionary<string, object>(tags);
        }

        /// <inheritdoc />
        public Guid Id { get; }

        /// <inheritdoc />
        public IReadOnlyList<float> Vector => _Vector;

        /// <inheritdoc />
        public string? Name => _Name;

        /// <inheritdoc />
        public IReadOnlyList<string>? Labels => _Labels;

        /// <inheritdoc />
        public IReadOnlyDictionary<string, object>? Tags => _Tags;

        /// <inheritdoc />
        public async Task<Dictionary<int, HashSet<Guid>>> GetNeighborsAsync(CancellationToken cancellationToken = default)
        {
            await _Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Dictionary<int, HashSet<Guid>> copy = new Dictionary<int, HashSet<Guid>>();
                foreach (KeyValuePair<int, HashSet<Guid>> kvp in _Neighbors)
                {
                    copy[kvp.Key] = new HashSet<Guid>(kvp.Value);
                }
                return copy;
            }
            finally
            {
                _Lock.Release();
            }
        }

        /// <inheritdoc />
        public async Task AddNeighborAsync(int layer, Guid neighborGuid, CancellationToken cancellationToken = default)
        {
            ValidateNeighbor(layer, neighborGuid);

            bool added = false;
            await _Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_Neighbors.TryGetValue(layer, out HashSet<Guid>? neighbors))
                {
                    neighbors = new HashSet<Guid>();
                    _Neighbors[layer] = neighbors;
                }
                added = neighbors.Add(neighborGuid);
            }
            finally
            {
                _Lock.Release();
            }

            if (added)
            {
                await _Provider.UpsertNeighborAsync(Id, layer, neighborGuid, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task RemoveNeighborAsync(int layer, Guid neighborGuid, CancellationToken cancellationToken = default)
        {
            if (layer < 0 || layer > 63) throw new ArgumentOutOfRangeException(nameof(layer), "Layer must be between 0 and 63.");

            bool removed = false;
            await _Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_Neighbors.TryGetValue(layer, out HashSet<Guid>? neighbors))
                {
                    removed = neighbors.Remove(neighborGuid);
                    if (neighbors.Count == 0)
                    {
                        _Neighbors.Remove(layer);
                    }
                }
            }
            finally
            {
                _Lock.Release();
            }

            if (removed)
            {
                await _Provider.DeleteNeighborAsync(Id, layer, neighborGuid, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task SetMetadataAsync(string? name, List<string>? labels, Dictionary<string, object>? tags, CancellationToken cancellationToken = default)
        {
            await _Provider.UpdateMetadataAsync(Id, name, labels, tags, cancellationToken).ConfigureAwait(false);
            await _Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _Name = name;
                _Labels = labels == null ? null : new List<string>(labels);
                _Tags = tags == null ? null : new Dictionary<string, object>(tags);
            }
            finally
            {
                _Lock.Release();
            }
        }

        private void ValidateNeighbor(int layer, Guid neighborGuid)
        {
            if (layer < 0 || layer > 63) throw new ArgumentOutOfRangeException(nameof(layer), "Layer must be between 0 and 63.");
            if (neighborGuid == Guid.Empty) throw new ArgumentException("Neighbor ID cannot be empty.", nameof(neighborGuid));
            if (neighborGuid == Id) throw new ArgumentException("Node cannot be its own neighbor.", nameof(neighborGuid));
        }
    }
}
