using System.Security.Cryptography;
using System.Text;

namespace Backend.Seeding;

public sealed record DatabaseResetTargetExpectation
{
    public DatabaseResetTargetExpectation(string databaseName, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        if (!DatabaseResetTargetIdentity.IsValidFingerprint(fingerprint))
        {
            throw new ArgumentException(
                "Database reset target fingerprint must use the supported opaque SHA-256 format.",
                nameof(fingerprint));
        }

        DatabaseName = databaseName;
        Fingerprint = fingerprint;
    }

    public string DatabaseName { get; }

    public string Fingerprint { get; }
}

public enum DatabaseResetIdentityMismatchKind
{
    DatabaseName,
    TargetFingerprint
}

public sealed class DatabaseResetIdentityMismatchException : InvalidOperationException
{
    public DatabaseResetIdentityMismatchException(DatabaseResetIdentityMismatchKind mismatchKind)
        : base("The connected PostgreSQL target does not match the destructive reset expectation.")
    {
        MismatchKind = mismatchKind;
    }

    public DatabaseResetIdentityMismatchKind MismatchKind { get; }
}

public static class DatabaseResetTargetIdentity
{
    public const string FingerprintVersion = "postgres-reset-target-v1";

    // Both the benchmark runner and API execute this query independently.
    // jsonb supplies an unambiguous tuple representation while explicit UTC
    // timestamp formatting avoids session timezone differences.
    public const string ProbeSql = """
        SELECT
            current_database() AS "DatabaseName",
            jsonb_build_array(
                current_database(),
                COALESCE(inet_server_addr()::text, 'local-socket'),
                COALESCE(inet_server_port(), 0),
                to_char(
                    pg_postmaster_start_time() AT TIME ZONE 'UTC',
                    'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'))::text AS "IdentityTuple";
        """;

    public static string ComputeFingerprint(string identityTuple)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityTuple);
        var bytes = Encoding.UTF8.GetBytes($"{FingerprintVersion}\n{identityTuple}");
        return $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
    }

    public static bool IsValidFingerprint(string? fingerprint)
    {
        if (fingerprint is not { Length: 71 } ||
            !fingerprint.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in fingerprint.AsSpan(7))
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
