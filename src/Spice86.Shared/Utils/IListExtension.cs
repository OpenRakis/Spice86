namespace Spice86.Shared.Utils;

public static class IListExtension {
    /// <summary>
    /// Performs a shallow copy of elements from the <paramref name="source"/> list to the <paramref name="destination"/> list.
    /// </summary>
    /// <typeparam name="T">The type of elements in the lists.</typeparam>
    /// <param name="source">The list containing the elements to copy.</param>
    /// <param name="destination">
    /// The list that will receive the copied elements. Must be at least as long as <paramref name="source"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="destination"/> has fewer elements than <paramref name="source"/>.
    /// </exception>
    public static void CopyTo<T>(this IList<T> source, IList<T> destination) {
        if (source.Count > destination.Count) {
            throw new ArgumentException("destination is too short.");
        }

        for (int i = 0; i < source.Count; i++) {
            destination[i] = source[i];
        }
    }

    /// <summary>Attempts to copy the current <see cref="T:IList" /> to a destination <see cref="T:IList" /> and returns a value that indicates whether the copy operation succeeded.</summary>
    /// <param name="destination">The target of the copy operation.</param>
    /// <returns>
    /// <see langword="true" /> if the copy operation succeeded; otherwise, <see langword="false" />.</returns>
    public static bool TryCopyTo<T>(this IList<T> source, IList<T> destination) {
        if (source.Count > destination.Count) {
            return false;
        }

        CopyTo(source, destination);
        return true;
    }

    /// <summary>
    /// Returns a view (and not a copy!) of the <paramref name="source"/> list,
    /// starting at the specified <paramref name="offset"/> and containing up to <paramref name="count"/> elements.
    /// 
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    /// <param name="source">The original list to slice.</param>
    /// <param name="offset">The zero-based index at which the slice begins.</param>
    /// <param name="count">The number of elements to include in the slice.</param>
    /// <returns>
    /// An <see cref="IList{T}"/> representing the specified slice of the original list.
    /// </returns>
    public static IList<T> GetSlice<T>(this IList<T> source, int offset, int count) {
        return new ListView<T>(source, offset, count);
    }

    /// <summary>
    /// Creates a non-copying slice of the list using the range operator.
    /// </summary>
    /// <typeparam name="T">Type of elements in the list.</typeparam>
    /// <param name="list">The source list to slice.</param>
    /// <param name="range">The range specifying the slice (e.g., 2..5).</param>
    /// <returns>A <see cref="ListView{T}"/> representing the specified window.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the range is invalid.</exception>
    public static IList<T> GetSlice<T>(this IList<T> list, Range range) {
        int start = range.Start.IsFromEnd ? list.Count - range.Start.Value : range.Start.Value;
        int end = range.End.IsFromEnd ? list.Count - range.End.Value : range.End.Value;
        if (start < 0 || end < start || end > list.Count) {
            throw new ArgumentOutOfRangeException(nameof(range), "Invalid range for slicing.");
        }

        return GetSlice(list, start, end - start);
    }
}