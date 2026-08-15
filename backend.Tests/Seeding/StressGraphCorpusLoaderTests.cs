using Backend.Seeding;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace backend.Tests.Seeding;

[TestClass]
public class StressGraphCorpusLoaderTests
{
    private static readonly Lazy<string> ValidCorpusJson = new(CreateValidCorpusJson);

    [TestMethod]
    public async Task LoadAsync_RepositoryRuntimeAssetPassesContract()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "Seed",
            "insights_stress_corpus.json");

        var result = await StressGraphCorpusLoader.LoadAsync(path, CancellationToken.None);

        Assert.AreEqual("public-domain-stress-corpus-v1", result.CorpusId);
        Assert.AreEqual(StressGraphCorpusLoader.RequiredEntryCount, result.EntryCount);
    }

    [TestMethod]
    public async Task LoadAsync_ReturnsValidatedCorpus()
    {
        var path = WriteCorpus(ValidCorpusJson.Value);

        try
        {
            var result = await StressGraphCorpusLoader.LoadAsync(path, CancellationToken.None);

            Assert.AreEqual("test-corpus-v1", result.CorpusId);
            Assert.AreEqual(10_000, result.EntryCount);
            Assert.AreEqual(ValidCorpusJson.Value, result.Json);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task LoadAsync_MissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"missing-stress-corpus-{Guid.NewGuid():N}.json");

        var exception = await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
            StressGraphCorpusLoader.LoadAsync(path, CancellationToken.None));

        Assert.AreEqual(path, exception.FileName);
    }

    [TestMethod]
    public async Task LoadAsync_MalformedJson_ThrowsInvalidDataException()
    {
        await AssertInvalidAsync("{ not-json", "malformed");
    }

    [TestMethod]
    public async Task LoadAsync_WrongSchemaVersion_ThrowsInvalidDataException()
    {
        var document = ParseValidCorpus();
        document["schemaVersion"] = 2;

        await AssertInvalidAsync(document.ToJsonString(), "schemaVersion must be 1");
    }

    [TestMethod]
    public async Task LoadAsync_EntryCountMustBeExactlyTenThousand()
    {
        var document = ParseValidCorpus();
        document["entryCount"] = 9_999;

        await AssertInvalidAsync(document.ToJsonString(), "entryCount must be 10000");
    }

    [TestMethod]
    public async Task LoadAsync_EntriesArrayMustMatchDeclaredCount()
    {
        var document = ParseValidCorpus();
        document["entries"]!.AsArray().RemoveAt(9_999);

        await AssertInvalidAsync(
            document.ToJsonString(),
            "entries must contain 10000 records; received 9999");
    }

    [TestMethod]
    public async Task LoadAsync_EntriesMustHaveExactOrderedIndexes()
    {
        var document = ParseValidCorpus();
        Entry(document, 500)["index"] = 501;

        await AssertInvalidAsync(
            document.ToJsonString(),
            "entries[500].index must be 500");
    }

    [TestMethod]
    public async Task LoadAsync_TitleMustHaveThreeToSixWords()
    {
        var document = ParseValidCorpus();
        Entry(document, 0)["title"] = "Only Two";

        await AssertInvalidAsync(
            document.ToJsonString(),
            "title must contain 3 to 6 words");
    }

    [TestMethod]
    public async Task LoadAsync_TitleMustNotExceedSixWords()
    {
        var document = ParseValidCorpus();
        Entry(document, 0)["title"] = "One Two Three Four Five Six Seven";

        await AssertInvalidAsync(
            document.ToJsonString(),
            "title must contain 3 to 6 words");
    }

    [TestMethod]
    public async Task LoadAsync_TitleMustFitThirtyFiveUnicodeScalars()
    {
        var document = ParseValidCorpus();
        Entry(document, 0)["title"] = $"One Two {RepeatAstral(28)}";

        await AssertInvalidAsync(
            document.ToJsonString(),
            "title must contain at most 35 Unicode characters");
    }

    [TestMethod]
    public async Task LoadAsync_ExcerptMustFitWorstCaseBodyPrefix()
    {
        var document = ParseValidCorpus();
        Entry(document, 0)["excerpt"] = RepeatAstral(
            StressGraphCorpusLoader.MaximumExcerptLength + 1);

        await AssertInvalidAsync(
            document.ToJsonString(),
            "excerpt must contain at most 232 Unicode characters");
    }

    [TestMethod]
    public async Task LoadAsync_AcceptsTitleAndExcerptAtUnicodeScalarLimits()
    {
        var document = ParseValidCorpus();
        Entry(document, 0)["title"] = $"One Two {RepeatAstral(27)}";
        Entry(document, 0)["excerpt"] = RepeatAstral(
            StressGraphCorpusLoader.MaximumExcerptLength);
        var path = WriteCorpus(document.ToJsonString());

        try
        {
            var result = await StressGraphCorpusLoader.LoadAsync(path, CancellationToken.None);

            Assert.AreEqual(StressGraphCorpusLoader.RequiredEntryCount, result.EntryCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task LoadAsync_TitlesMustBeUniqueIgnoringCase()
    {
        var document = ParseValidCorpus();
        Entry(document, 1)["title"] = "DISTINCT CORPUS TITLE 0";

        await AssertInvalidAsync(document.ToJsonString(), "title must be unique");
    }

    [TestMethod]
    public async Task LoadAsync_ExcerptsMustBeUniqueIgnoringCase()
    {
        var document = ParseValidCorpus();
        Entry(document, 1)["excerpt"] = "DISTINCT PUBLIC-DOMAIN EXCERPT NUMBER 0.";

        await AssertInvalidAsync(document.ToJsonString(), "excerpt must be unique");
    }

    [TestMethod]
    public async Task LoadAsync_TagsPropertyIsRequired()
    {
        var document = ParseValidCorpus();
        Entry(document, 0).Remove("tags");

        await AssertInvalidAsync(document.ToJsonString(), "tags must be an array");
    }

    [TestMethod]
    public async Task LoadAsync_NullEntryIsRejectedAsInvalidData()
    {
        var document = ParseValidCorpus();
        document["entries"]!.AsArray()[0] = null;

        await AssertInvalidAsync(document.ToJsonString(), "entries[0] must be an object");
    }

    [TestMethod]
    public async Task LoadAsync_TagsArrayMustNotBeEmpty()
    {
        var document = ParseValidCorpus();
        Entry(document, 0)["tags"] = new JsonArray();

        await AssertInvalidAsync(
            document.ToJsonString(),
            "tags must contain at least one tag");
    }

    [TestMethod]
    public async Task LoadAsync_TagsMustBeSorted()
    {
        var document = ParseValidCorpus();
        Entry(document, 0)["tags"] = new JsonArray("stress", "corpus");

        await AssertInvalidAsync(
            document.ToJsonString(),
            "tags must be unique and sorted in ordinal order");
    }

    [TestMethod]
    public async Task LoadAsync_TagsMustBeUnique()
    {
        var document = ParseValidCorpus();
        Entry(document, 0)["tags"] = new JsonArray("corpus", "corpus");

        await AssertInvalidAsync(
            document.ToJsonString(),
            "tags must be unique and sorted in ordinal order");
    }

    [TestMethod]
    public async Task LoadAsync_TagsMustNotContainBlankValues()
    {
        var document = ParseValidCorpus();
        Entry(document, 0)["tags"] = new JsonArray("", "stress");

        await AssertInvalidAsync(
            document.ToJsonString(),
            "tags[0] must be a non-empty string");
    }

    [DataTestMethod]
    [DataRow("title", " Distinct Corpus Title Zero")]
    [DataRow("excerpt", "Distinct public-domain excerpt zero. ")]
    [DataRow("category", " public-domain")]
    public async Task LoadAsync_TextFieldsMustNotHaveOuterWhitespace(
        string property,
        string value)
    {
        var document = ParseValidCorpus();
        Entry(document, 0)[property] = value;

        await AssertInvalidAsync(
            document.ToJsonString(),
            $"entries[0].{property} must not contain leading or trailing whitespace");
    }

    [TestMethod]
    public async Task LoadAsync_TagMustNotHaveOuterWhitespace()
    {
        var document = ParseValidCorpus();
        Entry(document, 0)["tags"] = new JsonArray("corpus", "stress ");

        await AssertInvalidAsync(
            document.ToJsonString(),
            "entries[0].tags[1] must not contain leading or trailing whitespace");
    }

    [TestMethod]
    public async Task LoadAsync_TextMustUseNfcUnicodeNormalization()
    {
        var document = ParseValidCorpus();
        Entry(document, 0)["title"] = "Cafe\u0301 Has Meaning";

        await AssertInvalidAsync(
            document.ToJsonString(),
            "entries[0].title must use NFC Unicode normalization");
    }

    [TestMethod]
    public async Task LoadAsync_HonorsCancellationBeforeValidationCompletes()
    {
        var path = WriteCorpus(ValidCorpusJson.Value);
        using var source = new CancellationTokenSource();
        source.Cancel();

        try
        {
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() =>
                StressGraphCorpusLoader.LoadAsync(path, source.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static JsonObject ParseValidCorpus() =>
        JsonNode.Parse(ValidCorpusJson.Value)!.AsObject();

    private static JsonObject Entry(JsonObject document, int index) =>
        document["entries"]!.AsArray()[index]!.AsObject();

    private static string RepeatAstral(int count) =>
        string.Concat(Enumerable.Repeat("𐐷", count));

    private static async Task AssertInvalidAsync(string json, string expectedMessage)
    {
        var path = WriteCorpus(json);

        try
        {
            var exception = await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                StressGraphCorpusLoader.LoadAsync(path, CancellationToken.None));

            StringAssert.Contains(exception.Message, expectedMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteCorpus(string json)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"stress-corpus-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string CreateValidCorpusJson()
    {
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            corpusId = "test-corpus-v1",
            entryCount = StressGraphCorpusLoader.RequiredEntryCount,
            entries = Enumerable
                .Range(0, StressGraphCorpusLoader.RequiredEntryCount)
                .Select(index => new
                {
                    index,
                    title = $"Distinct Corpus Title {index}",
                    excerpt = $"Distinct public-domain excerpt number {index}.",
                    category = "public-domain",
                    tags = new[] { "corpus", "stress" }
                })
        });
    }
}
