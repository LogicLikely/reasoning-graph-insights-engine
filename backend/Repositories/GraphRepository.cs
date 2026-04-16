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
}
