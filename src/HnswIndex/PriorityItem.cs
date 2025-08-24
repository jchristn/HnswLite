namespace Hnsw
{
    using System;

    /// <summary>
    /// Represents an item with an associated priority for use in priority queues.
    /// </summary>
    /// <typeparam name="T">The type of the item.</typeparam>
    public sealed class PriorityItem<T>
    {
        #region Public-Members

        /// <summary>
        /// Gets the priority of the item.
        /// Lower values indicate higher priority.
        /// </summary>
        public float Priority => _Priority;

        /// <summary>
        /// Gets the item associated with the priority.
        /// </summary>
        public T Item => _Item;

        #endregion

        #region Private-Members

        private readonly float _Priority;
        private readonly T _Item;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the PriorityItem class.
        /// </summary>
        /// <param name="priority">The priority of the item. Must be a finite number.</param>
        /// <param name="item">The item associated with the priority.</param>
        /// <exception cref="ArgumentException">Thrown when priority is NaN or Infinity.</exception>
        public PriorityItem(float priority, T item)
        {
            if (float.IsNaN(priority) || float.IsInfinity(priority))
                throw new ArgumentException("Priority must be a finite number.", nameof(priority));

            _Priority = priority;
            _Item = item;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns a string representation of the priority item.
        /// </summary>
        /// <returns>A string containing the priority and item information.</returns>
        public override string ToString()
        {
            return $"Priority: {_Priority}, Item: {_Item}";
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current priority item.
        /// </summary>
        /// <param name="obj">The object to compare with the current priority item.</param>
        /// <returns>true if the specified object is equal to the current priority item; otherwise, false.</returns>
        public override bool Equals(object? obj)
        {
            if (obj is PriorityItem<T> other)
            {
                return _Priority.Equals(other._Priority) && 
                       System.Collections.Generic.EqualityComparer<T>.Default.Equals(_Item, other._Item);
            }
            return false;
        }

        /// <summary>
        /// Returns the hash code for this priority item.
        /// </summary>
        /// <returns>A hash code for the current priority item.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(_Priority, _Item);
        }

        #endregion
        
        #region Private-Methods
        
        #endregion
    }
}