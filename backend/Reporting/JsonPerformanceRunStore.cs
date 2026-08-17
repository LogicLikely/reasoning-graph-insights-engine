using System.Text.Json;

namespace Backend.Reporting;

public sealed class JsonPerformanceRunStore : IPerformanceRunStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _accessLock = new(1, 1);

    public JsonPerformanceRunStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public async Task<PerformanceReportDocument> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        await _accessLock.WaitAsync(cancellationToken);
        try
        {
            return await ReadReportFileAsync(cancellationToken);
        }
        finally
        {
            _accessLock.Release();
        }
    }

    public async Task<PerformanceRunRecord> AppendAsync(
        PerformanceRunRecord run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        await _accessLock.WaitAsync(cancellationToken);
        try
        {
            var report = await ReadReportFileAsync(cancellationToken);
            var storedRun = run with
            {
                RunNumber = report.Runs.Count == 0
                    ? 1
                    : checked(report.Runs.Max(existingRun => existingRun.RunNumber) + 1)
            };

            report.Runs.Add(storedRun);
            await WriteReportAtomicallyAsync(report, cancellationToken);

            return storedRun;
        }
        finally
        {
            _accessLock.Release();
        }
    }

    public void Dispose()
    {
        _accessLock.Dispose();
    }

    private async Task<PerformanceReportDocument> ReadReportFileAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new PerformanceReportDocument();
        }

        try
        {
            await using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16_384,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var report = await JsonSerializer.DeserializeAsync<PerformanceReportDocument>(
                stream,
                SerializerOptions,
                cancellationToken);

            if (report is null)
            {
                throw new InvalidDataException(
                    $"Performance report '{_filePath}' contains no JSON document.");
            }

            if (report.SchemaVersion != PerformanceReportDocument.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Performance report '{_filePath}' uses unsupported schema version " +
                    $"{report.SchemaVersion}.");
            }

            if (report.Runs is null)
            {
                throw new InvalidDataException(
                    $"Performance report '{_filePath}' has a null runs collection.");
            }

            return report;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Performance report '{_filePath}' is not valid JSON.",
                exception);
        }
    }

    private async Task WriteReportAtomicallyAsync(
        PerformanceReportDocument report,
        CancellationToken cancellationToken)
    {
        var directoryPath = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException(
                $"Performance report path '{_filePath}' has no parent directory.");
        Directory.CreateDirectory(directoryPath);

        var temporaryPath = Path.Combine(
            directoryPath,
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16_384,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    report,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(_filePath))
            {
                File.Move(temporaryPath, _filePath, overwrite: true);
            }
            else
            {
                File.Move(temporaryPath, _filePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
