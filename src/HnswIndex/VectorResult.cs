namespace Hnsw
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Represents a search result containing the vector ID, distance, and vector data.
    /// Used to return results from nearest neighbor searches in the HNSW index.
    /// </summary>
    public class VectorResult
    {
        // Private backing fields
        private Guid _guid = Guid.Empty;
        private float _distance = 0.0f;
        private List<float> _vectors = new List<float>();

        // Public properties
        /// <summary>
        /// Gets or sets the unique identifier of the vector.
        /// Cannot be Guid.Empty.
        /// Default: Guid.Empty (should be set to valid value before use).
        /// </summary>
        public Guid GUID
        {
            get => _guid;
            set
            {
                if (value == Guid.Empty)
                    throw new ArgumentException("GUID cannot be Guid.Empty.", nameof(value));
                _guid = value;
            }
        }

        /// <summary>
        /// Gets or sets the distance from the query vector.
        /// Minimum: 0.0 (identical vectors), Maximum: float.MaxValue, Default: 0.0.
        /// Must be a finite non-negative number (not NaN or Infinity).
        /// Distance interpretation depends on the distance function used:
        /// - Euclidean: L2 distance (0 = identical)
        /// - Cosine: 1 - cosine similarity (0 = identical, 2 = opposite)
        /// - DotProduct: Negative dot product (lower = more similar)
        /// </summary>
        public float Distance
        {
            get => _distance;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new ArgumentException("Distance must be a finite number.", nameof(value));
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "Distance cannot be negative.");
                _distance = value;
            }
        }

        /// <summary>
        /// Gets or sets the vector data.
        /// Cannot be null. Setting to null creates a new empty list.
        /// Vector dimension typically ranges from 1 to 4096.
        /// All values should be finite (not NaN or Infinity).
        /// Default: Empty list.
        /// Note: Property name 'Vectors' is plural for compatibility but represents a single vector.
        /// </summary>
        public List<float> Vectors
        {
            get => _vectors;
            set
            {
                if (value == null)
                {
                    _vectors = new List<float>();
                }
                else
                {
                    // Validate vector values
                    for (int i = 0; i < value.Count; i++)
                    {
                        if (float.IsNaN(value[i]) || float.IsInfinity(value[i]))
                            throw new ArgumentException($"Vector contains invalid value at index {i}. All values must be finite.", nameof(value));
                    }
                    _vectors = value;
                }
            }
        }

        // Constructors
        /// <summary>
        /// Initializes a new instance of the VectorResult class with default values.
        /// </summary>
        public VectorResult()
        {
            // All fields are already initialized with their default values
        }

        /// <summary>
        /// Initializes a new instance of the VectorResult class with specified values.
        /// </summary>
        /// <param name="guid">The vector identifier. Cannot be Guid.Empty.</param>
        /// <param name="distance">The distance from query. Minimum: 0.0.</param>
        /// <param name="vectors">The vector data. Cannot be null.</param>
        public VectorResult(Guid guid, float distance, List<float> vectors)
        {
            GUID = guid;
            Distance = distance;
            Vectors = vectors ?? throw new ArgumentNullException(nameof(vectors));
        }

        /// <summary>
        /// Initializes a new instance of the VectorResult class by copying vector data.
        /// </summary>
        /// <param name="guid">The vector identifier. Cannot be Guid.Empty.</param>
        /// <param name="distance">The distance from query. Minimum: 0.0.</param>
        /// <param name="vectors">The vector data to copy. Cannot be null.</param>
        public VectorResult(Guid guid, float distance, IEnumerable<float> vectors)
        {
            GUID = guid;
            Distance = distance;
            Vectors = vectors?.ToList() ?? throw new ArgumentNullException(nameof(vectors));
        }

        // Public methods
        /// <summary>
        /// Validates that the result is complete and consistent.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
        public void Validate()
        {
            if (_guid == Guid.Empty)
                throw new InvalidOperationException("GUID cannot be Guid.Empty.");

            if (_vectors == null || _vectors.Count == 0)
                throw new InvalidOperationException("Vectors cannot be null or empty.");

            if (float.IsNaN(_distance) || float.IsInfinity(_distance))
                throw new InvalidOperationException("Distance must be a finite number.");

            if (_distance < 0)
                throw new InvalidOperationException("Distance cannot be negative.");
        }

        /// <summary>
        /// Gets the dimension of the vector.
        /// </summary>
        public int Dimension => _vectors.Count;

        /// <summary>
        /// Creates a deep copy of this VectorResult.
        /// </summary>
        /// <returns>A new VectorResult instance with copied values.</returns>
        public VectorResult Clone()
        {
            return new VectorResult
            {
                _guid = this._guid,
                _distance = this._distance,
                Vectors = new List<float>(this._vectors)
            };
        }

        /// <summary>
        /// Returns a string representation of the vector result.
        /// </summary>
        /// <returns>A string containing the GUID, distance, and vector dimension.</returns>
        public override string ToString()
        {
            return $"VectorResult {{ GUID = {_guid}, Distance = {_distance:F6}, Dimension = {_vectors.Count} }}";
        }

        /// <summary>
        /// Determines whether this instance is equal to another VectorResult.
        /// </summary>
        /// <param name="other">The VectorResult to compare with.</param>
        /// <returns>true if the instances are equal; otherwise, false.</returns>
        public bool Equals(VectorResult other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return true;

            return _guid == other._guid &&
                   Math.Abs(_distance - other._distance) < float.Epsilon &&
                   _vectors.Count == other._vectors.Count &&
                   _vectors.SequenceEqual(other._vectors);
        }

        /// <summary>
        /// Determines whether this instance is equal to another object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>true if the instances are equal; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as VectorResult);
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + _guid.GetHashCode();
                hash = hash * 31 + _distance.GetHashCode();
                hash = hash * 31 + (_vectors?.Count ?? 0);
                return hash;
            }
        }
    }
}