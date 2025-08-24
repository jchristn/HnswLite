namespace Hnsw
{
    using System;

    /// <summary>
    /// Represents a candidate node during HNSW search operations.
    /// </summary>
    internal class SearchCandidate : IComparable<SearchCandidate>
    {
        #region Public-Members

        /// <summary>
        /// Gets or sets the distance from the query vector.
        /// </summary>
        public float Distance 
        { 
            get => _Distance;
            set => _Distance = value;
        }

        /// <summary>
        /// Gets or sets the node identifier.
        /// </summary>
        public Guid NodeId 
        { 
            get => _NodeId;
            set => _NodeId = value;
        }

        #endregion

        #region Private-Members

        private float _Distance;
        private Guid _NodeId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the SearchCandidate class.
        /// </summary>
        public SearchCandidate()
        {
        }

        /// <summary>
        /// Initializes a new instance of the SearchCandidate class with specified values.
        /// </summary>
        /// <param name="distance">Distance from the query vector.</param>
        /// <param name="nodeId">Node identifier.</param>
        public SearchCandidate(float distance, Guid nodeId)
        {
            _Distance = distance;
            _NodeId = nodeId;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Compares this candidate to another based on distance.
        /// </summary>
        /// <param name="other">The other candidate to compare to. Can be null.</param>
        /// <returns>A value indicating the relative order.</returns>
        public int CompareTo(SearchCandidate? other)
        {
            if (other == null) 
            {
                return 1;
            }
            
            return _Distance.CompareTo(other._Distance);
        }

        #endregion
        
        #region Private-Methods
        
        #endregion
    }
}