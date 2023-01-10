namespace Spice86.Core;

public static class LinkedListExtensions
{
    /// <summary>
    /// Replaces an existing linked list node with zero or more new nodes.
    /// </summary>
    /// <typeparam name="T">Type of list item.</typeparam>
    /// <param name="list">Linked list instance.</param>
    /// <param name="originalItem">Item to replace.</param>
    /// <param name="newItems">Values to insert in place of the original item.</param>
    public static void Replace<T>(this LinkedList<T> list, T originalItem, T[] newItems)
    {
        if (list == null) {
            throw new ArgumentNullException(nameof(list));
        }

        if (newItems == null) {
            throw new ArgumentNullException(nameof(newItems));
        }

        LinkedListNode<T>? originalNode = list.Find(originalItem);
        if (originalNode == null) {
            throw new ArgumentException("Original item not found.");
        }

        if (originalNode.Previous == null)
        {
            list.RemoveFirst();
            for (int i = newItems.Length - 1; i >= 0; i--) {
                list.AddFirst(newItems[i]);
            }
        }
        else
        {
            LinkedListNode<T> previous = originalNode.Previous;
            list.Remove(originalNode);
            for (int i = newItems.Length - 1; i >= 0; i--) {
                list.AddAfter(previous, newItems[i]);
            }
        }
    }
    /// <summary>
    /// Replaces an existing linked list node with a new node.
    /// </summary>
    /// <typeparam name="T">Type of list item.</typeparam>
    /// <param name="list">Linked list instance.</param>
    /// <param name="originalItem">Item to replace.</param>
    /// <param name="newItem">New item to replace the original with.</param>
    public static void Replace<T>(this LinkedList<T> list, T originalItem, T newItem) {
        if (list == null) {
            throw new ArgumentNullException(nameof(list));
        }

        LinkedListNode<T>? originalNode = list.Find(originalItem);
        if (originalNode == null) {
            throw new ArgumentException("Original item not found.");
        }

        if (originalNode.Previous == null)
        {
            list.RemoveFirst();
            list.AddFirst(newItem);
        }
        else
        {
            LinkedListNode<T> previous = originalNode.Previous;
            list.Remove(originalNode);
            list.AddAfter(previous, newItem);
        }
    }
}
