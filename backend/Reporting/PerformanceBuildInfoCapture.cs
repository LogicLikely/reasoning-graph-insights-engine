using System.Runtime.InteropServices;
using System.Runtime;

namespace Backend.Reporting;

public static class PerformanceBuildInfoCapture
{
    public static PerformanceBuildInfo Capture(
        string? gitCommit = null,
        bool? dirty = null,
        string? gitBranch = null)
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif

        return new PerformanceBuildInfo
        {
            GitCommit = gitCommit,
            Dirty = dirty,
            GitBranch = gitBranch,
            Configuration = configuration,
            DotNetVersion = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            LogicalProcessorCount = Environment.ProcessorCount,
            ServerGarbageCollection = GCSettings.IsServerGC
        };
    }
}
