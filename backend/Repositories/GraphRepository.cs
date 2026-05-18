using Backend.Data;
using Backend.Models.Domain;
using Dapper;
using System.Linq;
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
            id AS Id,
            kind AS Kind,
            title AS Title,
            body_text AS BodyText,
            category AS Category,
            tags AS Tags,
            prior AS Prior,
            weight AS Weight,
            confidence AS Confidence,
            importance AS Importance,
            evidence::text AS Evidence
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

        var nodeRows = await connection.QueryAsync<dynamic>(nodesCommand);

        //Individually assigns each property so can do custom stuff with evidence field
        graph.Nodes = nodeRows.Select(row => new GraphNode
        {
            Id = row.Id,
            Kind = row.Kind,
            Title = row.Title,
            BodyText = row.BodyText,
            Category = row.Category,
            Tags = (row.Tags as string[])?.ToList() ?? new List<string>(),
            Prior = row.Prior,
            Weight = row.Weight,
            Confidence = row.Confidence,
            Importance = row.Importance,
            Evidence = row.Evidence == null
                ? null
                : JsonSerializer.Deserialize<GraphEvidenceDetails>((string)row.Evidence, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        }).ToList();

        graph.Edges = (await connection.QueryAsync<GraphEdge>(edgesCommand)).AsList();

        return graph;
    }
}
