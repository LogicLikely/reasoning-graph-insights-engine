using Backend.Insights.Workers;

namespace Backend.Insights.Benchmarking;

public interface IAnalysisWorkerCommandProvider
{
    WorkerProcessCommand GetCommand();
}

public sealed class PublishedAnalysisWorkerCommandProvider : IAnalysisWorkerCommandProvider
{
    public const string WorkerPathEnvironmentVariable = "LOGICLIKELY_INSIGHTS_ANALYSIS_WORKER_PATH";

    public WorkerProcessCommand GetCommand()
    {
        var assemblyPath = Environment.GetEnvironmentVariable(WorkerPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            assemblyPath = Path.Combine(AppContext.BaseDirectory, "backend.AnalysisWorker.dll");
        }

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException(
                $"Analysis worker was not found. Set {WorkerPathEnvironmentVariable} or publish it beside the runner.",
                assemblyPath);
        }

        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(dotnet)) dotnet = "dotnet";
        return new WorkerProcessCommand(dotnet, [assemblyPath], Path.GetDirectoryName(assemblyPath));
    }
}
