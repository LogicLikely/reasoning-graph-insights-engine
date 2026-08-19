namespace Backend.Reporting;

public static class PerformanceReportPathResolver
{
    public const string ReportFileName = "performance-runs.json";

    public static string ResolveFromContentRoot(string contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        return Path.GetFullPath(Path.Combine(
            contentRootPath,
            "..",
            "artifacts",
            "performance",
            ReportFileName));
    }
}
