namespace Hnsw
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Simple min-heap implementation for priority queue operations.
    /// </summary>
    /// <typeparam name="T">The type of elements in the heap.</typeparam>
    public class MinHeap<T>
    {

        #region Private-Members

        private List<(float priority, T item)> _Items = new List<(float, T)>();
        private IComparer<T> _ItemComparer = Comparer<T>.Default;

        #endregion

        #region Public-Members
        /// <summary>
        /// Gets the number of items in the heap.
        /// Minimum: 0, Maximum: int.MaxValue (limited by available memory).
        /// </summary>
        public int Count => _Items.Count;

        /// <summary>
        /// Gets whether the heap is empty.
        /// </summary>
        public bool IsEmpty => _Items.Count == 0;

        /// <summary>
        /// Gets or sets the capacity of the internal storage.
        /// Minimum: 0, Maximum: int.MaxValue (limited by available memory).
        /// Setting a smaller capacity than Count will automatically adjust to Count.
        /// </summary>
        public int Capacity
        {
            get => _Items.Capacity;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "Capacity cannot be negative.");
                _Items.Capacity = Math.Max(value, _Items.Count);
            }
        }

        #endregion

        #region Constructors-and-Factories
        /// <summary>
        /// Initializes a new instance of the MinHeap class with default capacity.
        /// </summary>
        /// <param name="itemComparer">Optional comparer for items with equal priority. 
        /// If null, uses Comparer&lt;T&gt;.Default.</param>
        public MinHeap(IComparer<T>? itemComparer = null)
        {
            _ItemComparer = itemComparer ?? Comparer<T>.Default;
            _Items = new List<(float, T)>();
        }

        /// <summary>
        /// Initializes a new instance of the MinHeap class with specified initial capacity.
        /// </summary>
        /// <param name="capacity">Initial capacity. Minimum: 0, Maximum: int.MaxValue.</param>
        /// <param name="itemComparer">Optional comparer for items with equal priority. 
        /// If null, uses Comparer&lt;T&gt;.Default.</param>
        public MinHeap(int capacity, IComparer<T>? itemComparer = null)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity),
                    "Capacity cannot be negative.");

            _ItemComparer = itemComparer ?? Comparer<T>.Default;
            _Items = new List<(float, T)>(capacity);
        }

        #endregion

        #region Public-Methods
        /// <summary>
        /// Adds an item to the heap with the specified priority.
        /// Lower priority values are considered higher priority (min-heap).
        /// </summary>
        /// <param name="priority">The priority of the item. Must be a finite number (not NaN or Infinity).</param>
        /// <param name="item">The item to add. Can be null if T is a reference type.</param>
        /// <exception cref="ArgumentException">Thrown when priority is NaN or Infinity.</exception>
        public void Push(float priority, T item)
        {
            if (float.IsNaN(priority) || float.IsInfinity(priority))
                throw new ArgumentException("Priority must be a finite number.", nameof(priority));

            _Items.Add((priority, item));
            BubbleUp(_Items.Count - 1);
        }

        /// <summary>
        /// Removes and returns the item with the lowest priority.
        /// </summary>
        /// <returns>A tuple containing the priority and item.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the heap is empty.</exception>
        public (float priority, T item) Pop()
        {
            if (_Items.Count == 0)
                throw new InvalidOperationException("Cannot pop from an empty heap.");

            var result = _Items[0];

            if (_Items.Count == 1)
            {
                _Items.Clear();
            }
            else
            {
                _Items[0] = _Items[_Items.Count - 1];
                _Items.RemoveAt(_Items.Count - 1);
                BubbleDown(0);
            }

            return result;
        }

        /// <summary>
        /// Returns the item with the lowest priority without removing it.
        /// </summary>
        /// <returns>A tuple containing the priority and item.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the heap is empty.</exception>
        public (float priority, T item) Peek()
        {
            if (_Items.Count == 0)
                throw new InvalidOperationException("Cannot peek at an empty heap.");

            return _Items[0];
        }

        /// <summary>
        /// Removes all items from the heap.
        /// </summary>
        public void Clear()
        {
            _Items.Clear();
        }

        /// <summary>
        /// Determines whether the heap contains a specific item.
        /// </summary>
        /// <param name="item">The item to locate in the heap.</param>
        /// <returns>true if item is found in the heap; otherwise, false.</returns>
        public bool Contains(T item)
        {
            return _Items.Any(x => EqualityComparer<T>.Default.Equals(x.item, item));
        }

        /// <summary>
        /// Returns all items in the heap sorted by priority (ascending).
        /// Items with equal priority are sorted using the item comparer.
        /// This operation does not modify the heap.
        /// </summary>
        /// <returns>A new list containing all items sorted by priority.</returns>
        public List<(float priority, T item)> GetAll()
        {
            return _Items
                .OrderBy(x => x.priority)
                .ThenBy(x => x.item, _ItemComparer)
                .ToList();
        }

        /// <summary>
        /// Returns all items in the heap in their internal storage order (not sorted).
        /// This operation is O(n) and does not modify the heap.
        /// </summary>
        /// <returns>A new list containing all items in heap order.</returns>
        public List<(float priority, T item)> GetAllUnsorted()
        {
            return new List<(float priority, T item)>(_Items);
        }

        /// <summary>
        /// Converts the heap to an array sorted by priority (ascending).
        /// </summary>
        /// <returns>An array containing all items sorted by priority.</returns>
        public (float priority, T item)[] ToArray()
        {
            return GetAll().ToArray();
        }

        #endregion

        #region Private-Methods

        private void BubbleUp(int childIndex)
        {
            while (childIndex > 0)
            {
                int parentIndex = (childIndex - 1) / 2;
                if (CompareItems(parentIndex, childIndex) <= 0)
                    break;

                Swap(parentIndex, childIndex);
                childIndex = parentIndex;
            }
        }

        private void BubbleDown(int index)
        {
            while (true)
            {
                int smallest = index;
                int leftChild = 2 * index + 1;
                int rightChild = 2 * index + 2;

                if (leftChild < _Items.Count && CompareItems(leftChild, smallest) < 0)
                    smallest = leftChild;

                if (rightChild < _Items.Count && CompareItems(rightChild, smallest) < 0)
                    smallest = rightChild;

                if (smallest == index)
                    break;

                Swap(index, smallest);
                index = smallest;
            }
        }

        private int CompareItems(int i, int j)
        {
            int priorityComparison = _Items[i].priority.CompareTo(_Items[j].priority);
            if (priorityComparison != 0)
                return priorityComparison;

            // Use item comparer for tie-breaking
            return _ItemComparer.Compare(_Items[i].item, _Items[j].item);
        }

        private void Swap(int i, int j)
        {
            (_Items[i], _Items[j]) = (_Items[j], _Items[i]);
        }

        #endregion
    }
}