namespace Backend.Insights.Analysis;

/// <summary>
/// Deterministic stable ordering that lets cancellation escape directly.
/// Framework sort helpers wrap comparer exceptions, which would otherwise
/// misclassify an <see cref="OperationCanceledException"/> as an execution
/// failure at the worker boundary.
/// </summary>
internal static class CancellationAwareOrdering
{
    public static void Sort<T>(
        IList<T> values,
        Comparison<T> comparison,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(comparison);
        cancellationToken.ThrowIfCancellationRequested();

        if (values.Count < 2)
        {
            return;
        }

        var source = new T[values.Count];
        var destination = new T[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            source[index] = values[index];
        }

        var width = 1;
        while (width < source.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var left = 0;
            while (left < source.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var middle = (int)Math.Min((long)left + width, source.Length);
                var right = (int)Math.Min((long)middle + width, source.Length);
                Merge(
                    source,
                    destination,
                    left,
                    middle,
                    right,
                    comparison,
                    cancellationToken);
                left = right;
            }

            (source, destination) = (destination, source);
            width = width > source.Length / 2 ? source.Length : width * 2;
        }

        for (var index = 0; index < source.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            values[index] = source[index];
        }
    }

    private static void Merge<T>(
        IReadOnlyList<T> source,
        IList<T> destination,
        int left,
        int middle,
        int right,
        Comparison<T> comparison,
        CancellationToken cancellationToken)
    {
        var leftIndex = left;
        var rightIndex = middle;
        var destinationIndex = left;

        while (leftIndex < middle && rightIndex < right)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination[destinationIndex++] = comparison(
                source[leftIndex],
                source[rightIndex]) <= 0
                ? source[leftIndex++]
                : source[rightIndex++];
        }

        while (leftIndex < middle)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination[destinationIndex++] = source[leftIndex++];
        }

        while (rightIndex < right)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination[destinationIndex++] = source[rightIndex++];
        }
    }
}
