using Backend.Data;
using Backend.Models.Domain;
using Dapper;
using System.Linq;
using System.Text.Json;

namespace Backend.Repositories;

public class GraphRepository : IGraphRepository
{
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
            body_text,
            category,
            tags,
            prior,
            weight,
            confidence,
            importance,
            evidence::text AS evidence
        FROM nodes
        WHERE graph_id = @GraphId
        ORDER BY id;
        """;

    private const string EdgesSql = """
        SELECT 
            id, 
            from_node_id, 
            to_node_id, 
            kind
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

        // Use an intermediate row type to ensure robust mapping from PostgreSQL's lowercase column names
        var graphRow = await connection.QuerySingleOrDefaultAsync<GraphRow>(command);
        var graph = graphRow == null ? null : new Graph
        {
            Id = graphRow.id,
            Slug = graphRow.slug,
            Title = graphRow.title,
            Description = graphRow.description
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
            Id = row.id,
            Kind = row.kind,
            Title = row.title,
            BodyText = row.body_text,
            Category = row.category,
            Tags = row.tags?.ToList() ?? new List<string>(),
            Prior = row.prior,
            Weight = row.weight,
            Confidence = row.confidence,
            Importance = row.importance,
            Evidence = string.IsNullOrEmpty(row.evidence)
                ? null
                : JsonSerializer.Deserialize<GraphEvidenceDetails>(row.evidence, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        }).ToList();

        var edgeRows = (await connection.QueryAsync<EdgeRow>(edgesCommand)).ToList();
        graph.Edges = edgeRows.Select(row => new GraphEdge
        {
            Id = row.id,
            From = row.from_node_id,
            To = row.to_node_id,
            Kind = row.kind
        }).ToList();

        return graph;
    }

    /// <summary>
    /// Internal helper to match exact Postgres lowercase column names for the main graph object.
    /// </summary>
    private sealed class GraphRow
    {
        public int id { get; set; }
        public string slug { get; set; } = default!;
        public string title { get; set; } = default!;
        public string? description { get; set; }
    }

    /// <summary>
    /// Internal helper to match exact Postgres lowercase column names for edges.
    /// </summary>
    private sealed class EdgeRow
    {
        public string id { get; set; } = default!;
        public string from_node_id { get; set; } = default!;
        public string to_node_id { get; set; } = default!;
        public string kind { get; set; } = default!;
    }

    private sealed class NodeRow
    {
        public string id { get; set; } = default!;
        public string kind { get; set; } = default!;
        public string title { get; set; } = default!;
        public string body_text { get; set; } = default!;
        public string? category { get; set; }
        public string[]? tags { get; set; }
        public decimal? prior { get; set; }
        public decimal? weight { get; set; }
        public decimal? confidence { get; set; }
        public decimal? importance { get; set; }
        public string? evidence { get; set; }
    }
}
