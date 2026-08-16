using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Insights.Contracts;

namespace backend.Tests.Insights.Contracts;

[TestClass]
public class CanonicalJsonTests
{
    [TestMethod]
    public void Canonicalize_SortsObjectKeysPreservesArrayOrderAndNormalizesNumbers()
    {
        using var document = JsonDocument.Parse(
            """{"z":null,"b":[3.00,1e0,-0.000],"a":{"y":1.2300,"x":1000}}""");

        var canonical = CanonicalJson.Canonicalize(document.RootElement);

        Assert.AreEqual(
            """{"a":{"x":1e3,"y":1.23},"b":[3,1,0],"z":null}""",
            canonical);
    }

    [TestMethod]
    public void ComputeSha256_IsStableAcrossPropertyOrderWhitespaceAndNumberSpelling()
    {
        using var left = JsonDocument.Parse(""" { "b": 1.00, "a": [2e0, null] } """);
        using var right = JsonDocument.Parse("""{"a":[2.000,null],"b":1e0}""");

        Assert.AreEqual(
            CanonicalJson.ComputeSha256(left.RootElement),
            CanonicalJson.ComputeSha256(right.RootElement));
    }

    [TestMethod]
    public void ComputeSha256_IsSensitiveToArrayOrderLogicalValuesAndExplicitNull()
    {
        using var baseline = JsonDocument.Parse("""{"items":[1,2],"optional":null}""");
        using var reordered = JsonDocument.Parse("""{"items":[2,1],"optional":null}""");
        using var changed = JsonDocument.Parse("""{"items":[1,3],"optional":null}""");
        using var omittedNull = JsonDocument.Parse("""{"items":[1,2]}""");

        var digest = CanonicalJson.ComputeSha256(baseline.RootElement);
        Assert.AreNotEqual(digest, CanonicalJson.ComputeSha256(reordered.RootElement));
        Assert.AreNotEqual(digest, CanonicalJson.ComputeSha256(changed.RootElement));
        Assert.AreNotEqual(digest, CanonicalJson.ComputeSha256(omittedNull.RootElement));
    }

    [TestMethod]
    public void ComputeSha256_UsesUtf8WithoutBomAndLowercasePrefixedHex()
    {
        var value = new { text = "résilience", optional = (string?)null };
        var canonical = CanonicalJson.Canonicalize(value);
        var expectedBytes = Encoding.UTF8.GetBytes(canonical);
        var expected = $"sha256:{Convert.ToHexString(SHA256.HashData(expectedBytes)).ToLowerInvariant()}";

        var actual = CanonicalJson.ComputeSha256(value);

        Assert.AreEqual("{" + "\"optional\":null,\"text\":\"résilience\"}", canonical);
        Assert.AreEqual(expected, actual);
        StringAssert.Matches(actual, new System.Text.RegularExpressions.Regex("^sha256:[0-9a-f]{64}$"));
        Assert.IsFalse(expectedBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
    }

    [TestMethod]
    public void ComputeSha256Sequence_MatchesTheCanonicalCompleteArrayDigest()
    {
        var values = new[]
        {
            new { nodeId = "b", score = 1.2500m, path = new[] { "b", "root" } },
            new { nodeId = "a", score = -0.0m, path = new[] { "a", "root" } }
        };

        Assert.AreEqual(
            CanonicalJson.ComputeSha256(values),
            CanonicalJson.ComputeSha256Sequence(values, CancellationToken.None));

        var jsonValues = values
            .Select(value => JsonSerializer.SerializeToElement(value))
            .ToArray();
        Assert.AreEqual(
            CanonicalJson.ComputeSha256(jsonValues),
            CanonicalJson.ComputeSha256Sequence(jsonValues, CancellationToken.None));
    }

    [TestMethod]
    public void ComputeSha256Sequence_ObservesCancellationWhileEnumeratingItems()
    {
        using var cancellation = new CancellationTokenSource();

        Assert.ThrowsException<OperationCanceledException>(() =>
            CanonicalJson.ComputeSha256Sequence(
                CancelAfterFirst(cancellation),
                cancellation.Token));
    }

    [TestMethod]
    public void Canonicalize_RejectsDuplicateObjectMemberNames()
    {
        using var document = JsonDocument.Parse("""{"same":1,"same":2}""");

        Assert.ThrowsException<FormatException>(() => CanonicalJson.Canonicalize(document.RootElement));
    }

    [TestMethod]
    public void CanonicalResultNumber_FreezesFloatingProjectionAtTwelveDecimalPlaces()
    {
        Assert.AreEqual(0.123456789012m, CanonicalResultNumber.Normalize(0.1234567890124m));
        Assert.AreEqual(0.123456789013m, CanonicalResultNumber.Normalize(0.1234567890126m));
        Assert.AreEqual(1.234567890123m, CanonicalResultNumber.Normalize(1.2345678901234d));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            CanonicalResultNumber.Normalize(double.PositiveInfinity));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            CanonicalResultNumber.Normalize(double.MaxValue));
    }

    [TestMethod]
    public void ContractSerializer_RejectsNumericAndUndefinedEnumRepresentations()
    {
        var options = CanonicalJson.CreateSerializerOptions();

        Assert.ThrowsException<JsonException>(() =>
            JsonSerializer.Deserialize<ExecutionStatus>("1", options));
        Assert.ThrowsException<JsonException>(() =>
            JsonSerializer.Deserialize<ExecutionStatus>("\"Succeeded\"", options));
        Assert.AreEqual(
            ExecutionStatus.Succeeded,
            JsonSerializer.Deserialize<ExecutionStatus>("\"succeeded\"", options));
        Assert.ThrowsException<JsonException>(() =>
            JsonSerializer.Serialize((ExecutionStatus)999, options));
    }

    private static IEnumerable<int> CancelAfterFirst(CancellationTokenSource cancellation)
    {
        yield return 1;
        cancellation.Cancel();
        yield return 2;
    }
}
