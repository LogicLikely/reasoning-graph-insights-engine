using System.Diagnostics;
using Backend.Insights.Contracts;

namespace Backend.Insights.Benchmarking;

public interface ISourceRevisionProvider
{
    SourceRevision GetSourceRevision();
}

public sealed class GitSourceRevisionProvider : ISourceRevisionProvider
{
    public const string RevisionEnvironmentVariable = "LOGICLIKELY_SOURCE_REVISION";
    public const string DirtyEnvironmentVariable = "LOGICLIKELY_SOURCE_DIRTY";

    public SourceRevision GetSourceRevision()
    {
        try
        {
            var overriddenRevision = Environment.GetEnvironmentVariable(RevisionEnvironmentVariable)?.Trim();
            if (!string.IsNullOrWhiteSpace(overriddenRevision))
            {
                if (!IsHexRevision(overriddenRevision) ||
                    !TryReadDirty(Environment.GetEnvironmentVariable(DirtyEnvironmentVariable), out var dirty))
                {
                    throw new InvalidOperationException(
                        $"{RevisionEnvironmentVariable} must be a 7-64 character hexadecimal Git SHA and " +
                        $"{DirtyEnvironmentVariable} must be true/false or 1/0.");
                }

                return new SourceRevision(overriddenRevision.ToLowerInvariant(), dirty);
            }

            var repositoryRoot = FindRepositoryRoot();
            if (repositoryRoot is null)
            {
                throw Unavailable();
            }

            var revision = RunGit(repositoryRoot, ["rev-parse", "--verify", "HEAD"]);
            if (revision.ExitCode != 0 || !IsHexRevision(revision.Output))
            {
                throw Unavailable();
            }

            var status = RunGit(repositoryRoot, ["status", "--porcelain", "--untracked-files=normal"]);
            return new SourceRevision(
                revision.Output.ToLowerInvariant(),
                status.ExitCode != 0 || status.Output.Length > 0);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Unavailable(exception);
        }
    }

    private static InvalidOperationException Unavailable(Exception? inner = null) => new(
        "An honest Git source revision could not be determined. Run inside a Git worktree or set " +
        $"{RevisionEnvironmentVariable} and {DirtyEnvironmentVariable} explicitly.",
        inner);

    private static string? FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                    File.Exists(Path.Combine(directory.FullName, ".git")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static (int ExitCode, string Output) RunGit(
        string repositoryRoot,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The git process could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(2_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Git source revision discovery timed out.");
        }

        Task.WaitAll(outputTask, errorTask);
        return (process.ExitCode, outputTask.Result.Trim());
    }

    private static bool IsHexRevision(string value) =>
        value.Length is >= 7 and <= 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool TryReadDirty(string? value, out bool dirty)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "1":
            case "true":
                dirty = true;
                return true;
            case "0":
            case "false":
                dirty = false;
                return true;
            default:
                dirty = false;
                return false;
        }
    }
}
