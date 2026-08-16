using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Backend.Insights.Contracts;

public sealed record SemanticIdentity
{
    private static readonly Regex IdentityPattern = new(
        "^(?<family>[a-z][a-z0-9]*(?:-[a-z0-9]+)*)-v(?<version>0|[1-9][0-9]*)\\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private SemanticIdentity(string family, BigInteger version)
    {
        Family = family;
        Version = version;
    }

    public string Family { get; }

    public BigInteger Version { get; }

    public string Value => $"{Family}-v{Version.ToString(CultureInfo.InvariantCulture)}";

    public static SemanticIdentity Parse(string value)
    {
        if (!TryParse(value, out var identity))
        {
            throw new FormatException(
                $"Semantic identity '{value}' must match '<family>-vN' with a lowercase kebab-case family and a non-negative version without leading zeroes.");
        }

        return identity;
    }

    public static bool TryParse(string? value, out SemanticIdentity identity)
    {
        identity = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = IdentityPattern.Match(value);
        if (!match.Success ||
            !BigInteger.TryParse(match.Groups["version"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            return false;
        }

        identity = new SemanticIdentity(match.Groups["family"].Value, version);
        return true;
    }

    public override string ToString() => Value;
}

[Flags]
public enum SemanticContractChange
{
    ImplementationOnly = 0,
    LogicalMeaning = 1 << 0,
    CanonicalDigest = 1 << 1,
    DeterministicOrdering = 1 << 2
}

public static class SemanticIdentityRules
{
    public static bool RequiresNewIdentity(SemanticContractChange change)
    {
        const SemanticContractChange supportedChanges =
            SemanticContractChange.LogicalMeaning |
            SemanticContractChange.CanonicalDigest |
            SemanticContractChange.DeterministicOrdering;

        if ((change & ~supportedChanges) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown semantic contract change.");
        }

        return change != SemanticContractChange.ImplementationOnly;
    }
}
