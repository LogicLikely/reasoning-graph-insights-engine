namespace Backend.Insights.Contracts;

/// <summary>
/// Freezes the numeric projection used before floating-derived algorithm values
/// enter a canonical logical-result digest. Exact integer/count fields and
/// canonical input parameters are not rounded by this helper.
/// </summary>
public static class CanonicalResultNumber
{
    public const int FractionalDecimalPlaces = 12;

    public static decimal Normalize(decimal value) =>
        decimal.Round(value, FractionalDecimalPlaces, MidpointRounding.ToEven);

    public static decimal Normalize(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Canonical algorithm result numbers must be finite.");
        }

        try
        {
            return Normalize((decimal)value);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Canonical algorithm result numbers must fit the decimal contract range.");
        }
    }
}
