namespace Hnsw
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Parameters for HNSW index configuration.
    /// </summary>
    public class HnswParameters
    {
        // Private backing fields
        private int _m = 16;
        private int _maxM = 32;
        private int _efConstruction = 200;
        private int _maxLayers = 16;
        private double _levelMultiplier = 1.0 / Math.Log(2.0);
        private bool _extendCandidates = false;
        private bool _keepPrunedConnections = false;
        private string _distanceFunctionName = "L2";

        /// <summary>
        /// Gets or sets the maximum number of connections per layer (except layer 0).
        /// Minimum: 2, Maximum: 100, Default: 16.
        /// Typical values range from 8 to 48.
        /// </summary>
        public int M
        {
            get => _m;
            set
            {
                if (value < 2)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "M must be at least 2 for meaningful connectivity.");
                if (value > 100)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "M values greater than 100 are not recommended due to performance implications.");
                _m = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of connections for layer 0.
        /// Minimum: 1, Maximum: 200, Default: 32.
        /// Should be greater than M, typically 2*M.
        /// </summary>
        public int MaxM
        {
            get => _maxM;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "MaxM must be at least 1.");
                if (value > 200)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "MaxM values greater than 200 are not recommended due to performance implications.");
                _maxM = value;
            }
        }

        /// <summary>
        /// Gets or sets the size of the dynamic candidate list.
        /// Minimum: 1, Maximum: 2000, Default: 200.
        /// Higher values improve recall but decrease construction speed.
        /// </summary>
        public int EfConstruction
        {
            get => _efConstruction;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "EfConstruction must be at least 1.");
                if (value > 2000)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "EfConstruction values greater than 2000 provide diminishing returns.");
                _efConstruction = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of layers in the graph.
        /// Minimum: 1, Maximum: 64, Default: 16.
        /// Typically between 10 and 30.
        /// </summary>
        public int MaxLayers
        {
            get => _maxLayers;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "MaxLayers must be at least 1.");
                if (value > 64)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "MaxLayers greater than 64 is not recommended.");
                _maxLayers = value;
            }
        }

        /// <summary>
        /// Gets or sets the level assignment multiplier.
        /// Minimum: Greater than 0, Maximum: 2.0, Default: 1/ln(2) ≈ 1.44.
        /// Controls the layer assignment probability distribution.
        /// Must be a finite number (not NaN or Infinity).
        /// </summary>
        public double LevelMultiplier
        {
            get => _levelMultiplier;
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "LevelMultiplier must be greater than 0.");
                if (value > 2.0)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "LevelMultiplier greater than 2.0 is not typical.");
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new ArgumentException("LevelMultiplier must be a valid finite number.",
                        nameof(value));
                _levelMultiplier = value;
            }
        }

        /// <summary>
        /// Gets or sets whether to extend candidates with neighbors' neighbors.
        /// Default: false.
        /// Can improve recall at the cost of construction time.
        /// </summary>
        public bool ExtendCandidates
        {
            get => _extendCandidates;
            set => _extendCandidates = value;
        }

        /// <summary>
        /// Gets or sets whether to add pruned connections to lower levels.
        /// Default: false.
        /// Can improve connectivity at the cost of more memory usage.
        /// </summary>
        public bool KeepPrunedConnections
        {
            get => _keepPrunedConnections;
            set => _keepPrunedConnections = value;
        }

        /// <summary>
        /// Gets or sets the name of the distance function.
        /// Minimum length: 1 (non-whitespace), Maximum length: 100 characters, Default: "L2".
        /// Common values: "L2", "L1", "Cosine", "InnerProduct".
        /// Cannot be null or whitespace.
        /// </summary>
        public string DistanceFunctionName
        {
            get => _distanceFunctionName;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("DistanceFunctionName cannot be null or whitespace.",
                        nameof(value));
                if (value.Length > 100)
                    throw new ArgumentException("DistanceFunctionName length cannot exceed 100 characters.",
                        nameof(value));
                _distanceFunctionName = value;
            }
        }

        /// <summary>
        /// Initializes a new instance of the HnswParameters class with default values.
        /// </summary>
        public HnswParameters()
        {

        }

        /// <summary>
        /// Validates that all parameters are consistent with each other.
        /// </summary>
        public void Validate()
        {
            if (MaxM < M)
                throw new InvalidOperationException(
                    $"MaxM ({MaxM}) should be greater than or equal to M ({M}).");

            if (EfConstruction < M)
                throw new InvalidOperationException(
                    $"EfConstruction ({EfConstruction}) should be greater than or equal to M ({M}) for optimal performance.");
        }
    }
}