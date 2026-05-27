using Backend.Data;
using Backend.Models.Domain;
using Backend.Models.Dto;
using Dapper;
using System.Text.Json;

namespace Backend.Repositories;

public class GraphRepository : IGraphRepository
{
    private const string GraphSql = """
        SELECT id, slug, title, description
        FROM graphs
        WHERE slug = @Slug;
        """;

    private const string NodesSql = """
        SELECT 
            id, kind, title, body_text AS BodyText,
            category, 
            tags, 
            prior, 
            weight, 
            confidence, 
            importance, 
            evidence
        FROM nodes
        WHERE graph_id = @GraphId
        ORDER BY id;
        """;

    private const string EdgesSql = """
        SELECT id, from_node_id AS "From", to_node_id AS "To", kind
        FROM edges
        WHERE graph_id = @GraphId
        ORDER BY id;
        """;

    private readonly DbConnectionFactory _dbConnectionFactory;

    public GraphRepository(DbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
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

        var graph = await connection.QuerySingleOrDefaultAsync<Graph>(command);

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

        // Using dynamic to handle the explicit deserialization of the JSONB evidence column
        var nodeRows = await connection.QueryAsync<dynamic>(nodesCommand);
        graph.Nodes = nodeRows.Select(row => new GraphNode
        {
            Id = row.id,
            Kind = row.kind,
            Title = row.title,
            BodyText = row.bodytext,
            Category = row.category,
            Tags = (row.tags as string[] ?? Array.Empty<string>()).ToList(),
            Prior = row.prior,
            Weight = row.weight,
            Confidence = row.confidence,
            Importance = row.importance,
            Evidence = row.evidence != null
                ? JsonSerializer.Deserialize<GraphEvidence>((string)row.evidence,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null
        }).ToList();

        graph.Edges = (await connection.QueryAsync<GraphEdge>(edgesCommand)).AsList();

        return graph;
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
                    category, tags, prior, weight, 
                    confidence, importance, evidence
                ) VALUES (
                    @Id, (SELECT id FROM graphs WHERE slug = @Slug), @Kind, @Title, @BodyText, 
                    @Category, @Tags, @Prior, @Weight, 
                    @Confidence, @Importance, @Evidence::jsonb
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
                node.Prior,
                node.Weight,
                node.Confidence,
                node.Importance,
                Evidence = node.Evidence != null ? JsonSerializer.Serialize(node.Evidence) : null
            };

            await connection.ExecuteAsync(new CommandDefinition(InsertNodeSql, nodeParams, transaction, cancellationToken: cancellationToken));

            if (!string.IsNullOrEmpty(parentID))
            {
                const string InsertEdgeSql = """
                    INSERT INTO edges (id, graph_id, from_node_id, to_node_id, kind)
                    VALUES (@EdgeId, (SELECT id FROM graphs WHERE slug = @Slug), @From, @To, 'support');
                    """;

                var edgeParams = new { EdgeId = $"e-{node.Id}", Slug = slug, From = parentID, To = node.Id };
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
}
