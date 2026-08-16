using System.Numerics;
using Backend.Insights.Contracts;

namespace backend.Tests.Insights.Contracts;

[TestClass]
public class SemanticIdentityTests
{
    [DataTestMethod]
    [DataRow("robustness-v0", "robustness", 0)]
    [DataRow("critical-counter-v1", "critical-counter", 1)]
    [DataRow("family2-part3-v42", "family2-part3", 42)]
    public void Parse_AcceptsFrozenFormat(string value, string family, int version)
    {
        var identity = SemanticIdentity.Parse(value);

        Assert.AreEqual(family, identity.Family);
        Assert.AreEqual(new BigInteger(version), identity.Version);
        Assert.AreEqual(value, identity.Value);
        Assert.AreEqual(value, identity.ToString());
    }

    [TestMethod]
    public void Parse_AcceptsNonNegativeVersionsBeyondInt32WithoutChangingTheStringContract()
    {
        var identity = SemanticIdentity.Parse("algorithm-v2147483648");

        Assert.AreEqual(BigInteger.Parse("2147483648"), identity.Version);
        Assert.AreEqual("algorithm-v2147483648", identity.Value);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("Robustness-v0")]
    [DataRow("robustness")]
    [DataRow("robustness-v01")]
    [DataRow("robustness-v-1")]
    [DataRow("robustness_v1")]
    [DataRow("-robustness-v1")]
    [DataRow("robustness-v0\n")]
    [DataRow("robustness-v0\r\n")]
    public void TryParse_RejectsValuesOutsideFrozenFormat(string value)
    {
        Assert.IsFalse(SemanticIdentity.TryParse(value, out _));
        Assert.ThrowsException<FormatException>(() => SemanticIdentity.Parse(value));
    }

    [TestMethod]
    public void Rules_KeepIdentityForOutputPreservingImplementationChanges()
    {
        Assert.IsFalse(SemanticIdentityRules.RequiresNewIdentity(SemanticContractChange.ImplementationOnly));
    }

    [DataTestMethod]
    [DataRow(SemanticContractChange.LogicalMeaning)]
    [DataRow(SemanticContractChange.CanonicalDigest)]
    [DataRow(SemanticContractChange.DeterministicOrdering)]
    [DataRow(SemanticContractChange.LogicalMeaning | SemanticContractChange.CanonicalDigest)]
    public void Rules_RequireIdentityForObservableContractChanges(SemanticContractChange change)
    {
        Assert.IsTrue(SemanticIdentityRules.RequiresNewIdentity(change));
    }
}
