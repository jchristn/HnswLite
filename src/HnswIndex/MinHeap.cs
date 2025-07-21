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
        // Private members
        private List<(float priority, T item)> _items = new List<(float, T)>();
        private IComparer<T> _itemComparer = Comparer<T>.Default;

        // Public members
        /// <summary>
        /// Gets the number of items in the heap.
        /// Minimum: 0, Maximum: int.MaxValue (limited by available memory).
        /// </summary>
        public int Count => _items.Count;

        /// <summary>
        /// Gets whether the heap is empty.
        /// </summary>
        public bool IsEmpty => _items.Count == 0;

        /// <summary>
        /// Gets or sets the capacity of the internal storage.
        /// Minimum: 0, Maximum: int.MaxValue (limited by available memory).
        /// Setting a smaller capacity than Count will automatically adjust to Count.
        /// </summary>
        public int Capacity
        {
            get => _items.Capacity;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "Capacity cannot be negative.");
                _items.Capacity = Math.Max(value, _items.Count);
            }
        }

        // Constructors
        /// <summary>
        /// Initializes a new instance of the MinHeap class with default capacity.
        /// </summary>
        /// <param name="itemComparer">Optional comparer for items with equal priority. 
        /// If null, uses Comparer&lt;T&gt;.Default.</param>
        public MinHeap(IComparer<T> itemComparer = null)
        {
            _itemComparer = itemComparer ?? Comparer<T>.Default;
            _items = new List<(float, T)>();
        }

        /// <summary>
        /// Initializes a new instance of the MinHeap class with specified initial capacity.
        /// </summary>
        /// <param name="capacity">Initial capacity. Minimum: 0, Maximum: int.MaxValue.</param>
        /// <param name="itemComparer">Optional comparer for items with equal priority. 
        /// If null, uses Comparer&lt;T&gt;.Default.</param>
        public MinHeap(int capacity, IComparer<T> itemComparer = null)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity),
                    "Capacity cannot be negative.");

            _itemComparer = itemComparer ?? Comparer<T>.Default;
            _items = new List<(float, T)>(capacity);
        }

        // Public methods
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

            _items.Add((priority, item));
            BubbleUp(_items.Count - 1);
        }

        /// <summary>
        /// Removes and returns the item with the lowest priority.
        /// </summary>
        /// <returns>A tuple containing the priority and item.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the heap is empty.</exception>
        public (float priority, T item) Pop()
        {
            if (_items.Count == 0)
                throw new InvalidOperationException("Cannot pop from an empty heap.");

            var result = _items[0];

            if (_items.Count == 1)
            {
                _items.Clear();
            }
            else
            {
                _items[0] = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);
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
            if (_items.Count == 0)
                throw new InvalidOperationException("Cannot peek at an empty heap.");

            return _items[0];
        }

        /// <summary>
        /// Removes all items from the heap.
        /// </summary>
        public void Clear()
        {
            _items.Clear();
        }

        /// <summary>
        /// Determines whether the heap contains a specific item.
        /// </summary>
        /// <param name="item">The item to locate in the heap.</param>
        /// <returns>true if item is found in the heap; otherwise, false.</returns>
        public bool Contains(T item)
        {
            return _items.Any(x => EqualityComparer<T>.Default.Equals(x.item, item));
        }

        /// <summary>
        /// Returns all items in the heap sorted by priority (ascending).
        /// Items with equal priority are sorted using the item comparer.
        /// This operation does not modify the heap.
        /// </summary>
        /// <returns>A new list containing all items sorted by priority.</returns>
        public List<(float priority, T item)> GetAll()
        {
            return _items
                .OrderBy(x => x.priority)
                .ThenBy(x => x.item, _itemComparer)
                .ToList();
        }

        /// <summary>
        /// Returns all items in the heap in their internal storage order (not sorted).
        /// This operation is O(n) and does not modify the heap.
        /// </summary>
        /// <returns>A new list containing all items in heap order.</returns>
        public List<(float priority, T item)> GetAllUnsorted()
        {
            return new List<(float priority, T item)>(_items);
        }

        /// <summary>
        /// Converts the heap to an array sorted by priority (ascending).
        /// </summary>
        /// <returns>An array containing all items sorted by priority.</returns>
        public (float priority, T item)[] ToArray()
        {
            return GetAll().ToArray();
        }

        // Private methods
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

                if (leftChild < _items.Count && CompareItems(leftChild, smallest) < 0)
                    smallest = leftChild;

                if (rightChild < _items.Count && CompareItems(rightChild, smallest) < 0)
                    smallest = rightChild;

                if (smallest == index)
                    break;

                Swap(index, smallest);
                index = smallest;
            }
        }

        private int CompareItems(int i, int j)
        {
            int priorityComparison = _items[i].priority.CompareTo(_items[j].priority);
            if (priorityComparison != 0)
                return priorityComparison;

            // Use item comparer for tie-breaking
            return _itemComparer.Compare(_items[i].item, _items[j].item);
        }

        private void Swap(int i, int j)
        {
            (_items[i], _items[j]) = (_items[j], _items[i]);
        }
    }
}