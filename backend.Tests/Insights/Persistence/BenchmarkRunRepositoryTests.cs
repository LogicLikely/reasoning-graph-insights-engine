using System.Text.Json;
using Backend.Data;
using Backend.Insights.Contracts;
using Backend.Insights.Persistence;
using backend.Tests.Repositories;
using Moq;

namespace backend.Tests.Insights.Persistence;

[TestClass]
public class BenchmarkRunRepositoryTests
{
    [TestMethod]
    public void MutationApi_RequiresAnExplicitRunBoundIntent()
    {
        var mutationNames = new[]
        {
            nameof(IBenchmarkRunRepository.CreateRunAsync),
            nameof(IBenchmarkRunRepository.UpdateLifecycleAsync),
            nameof(IBenchmarkRunRepository.AppendSampleAsync),
            nameof(IBenchmarkRunRepository.AppendOutputAsync)
        };

        foreach (var method in typeof(IBenchmarkRunRepository).GetMethods()
                     .Where(method => mutationNames.Contains(method.Name, StringComparer.Ordinal)))
        {
            Assert.AreEqual(
                typeof(ExplicitBenchmarkRunIntent),
                method.GetParameters()[0].ParameterType,
                $"{method.Name} must require explicit-run intent as its first argument.");
        }

        Assert.ThrowsException<ArgumentException>(() =>
            ExplicitBenchmarkRunIntent.ForRun(Guid.Empty));
    }

    [TestMethod]
    public async Task CreateRunAsync_RejectsMismatchedIntentBeforeOpeningDatabase()
    {
        var factoryMock = CreateUnconfiguredFactoryMock();
        var repository = new BenchmarkRunRepository(factoryMock.Object);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            repository.CreateRunAsync(
                ExplicitBenchmarkRunIntent.ForRun(Guid.NewGuid()),
                BenchmarkPersistenceTestData.Manifest(),
                CancellationToken.None));

        factoryMock.Verify(factory => factory.CreateConnection(), Times.Never);
    }

    [TestMethod]
    public async Task CreateRunAsync_WritesStrictJsonPayloadAndNormalizedIdentityOnce()
    {
        var connection = new FakeDbConnection();
        var repository = CreateRepository(connection);
        var manifest = BenchmarkPersistenceTestData.Manifest();
        var intent = ExplicitBenchmarkRunIntent.ForRun(manifest.RunId);

        await repository.CreateRunAsync(intent, manifest, CancellationToken.None);

        Assert.AreEqual(1, connection.ExecutedCommands.Count);
        var command = connection.ExecutedCommands[0];
        StringAssert.Contains(command.CommandText, "INSERT INTO benchmark.runs");
        Assert.AreEqual(manifest.RunId, command.Parameters["RunId"]);
        Assert.AreEqual("running", command.Parameters["Status"]);
        Assert.AreEqual(DBNull.Value, command.Parameters["FailureKind"]);
        Assert.AreEqual("command-line", command.Parameters["RunnerType"]);
        Assert.AreEqual(manifest.Dataset.DatasetInputFingerprint,
            command.Parameters["DatasetInputFingerprint"]);
        Assert.AreEqual(manifest.Algorithm.SemanticIdentity,
            command.Parameters["AlgorithmSemanticIdentity"]);

        var manifestJson = command.Parameters["ManifestJson"] as string;
        Assert.IsNotNull(manifestJson);
        using var document = JsonDocument.Parse(manifestJson);
        Assert.AreEqual(
            "2026-08-15T14:00:00-04:00",
            document.RootElement.GetProperty("startedAt").GetString(),
            "Canonical JSON must retain the original explicit offset.");
        Assert.AreEqual(
            "running",
            document.RootElement.GetProperty("execution").GetProperty("status").GetString());
        Assert.AreEqual(manifest.CanonicalParameters.Digest,
            document.RootElement.GetProperty("canonicalParameters").GetProperty("digest").GetString());
    }

    [TestMethod]
    public async Task AppendMethods_AllowMultiplePhaseRowsForOneSampleAndPartialOutputCapture()
    {
        var connection = new FakeDbConnection();
        var repository = CreateRepository(connection);
        var intent = ExplicitBenchmarkRunIntent.ForRun(BenchmarkPersistenceTestData.RunId);
        var first = BenchmarkPersistenceTestData.Sample("graph.lookup");
        var second = BenchmarkPersistenceTestData.Sample("nodes.query");
        var partialFailure = new ExecutionOutcome(
            ExecutionStatus.Crashed,
            BenchmarkPersistenceTestData.Failure(FailureKind.Crash));
        var output = BenchmarkPersistenceTestData.Output(partialFailure);

        await repository.AppendSampleAsync(intent, first, CancellationToken.None);
        await repository.AppendSampleAsync(intent, second, CancellationToken.None);
        await repository.AppendOutputAsync(intent, output, CancellationToken.None);

        Assert.AreEqual(3, connection.ExecutedCommands.Count);
        Assert.IsTrue(connection.ExecutedCommands.Take(2).All(command =>
            command.CommandText.Contains("INSERT INTO benchmark.samples", StringComparison.Ordinal)));
        Assert.IsTrue(connection.ExecutedCommands.Take(2).All(command =>
            Equals(command.Parameters["SampleId"], BenchmarkPersistenceTestData.SampleId)));
        Assert.AreEqual("graph.lookup", connection.ExecutedCommands[0].Parameters["Phase"]);
        Assert.AreEqual("nodes.query", connection.ExecutedCommands[1].Parameters["Phase"]);
        Assert.IsTrue(connection.ExecutedCommands.Take(2).All(command =>
            Equals(
                command.Parameters["TimingBoundaryProvenance"],
                "directly-instrumented")));
        StringAssert.Contains(
            connection.ExecutedCommands[0].CommandText,
            "timing_boundary_provenance");
        using (var sampleDocument = JsonDocument.Parse(
                   (string)connection.ExecutedCommands[0].Parameters["SampleJson"]!))
        {
            Assert.AreEqual(
                "directly-instrumented",
                sampleDocument.RootElement.GetProperty("timingBoundaryProvenance").GetString());
            Assert.AreEqual(
                18L,
                sampleDocument.RootElement
                    .GetProperty("operationCounters")
                    .GetProperty("visitedNodeCount")
                    .GetInt64());
        }
        StringAssert.Contains(connection.ExecutedCommands[2].CommandText, "INSERT INTO benchmark.outputs");
        Assert.AreEqual("crashed", connection.ExecutedCommands[2].Parameters["Status"]);
        Assert.AreEqual("crash", connection.ExecutedCommands[2].Parameters["FailureKind"]);
    }

    [TestMethod]
    public async Task UpdateLifecycleAsync_ChangesOnlyLifecyclePayloadFieldsAndPreservesOutcomeKinds()
    {
        var connection = new FakeDbConnection();
        var repository = CreateRepository(connection);
        var intent = ExplicitBenchmarkRunIntent.ForRun(BenchmarkPersistenceTestData.RunId);
        var completedAt = DateTimeOffset.Parse("2026-08-15T14:00:03-04:00");
        var cases = new[]
        {
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            ExecutionOutcome.ValidationFailed(
            [
                new ValidationFailure("graphSlug", "missing", "Graph was not found.")
            ]),
            new ExecutionOutcome(
                ExecutionStatus.Failed,
                BenchmarkPersistenceTestData.Failure(FailureKind.Execution)),
            new ExecutionOutcome(
                ExecutionStatus.TimedOut,
                BenchmarkPersistenceTestData.Failure(FailureKind.Timeout)),
            new ExecutionOutcome(
                ExecutionStatus.Cancelled,
                BenchmarkPersistenceTestData.Failure(FailureKind.Cancellation)),
            new ExecutionOutcome(
                ExecutionStatus.Crashed,
                BenchmarkPersistenceTestData.Failure(FailureKind.Crash))
        };

        foreach (var execution in cases)
        {
            await repository.UpdateLifecycleAsync(
                intent,
                execution,
                completedAt,
                CancellationToken.None);
        }

        Assert.AreEqual(cases.Length, connection.ExecutedCommands.Count);
        foreach (var command in connection.ExecutedCommands)
        {
            StringAssert.Contains(command.CommandText, "UPDATE benchmark.runs");
            StringAssert.Contains(command.CommandText, "'{execution}'");
            StringAssert.Contains(command.CommandText, "'{completedAt}'");
            Assert.IsFalse(command.CommandText.Contains("scenario_key =", StringComparison.Ordinal));
            Assert.IsFalse(command.CommandText.Contains("operation_key =", StringComparison.Ordinal));
            Assert.IsFalse(command.CommandText.Contains("manifest_json = @ManifestJson", StringComparison.Ordinal));
        }

        CollectionAssert.AreEqual(
            new[] { "succeeded", "failed", "failed", "timed-out", "cancelled", "crashed" },
            connection.ExecutedCommands
                .Select(command => command.Parameters["Status"] as string)
                .ToArray());
        CollectionAssert.AreEqual(
            new string?[] { null, "validation", "execution", "timeout", "cancellation", "crash" },
            connection.ExecutedCommands
                .Select(command => command.Parameters["FailureKind"] as string)
                .ToArray());

        using var completedJson = JsonDocument.Parse(
            (string)connection.ExecutedCommands[0].Parameters["CompletedAtJson"]!);
        Assert.AreEqual("2026-08-15T14:00:03-04:00", completedJson.RootElement.GetString());
    }

    [TestMethod]
    public async Task UpdateLifecycleAsync_RequiresCompletionExactlyForTerminalStatesBeforeDatabaseAccess()
    {
        var factoryMock = CreateUnconfiguredFactoryMock();
        var repository = new BenchmarkRunRepository(factoryMock.Object);
        var intent = ExplicitBenchmarkRunIntent.ForRun(BenchmarkPersistenceTestData.RunId);

        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            repository.UpdateLifecycleAsync(
                intent,
                new ExecutionOutcome(ExecutionStatus.Succeeded),
                null,
                CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            repository.UpdateLifecycleAsync(
                intent,
                new ExecutionOutcome(ExecutionStatus.Running),
                DateTimeOffset.Parse("2026-08-15T14:00:03-04:00"),
                CancellationToken.None));

        factoryMock.Verify(factory => factory.CreateConnection(), Times.Never);
    }

    [TestMethod]
    public async Task UpdateLifecycleAsync_UsesAtomicForwardOnlyStateMachineWithExactTerminalReplay()
    {
        var connection = new FakeDbConnection();
        var repository = CreateRepository(connection);

        await repository.UpdateLifecycleAsync(
            ExplicitBenchmarkRunIntent.ForRun(BenchmarkPersistenceTestData.RunId),
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            DateTimeOffset.Parse("2026-08-15T14:00:03-04:00"),
            CancellationToken.None);

        var sql = connection.ExecutedCommands.Single().CommandText;
        StringAssert.Contains(sql, "status = 'queued'");
        StringAssert.Contains(sql, "status = 'running'");
        StringAssert.Contains(sql, "AND status = @Status");
        StringAssert.Contains(sql, "manifest_json->'execution' = @ExecutionJson::jsonb");
        StringAssert.Contains(sql, "manifest_json->'completedAt' = @CompletedAtJson::jsonb");
        StringAssert.Contains(sql, "THEN manifest_json ELSE jsonb_set");
    }

    [TestMethod]
    public async Task GetSnapshotAsync_ReturnsCanonicalPayloadsInStableAppendOrder()
    {
        var connection = new FakeDbConnection();
        var manifest = BenchmarkPersistenceTestData.Manifest(
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            DateTimeOffset.Parse("2026-08-15T14:00:03-04:00"));
        var firstSample = BenchmarkPersistenceTestData.Sample("graph.lookup");
        var secondSample = BenchmarkPersistenceTestData.Sample("nodes.query");
        var firstOutput = BenchmarkPersistenceTestData.Output();
        var secondOutput = BenchmarkPersistenceTestData.Output(
            sampleId: Guid.Parse("33333333-3333-3333-3333-333333333333"));
        connection.WhenCommandContains(
            "FROM benchmark.runs",
            [Row("ManifestJson", SerializeStored(manifest))]);
        connection.WhenCommandContains(
            "FROM benchmark.samples",
            [
                Row("PayloadJson", SerializeStored(firstSample)),
                Row("PayloadJson", SerializeStored(secondSample))
            ]);
        connection.WhenCommandContains(
            "FROM benchmark.outputs",
            [
                Row("PayloadJson", SerializeStored(firstOutput)),
                Row("PayloadJson", SerializeStored(secondOutput))
            ]);
        var repository = CreateRepository(connection);

        var snapshot = await repository.GetSnapshotAsync(
            BenchmarkPersistenceTestData.RunId,
            CancellationToken.None);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(ExecutionStatus.Succeeded, snapshot.Manifest.Execution.Status);
        Assert.AreEqual(2, snapshot.Samples.Count);
        Assert.AreEqual("graph.lookup", snapshot.Samples[0].Phase);
        Assert.AreEqual("nodes.query", snapshot.Samples[1].Phase);
        Assert.AreEqual(
            TimingBoundaryProvenance.DirectlyInstrumented,
            snapshot.Samples[0].TimingBoundaryProvenance);
        Assert.AreEqual(18L, snapshot.Samples[0].OperationCounters?.VisitedNodeCount);
        Assert.AreEqual(2, snapshot.Outputs.Count);
        Assert.AreEqual(firstOutput.SampleId, snapshot.Outputs[0].SampleId);
        Assert.AreEqual(secondOutput.SampleId, snapshot.Outputs[1].SampleId);
        Assert.AreEqual(3, connection.ExecutedCommands.Count);
        StringAssert.Contains(connection.ExecutedCommands[1].CommandText, "ORDER BY entry_id");
        StringAssert.Contains(connection.ExecutedCommands[2].CommandText, "ORDER BY entry_id");
    }

    [TestMethod]
    public async Task GetSnapshotAsync_ReturnsNullWithoutReadingChildrenWhenRunDoesNotExist()
    {
        var connection = new FakeDbConnection();
        connection.WhenCommandContains("FROM benchmark.runs", []);
        var repository = CreateRepository(connection);

        var snapshot = await repository.GetSnapshotAsync(
            BenchmarkPersistenceTestData.RunId,
            CancellationToken.None);

        Assert.IsNull(snapshot);
        Assert.AreEqual(1, connection.ExecutedCommands.Count);
    }

    [TestMethod]
    public async Task CreateRunAsync_RejectsChangedCanonicalParametersBeforeOpeningDatabase()
    {
        var manifest = BenchmarkPersistenceTestData.Manifest();
        var changedValue = JsonSerializer.SerializeToElement(new { includeEvidence = false });
        manifest = manifest with
        {
            CanonicalParameters = new CanonicalParameters(
                changedValue,
                manifest.CanonicalParameters.Digest)
        };
        var factoryMock = CreateUnconfiguredFactoryMock();
        var repository = new BenchmarkRunRepository(factoryMock.Object);

        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            repository.CreateRunAsync(
                ExplicitBenchmarkRunIntent.ForRun(manifest.RunId),
                manifest,
                CancellationToken.None));

        factoryMock.Verify(factory => factory.CreateConnection(), Times.Never);
    }

    [TestMethod]
    public async Task CreateRunAsync_RejectsCompletionBeforeStartBeforeOpeningDatabase()
    {
        var manifest = BenchmarkPersistenceTestData.Manifest(
            new ExecutionOutcome(ExecutionStatus.Succeeded),
            DateTimeOffset.Parse("2026-08-15T13:59:59-04:00"));
        var factoryMock = CreateUnconfiguredFactoryMock();
        var repository = new BenchmarkRunRepository(factoryMock.Object);

        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            repository.CreateRunAsync(
                ExplicitBenchmarkRunIntent.ForRun(manifest.RunId),
                manifest,
                CancellationToken.None));

        factoryMock.Verify(factory => factory.CreateConnection(), Times.Never);
    }

    [TestMethod]
    public async Task AppendSampleAsync_RejectsBlankClassificationInvalidProvenanceAndCountersBeforeDatabaseAccess()
    {
        var factoryMock = CreateUnconfiguredFactoryMock();
        var repository = new BenchmarkRunRepository(factoryMock.Object);
        var intent = ExplicitBenchmarkRunIntent.ForRun(BenchmarkPersistenceTestData.RunId);
        var sample = BenchmarkPersistenceTestData.Sample();

        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            repository.AppendSampleAsync(
                intent,
                sample with
                {
                    Classification = sample.Classification with { IterationKind = " " }
                },
                CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() =>
            repository.AppendSampleAsync(
                intent,
                sample with { TimingBoundaryProvenance = (TimingBoundaryProvenance)999 },
                CancellationToken.None));
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() =>
            repository.AppendSampleAsync(
                intent,
                sample with
                {
                    OperationCounters = sample.OperationCounters! with { CandidateCount = -1 }
                },
                CancellationToken.None));

        factoryMock.Verify(factory => factory.CreateConnection(), Times.Never);
    }

    private static Dictionary<string, object?> Row(string name, object? value)
        => new() { [name] = value };

    private static string SerializeStored<T>(T value)
        => JsonSerializer.Serialize(value, CanonicalJson.CreateSerializerOptions());

    private static BenchmarkRunRepository CreateRepository(FakeDbConnection connection)
    {
        var factoryMock = CreateUnconfiguredFactoryMock();
        factoryMock
            .Setup(factory => factory.CreateConnection())
            .Returns(connection);
        return new BenchmarkRunRepository(factoryMock.Object);
    }

    private static Mock<DbConnectionFactory> CreateUnconfiguredFactoryMock()
        => new(Mock.Of<Microsoft.Extensions.Options.IOptions<Backend.Configuration.DatabaseOptions>>());
}
