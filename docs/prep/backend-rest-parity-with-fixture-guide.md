# Bringing the REST Graph Endpoint Up to Par with the Fixture

## Purpose

This guide explains, step by step, what a junior engineer would need to do to make the backend REST response support the same node details that the frontend already knows how to display in fixture mode.

Today, the frontend `GraphDetailsPanel` can render rich node metadata from the local fixture, but the backend endpoint only returns a much smaller shape for each node.

The goal is to make the API contract rich enough that switching from fixture mode to API mode does not reduce what the UI can show.

## What the Frontend Expects Today

The frontend details panel already supports the following node fields:

- `id`
- `kind`
- `title`
- `bodyText`
- `category`
- `tags`
- `prior`
- `confidence`
- `weight`
- `importance`
- `evidence.type`
- `evidence.score`
- `evidence.rationale`

You can see that in:

- [GraphDetailsPanel.tsx](/Users/nuttzy/src/logiclikely/reasoning-graph-insights-engine/frontend/src/components/graph/GraphDetailsPanel.tsx)
- [sampleGraph.ts](/Users/nuttzy/src/logiclikely/reasoning-graph-insights-engine/frontend/src/fixtures/sampleGraph.ts)

## What the Backend Returns Today

The backend currently models and returns only this node shape:

- `id`
- `kind`
- `title`
- `bodyText`

You can see that gap in:

- [GraphNode.cs](/Users/nuttzy/src/logiclikely/reasoning-graph-insights-engine/backend/Models/Domain/GraphNode.cs)
- [GraphNodeDto.cs](/Users/nuttzy/src/logiclikely/reasoning-graph-insights-engine/backend/Models/Dto/GraphNodeDto.cs)
- [GraphRepository.cs](/Users/nuttzy/src/logiclikely/reasoning-graph-insights-engine/backend/Repositories/GraphRepository.cs)
- [GraphService.cs](/Users/nuttzy/src/logiclikely/reasoning-graph-insights-engine/backend/Services/GraphService.cs)

## Important Observation

This is a good REST lesson: sometimes the database already contains the data you need, but your API contract still falls short because:

1. the domain model does not define the fields
2. the SQL query does not select the fields
3. the DTO does not expose the fields
4. the service does not map the fields
5. the seed data may not populate every field consistently

That is exactly what is happening here.

## Step 1: Compare the UI Contract to the API Contract

Before changing code, write down the expected node contract from the frontend fixture and compare it with the backend DTO.

This exercise helps a junior engineer avoid a very common mistake: changing only the SQL query and forgetting that the API response class still does not include the new fields.

The fastest way to do the comparison is:

1. inspect `GraphFixtureNode` in the frontend fixture
2. inspect `GraphNodeDto` in the backend
3. list every field that exists in the fixture but not in the DTO

The missing fields are the initial backlog.

## Step 2: Confirm the Database Already Has Most of the Needed Data

Check the `nodes` table definition in:

- [insights_seed.sql](/Users/nuttzy/src/logiclikely/reasoning-graph-insights-engine/backend/data/sql/insights_seed.sql)

The schema already includes:

- `category`
- `tags`
- `prior`
- `weight`
- `confidence`
- `importance`
- `evidence`

This means the first half of the problem is not a database migration problem. It is mostly an API shaping problem.

However, it is also worth noticing that some seeded rows currently leave several of those values as `NULL`. That means code changes alone may still produce incomplete UI data until the seed data is improved.

## Step 3: Expand the Backend Domain Model

Update the backend domain model so it can hold the richer node data from the database.

The first file to change is:

- [GraphNode.cs](/Users/nuttzy/src/logiclikely/reasoning-graph-insights-engine/backend/Models/Domain/GraphNode.cs)

It should grow from the current four fields to include:

- `Category`
- `Tags`
- `Prior`
- `Weight`
- `Confidence`
- `Importance`
- `Evidence`

### Recommended Property Shapes

Use types that reflect the database and frontend contract clearly:

- `string? Category`
- `List<string> Tags` or `string[] Tags`
- `decimal? Prior`
- `decimal? Weight`
- `decimal? Confidence`
- `decimal? Importance`
- `GraphEvidenceDetails? Evidence`

You will also need to add a small class for evidence, for example:

- `Type`
- `Score`
- `Rationale`

This can live next to `GraphNode` in the domain layer.

## Step 4: Expand the API DTOs

Next, update:

- [GraphNodeDto.cs](/Users/nuttzy/src/logiclikely/reasoning-graph-insights-engine/backend/Models/Dto/GraphNodeDto.cs)

The DTO should expose the same fields the frontend uses so the JSON response is compatible with the fixture shape.

This is where many juniors miss the key idea:

- the domain model is for backend code
- the DTO is the public REST contract
- both need to be updated

If the DTO is not updated, the new fields will never reach the browser even if the repository fetched them correctly.

You should also add a DTO for evidence details if you decide to keep separate domain and DTO types for nested evidence data.

## Step 5: Update the Repository SQL Query

Once the model can hold the data, update the node query in:

- [GraphRepository.cs](/Users/nuttzy/src/logiclikely/reasoning-graph-insights-engine/backend/Repositories/GraphRepository.cs)

Right now it selects only:

- `id`
- `kind`
- `title`
- `body_text`

It should also select:

- `category`
- `tags`
- `prior`
- `weight`
- `confidence`
- `importance`
- `evidence`

### Why Aliases Matter

The backend uses Dapper, so column names must line up with C# property names.

For example:

- `body_text AS BodyText`

You should do the same for any database column that does not naturally match the C# property name.

Examples:

- `category AS Category`
- `prior AS Prior`
- `importance AS Importance`

Some columns already match closely enough, but being explicit in SQL often makes the mapping easier to reason about.

## Step 6: Decide How to Map the `evidence` JSON

The `evidence` column is stored as `jsonb`, so this step deserves special attention.

There are two broad approaches:

### Option A: Read JSON and Deserialize It

Select the evidence column and map it into a string or raw JSON value, then deserialize it into a typed C# class using `System.Text.Json`.

This is often the clearest teaching option because it makes the transformation explicit.

The flow would be:

1. read the raw JSON value from SQL
2. deserialize it into `GraphEvidenceDetails`
3. assign that object to the node

### Option B: Use a Dapper/Npgsql Mapping Strategy

You may also be able to configure mapping so the JSON column binds directly into a typed object.

This can be cleaner once a team is comfortable with the stack, but it hides more magic. For a junior engineer, explicit deserialization is usually easier to understand and debug.

### Recommendation

For teaching purposes, prefer explicit deserialization first.

It reinforces the lesson that REST DTOs are shaped intentionally, not automatically.

## Step 7: Verify `tags` Maps Correctly

The `tags` column is a Postgres text array.

Make sure it comes through as a collection type the frontend can use naturally. A good target is:

- `string[]`

or

- `List<string>`

Then map that cleanly into the DTO.

This is another useful junior-level lesson: SQL arrays and JSON objects often need explicit thought even when simple scalar columns do not.

## Step 8: Update the Service Mapping

After the repository returns richer domain objects, update:

- [GraphService.cs](/Users/nuttzy/src/logiclikely/reasoning-graph-insights-engine/backend/Services/GraphService.cs)

Right now it maps only four node fields into `GraphNodeDto`.

It should also map:

- `Category`
- `Tags`
- `Prior`
- `Weight`
- `Confidence`
- `Importance`
- `Evidence`

If there is a separate DTO type for evidence, map that nested object as well.

This is where the API response shape is finalized.

## Step 9: Review the JSON Naming

The frontend fixture uses camel-style names such as:

- `bodyText`
- `category`
- `importance`
- `evidence`

The backend C# classes use PascalCase properties. ASP.NET typically serializes those into camelCase JSON, but a junior engineer should still confirm the actual output.

Do not assume the payload shape matches the fixture until you inspect a real JSON response.

Good check:

1. run the backend
2. call `GET /api/graphs/sample-medium`
3. confirm the response JSON contains the expected fields and nested evidence object

## Step 10: Improve the Seed Data

Even after the code is fixed, the API may still not feel as rich as the fixture if the seed data leaves many fields blank.

For example, the schema has columns for:

- `confidence`
- `weight`
- `importance`

But many inserted node rows currently leave them null.

That means the frontend details panel may technically support the field, but the user still will not see it often.

To reach parity with the fixture experience, update the seed data in:

- [insights_seed.sql](/Users/nuttzy/src/logiclikely/reasoning-graph-insights-engine/backend/data/sql/insights_seed.sql)

Populate the seeded nodes more consistently so the API response feels as complete as the fixture response.

This is an important product lesson:

- schema parity is not the same as data parity

## Step 11: Decide Whether Edge Metadata Should Also Be Exposed

This task started with node details, but while reviewing the backend you should also notice that edges in the database carry more information too:

- `weight`
- `confidence`
- `rationale`

The current edge DTO only returns:

- `id`
- `from`
- `to`
- `kind`

If the long-term product direction includes richer edge inspection, the same contract-alignment exercise should be repeated for edges.

This is not required just to fix `GraphDetailsPanel`, but it is worth calling out while the team is already touching the graph API.

## Step 12: Add or Update Backend Tests

Once the code is changed, add tests that prove the new fields make it through the layers.

Useful places:

- repository tests for SQL-to-domain mapping
- service tests for domain-to-DTO mapping
- controller tests for endpoint behavior

### Minimum Assertions to Add

At a minimum, tests should verify that:

- `category` is returned when present
- `tags` is returned as a collection
- numeric fields like `prior` and `importance` are returned correctly
- `evidence.type`, `evidence.score`, and `evidence.rationale` are returned correctly
- null values do not crash the endpoint

This teaches another good REST habit: when you add fields to a contract, write tests that protect the contract.

## Step 13: Validate End to End From the Frontend

After the backend is updated, switch the frontend out of fixture mode and verify the actual user experience.

The practical checklist is:

1. set the frontend to use the API instead of the fixture
2. load the Demo page
3. click several node types
4. confirm the details panel shows the same categories of information as it did in fixture mode
5. check at least one evidence node to confirm nested evidence data appears correctly

This final step matters because the real goal is not “the backend compiles.” The real goal is “the UI experience stays equally rich when it is powered by the API.”

## Suggested Order of Work

If assigning this to a junior engineer, the safest order is:

1. compare fixture shape to API DTO shape
2. expand `GraphNode` and add evidence model classes
3. expand `GraphNodeDto` and any nested DTOs
4. update repository SQL to select the missing columns
5. implement JSON deserialization for `evidence`
6. update service mapping
7. add tests
8. improve seed data where values are currently missing
9. verify the endpoint JSON manually
10. test the frontend in API mode

This order reduces confusion because each layer builds on the one below it.

## Definition of Done

The work should be considered complete when all of the following are true:

- the backend endpoint returns the same node detail fields the fixture provides
- evidence data is returned as a structured nested object
- tags are returned as a collection
- null values are handled safely
- backend tests cover the richer contract
- the frontend Demo page shows comparable detail in API mode and fixture mode

## Key Lesson for a Junior Engineer

The main lesson is that REST work is not just “write a query.”

To expose new data successfully, you usually need to align five things:

1. database schema
2. repository query
3. domain model
4. DTO contract
5. frontend expectations

If any one of those layers is incomplete, the user sees an incomplete product even though the missing data may exist somewhere else in the system.
