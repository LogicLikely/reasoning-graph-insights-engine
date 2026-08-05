using Backend.Data;
using Backend.Models.Domain;
using Backend.Models.Dto;
using Dapper;
using System.Text.Json;

namespace Backend.Repositories;

public class GraphRepository : IGraphRepository
{
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const string GraphSql = """
        SELECT 
            id, slug, title, description
        FROM graphs
        WHERE slug = @Slug;
        """;

    private const string NodesSql = """
        SELECT 
            id,
            kind,
            title,
            body_text AS "BodyText",
            category,
            tags,
            log_odds AS "LogOdds",
            evidence::text AS evidence
        FROM nodes
        WHERE graph_id = @GraphId
        ORDER BY id;
        """;

    private const string EdgesSql = """
        SELECT 
            id, 
            from_node_id AS "From", 
            to_node_id AS "To", 
            kind,
            importance_to_parent AS "ImportanceToParent"
        FROM edges
        WHERE graph_id = @GraphId
        ORDER BY id;
        """;

    private const string ResetDatabaseSql = """
        DROP TABLE IF EXISTS public.edges, public.nodes, public.graphs CASCADE;
        """;

    private readonly DbConnectionFactory _dbConnectionFactory;
    private readonly IHostEnvironment _hostEnvironment;

    public GraphRepository(
        DbConnectionFactory dbConnectionFactory,
        IHostEnvironment hostEnvironment)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _hostEnvironment = hostEnvironment;
    }

    public async Task<Graph?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        var command = new CommandDefinition(
            GraphSql,
            new { Slug = slug },
            cancellationToken: cancellationToken);

        var graphRow = await connection.QuerySingleOrDefaultAsync<GraphRow>(command);
        var graph = graphRow == null ? null : new Graph
        {
            Id = graphRow.Id,
            Slug = graphRow.Slug,
            Title = graphRow.Title,
            Description = graphRow.Description
        };

        if (graph is null)
        {
            return null;
        }

        var nodesCommand = new CommandDefinition(
            NodesSql,
            new { GraphId = graph.Id },
            cancellationToken: cancellationToken);

        var edgesCommand = new CommandDefinition(
            EdgesSql,
            new { GraphId = graph.Id },
            cancellationToken: cancellationToken);

        var nodeRows = (await connection.QueryAsync<NodeRow>(nodesCommand)).ToList();

        //Individually assigns each property so can do custom stuff with evidence field
        graph.Nodes = nodeRows.Select(row => new GraphNode
        {
            Id = row.Id,
            Kind = row.Kind,
            Title = row.Title,
            BodyText = row.BodyText,
            Category = row.Category,
            Tags = row.Tags?.ToList() ?? new List<string>(),
            LogOdds = row.LogOdds,
            Evidence = string.IsNullOrEmpty(row.Evidence)
                ? null
                : JsonSerializer.Deserialize<GraphEvidenceDetails>(row.Evidence, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        }).ToList();


        var edgeRows = (await connection.QueryAsync<EdgeRow>(edgesCommand)).ToList();
        graph.Edges = edgeRows.Select(row => new GraphEdge
        {
            Id = row.Id,
            From = row.From,
            To = row.To,
            Kind = row.Kind,
            ImportanceToParent = row.ImportanceToParent
        }).ToList();

        return graph;
    }

    /// <summary>
    /// Internal helper for mapping query results before shaping the domain graph.
    /// </summary>
    private sealed class GraphRow
    {
        public int Id { get; set; }
        public string Slug { get; set; } = default!;
        public string Title { get; set; } = default!;
        public string? Description { get; set; }
    }

    /// <summary>
    /// Internal helper for mapping query results before shaping graph edges.
    /// </summary>
    private sealed class EdgeRow
    {
        public string Id { get; set; } = default!;
        public string From { get; set; } = default!;
        public string To { get; set; } = default!;
        public string Kind { get; set; } = default!;
        public decimal ImportanceToParent { get; set; } = 1m;
    }

    private sealed class NodeRow
    {
        public string Id { get; set; } = default!;
        public string Kind { get; set; } = default!;
        public string Title { get; set; } = default!;
        public string BodyText { get; set; } = default!;
        public string? Category { get; set; }
        public string[]? Tags { get; set; }
        public decimal LogOdds { get; set; }
        public string? Evidence { get; set; }
    }

    private static decimal GetEvidenceScoreFromLogOdds(decimal logOdds)
    {
        var probability = 1 / (1 + Math.Exp(-(double)logOdds));
        var score = (decimal)probability * 100;
        var boundedScore = Math.Min(99.99m, Math.Max(0.01m, score));

        return decimal.Round(boundedScore, 2);
    }

    private static string? SerializeEvidenceForNode(GraphNodeDto node)
    {
        var evidence = node.Evidence;
        if (string.Equals(node.Kind, "evidence", StringComparison.OrdinalIgnoreCase))
        {
            evidence ??= new GraphEvidenceDto();
            evidence.Score = GetEvidenceScoreFromLogOdds(node.LogOdds);
        }

        return evidence != null
            ? JsonSerializer.Serialize(evidence, EvidenceJsonOptions)
            : null;
    }


    public async Task<bool> DeleteNodeAsync(string slug, string nodeId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            // 1. Delete all edges associated with this node (incoming or outgoing)
            const string DeleteEdgesSql = """
                DELETE FROM edges
                WHERE (from_node_id = @NodeId OR to_node_id = @NodeId)
                AND graph_id = (SELECT id FROM graphs WHERE slug = @Slug);
                """;

            await connection.ExecuteAsync(new CommandDefinition(DeleteEdgesSql,
                new { NodeId = nodeId, Slug = slug }, transaction, cancellationToken: cancellationToken));

            // 2. Delete the node itself
            const string DeleteNodeSql = """
                DELETE FROM nodes
                WHERE id = @NodeId
                AND graph_id = (SELECT id FROM graphs WHERE slug = @Slug);
                """;

            var rowsAffected = await connection.ExecuteAsync(new CommandDefinition(DeleteNodeSql,
                new { NodeId = nodeId, Slug = slug }, transaction, cancellationToken: cancellationToken));

            transaction.Commit();
            return rowsAffected > 0;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<bool> AddNodeAsync(
        string slug,
        GraphNodeDto node,
        string? parentID = null,
        string edgeKind = "support",
        decimal importanceToParent = 1m,
        CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            const string InsertNodeSql = """
                INSERT INTO nodes (
                    id, graph_id, kind, title, body_text, 
                    category, tags, log_odds, evidence
                ) VALUES (
                    @Id, (SELECT id FROM graphs WHERE slug = @Slug), @Kind, @Title, @BodyText, 
                    @Category, @Tags, @LogOdds, @Evidence::jsonb
                );
                """;

            var nodeParams = new
            {
                node.Id,
                Slug = slug,
                node.Kind,
                node.Title,
                node.BodyText,
                node.Category,
                Tags = node.Tags.ToArray(),
                LogOdds = node.LogOdds,
                Evidence = SerializeEvidenceForNode(node)
            };

            await connection.ExecuteAsync(new CommandDefinition(InsertNodeSql, nodeParams, transaction, cancellationToken: cancellationToken));

            if (!string.IsNullOrEmpty(parentID))
            {
                const string InsertEdgeSql = """
                    INSERT INTO edges (id, graph_id, from_node_id, to_node_id, kind, importance_to_parent)
                    VALUES (@EdgeId, (SELECT id FROM graphs WHERE slug = @Slug), @From, @To, @Kind, @ImportanceToParent);
                    """;

                // Invert the From/To so the new supporting node points TO the parent claim.
                var edgeParams = new
                {
                    EdgeId = $"e-{node.Id}",
                    Slug = slug,
                    From = node.Id,
                    To = parentID,
                    Kind = edgeKind,
                    ImportanceToParent = importanceToParent
                };
                await connection.ExecuteAsync(new CommandDefinition(InsertEdgeSql, edgeParams, transaction, cancellationToken: cancellationToken));
            }

            transaction.Commit();
            return true;
        }
        catch
        {
            transaction.Rollback();
            return false;
        }
    }

    public async Task<bool> UpdateNodeAsync(
        string slug,
        string nodeId,
        GraphNodeUpdateDto node,
        CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        const string UpdateNodeSql = """
            UPDATE nodes
            SET
                title = COALESCE(@Title, title),
                body_text = COALESCE(@BodyText, body_text),
                log_odds = COALESCE(@LogOdds, log_odds),
                evidence = CASE
                    WHEN LOWER(kind) = 'evidence' AND @EvidenceScore IS NOT NULL
                        THEN jsonb_set(COALESCE(evidence, '{}'::jsonb), '{score}', to_jsonb(@EvidenceScore), true)
                    ELSE evidence
                END,
                updated_at = now()
            WHERE id = @NodeId
            AND graph_id = (SELECT id FROM graphs WHERE slug = @Slug);
            """;

        var rowsAffected = await connection.ExecuteAsync(new CommandDefinition(
            UpdateNodeSql,
            new
            {
                Slug = slug,
                NodeId = nodeId,
                node.Title,
                node.BodyText,
                node.LogOdds,
                EvidenceScore = node.LogOdds.HasValue
                    ? GetEvidenceScoreFromLogOdds(node.LogOdds.Value)
                    : (decimal?)null
            },
            cancellationToken: cancellationToken));

        return rowsAffected > 0;
    }

    public async Task<bool> AddEdgeAsync(
        string slug,
        GraphEdgeDto edge,
        CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        const string InsertEdgeSql = """
            INSERT INTO edges (id, graph_id, from_node_id, to_node_id, kind, importance_to_parent)
            VALUES (@Id, (SELECT id FROM graphs WHERE slug = @Slug), @From, @To, @Kind, @ImportanceToParent);
            """;

        var edgeId = string.IsNullOrWhiteSpace(edge.Id)
            ? $"e-{edge.From}-{edge.To}"
            : edge.Id;

        var rowsAffected = await connection.ExecuteAsync(new CommandDefinition(
            InsertEdgeSql,
            new
            {
                Id = edgeId,
                Slug = slug,
                edge.From,
                edge.To,
                edge.Kind,
                edge.ImportanceToParent
            },
            cancellationToken: cancellationToken));

        return rowsAffected > 0;
    }

    public async Task<bool> UpdateEdgeAsync(
        string slug,
        string edgeId,
        GraphEdgeUpdateDto edge,
        CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        const string UpdateEdgeSql = """
            UPDATE edges
            SET
                importance_to_parent = COALESCE(@ImportanceToParent, importance_to_parent),
                updated_at = now()
            WHERE id = @EdgeId
            AND graph_id = (SELECT id FROM graphs WHERE slug = @Slug);
            """;

        var rowsAffected = await connection.ExecuteAsync(new CommandDefinition(
            UpdateEdgeSql,
            new
            {
                Slug = slug,
                EdgeId = edgeId,
                edge.ImportanceToParent
            },
            cancellationToken: cancellationToken));

        return rowsAffected > 0;
    }

    public async Task UpdateNodeLogOddsBatchAsync(
        int graphId,
        IReadOnlyDictionary<string, decimal> logOddsByNodeId,
        CancellationToken cancellationToken = default)
    {
        if (logOddsByNodeId.Count == 0)
        {
            return;
        }

        using var connection = _dbConnectionFactory.CreateConnection();

        const string UpdateNodeLogOddsSql = """
            UPDATE nodes
            SET
                log_odds = @LogOdds,
                updated_at = now()
            WHERE id = @NodeId
            AND graph_id = @GraphId;
            """;

        var updateRows = logOddsByNodeId.Select(entry => new
        {
            GraphId = graphId,
            NodeId = entry.Key,
            LogOdds = entry.Value
        });

        await connection.ExecuteAsync(new CommandDefinition(
            UpdateNodeLogOddsSql,
            updateRows,
            cancellationToken: cancellationToken));
    }

    public async Task ResetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var seedSqlPath = Path.Combine(
            _hostEnvironment.ContentRootPath,
            "Data",
            "Sql",
            "insights_seed.sql");

        if (!File.Exists(seedSqlPath))
        {
            throw new FileNotFoundException("Database seed SQL file was not found.", seedSqlPath);
        }

        var seedSql = await File.ReadAllTextAsync(seedSqlPath, cancellationToken);

        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                ResetDatabaseSql,
                transaction: transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                seedSql,
                transaction: transaction,
                cancellationToken: cancellationToken));

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
