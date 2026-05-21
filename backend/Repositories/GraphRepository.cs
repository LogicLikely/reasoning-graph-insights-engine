using Backend.Data;
using Backend.Models.Domain;
using Dapper;

namespace Backend.Repositories;

public class GraphRepository : IGraphRepository
{
    private const string GraphSql = """
        SELECT id, slug, title, description
        FROM graphs
        WHERE slug = @Slug;
        """;

    private const string NodesSql = """
        SELECT id, kind, title, body_text AS BodyText
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

        graph.Nodes = (await connection.QueryAsync<GraphNode>(nodesCommand)).AsList();
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
}
