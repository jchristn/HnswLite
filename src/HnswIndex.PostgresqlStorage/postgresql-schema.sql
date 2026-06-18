CREATE TABLE IF NOT EXISTS hnsw_indexes (
    id uuid PRIMARY KEY,
    name text NOT NULL UNIQUE,
    dimension integer NOT NULL DEFAULT 0,
    storage_type text NOT NULL DEFAULT 'PostgreSQL',
    distance_function text NOT NULL DEFAULT 'Euclidean',
    m integer NOT NULL DEFAULT 16,
    max_m integer NOT NULL DEFAULT 32,
    ef_construction integer NOT NULL DEFAULT 200,
    entry_point_id uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS hnsw_nodes (
    index_id uuid NOT NULL REFERENCES hnsw_indexes(id) ON DELETE CASCADE,
    id uuid NOT NULL,
    vector_blob bytea NOT NULL,
    vector_dimension integer NOT NULL,
    metadata_json jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (index_id, id)
);

CREATE TABLE IF NOT EXISTS hnsw_node_layers (
    index_id uuid NOT NULL,
    node_id uuid NOT NULL,
    layer integer NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (index_id, node_id),
    FOREIGN KEY (index_id, node_id) REFERENCES hnsw_nodes(index_id, id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS hnsw_neighbors (
    index_id uuid NOT NULL,
    node_id uuid NOT NULL,
    layer integer NOT NULL,
    neighbor_id uuid NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (index_id, node_id, layer, neighbor_id),
    FOREIGN KEY (index_id, node_id) REFERENCES hnsw_nodes(index_id, id) ON DELETE CASCADE,
    FOREIGN KEY (index_id, neighbor_id) REFERENCES hnsw_nodes(index_id, id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS hnsw_metadata (
    index_id uuid NOT NULL REFERENCES hnsw_indexes(id) ON DELETE CASCADE,
    key text NOT NULL,
    value text NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (index_id, key)
);

CREATE TABLE IF NOT EXISTS hnsw_system_metadata (
    key text PRIMARY KEY,
    value text NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_hnsw_nodes_index_id ON hnsw_nodes(index_id);
CREATE INDEX IF NOT EXISTS idx_hnsw_neighbors_node_layer ON hnsw_neighbors(index_id, node_id, layer);
CREATE INDEX IF NOT EXISTS idx_hnsw_neighbors_neighbor ON hnsw_neighbors(index_id, neighbor_id);
CREATE INDEX IF NOT EXISTS idx_hnsw_node_layers_index_id ON hnsw_node_layers(index_id);

INSERT INTO hnsw_system_metadata (key, value, updated_at)
VALUES ('schema.version', '2.0.0', now())
ON CONFLICT (key)
DO UPDATE SET value = EXCLUDED.value, updated_at = now();
