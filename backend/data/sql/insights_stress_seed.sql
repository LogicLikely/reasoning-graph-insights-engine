--
-- Parameterized deterministic stress-graph generator.
-- Expected Dapper parameters: GraphId, Slug, Title, Description, Shape,
-- NodeCount, CounterCandidateCount, InitialTargetLogOdds,
-- CounterLeafLogBayesFactor, ProbabilityGivenParent,
-- ProbabilityGivenNotParent, CorpusJson, CorpusEntryCount.
-- This script assumes insights_seed.sql has already rebuilt the base schema.
--

INSERT INTO public.graphs (
    id,
    slug,
    title,
    description,
    created_at,
    updated_at
) OVERRIDING SYSTEM VALUE VALUES (
    @GraphId,
    @Slug,
    @Title,
    @Description,
    TIMESTAMPTZ '2026-08-15 00:00:00+00',
    TIMESTAMPTZ '2026-08-15 00:00:00+00'
);

WITH corpus AS (
    SELECT
        (entry.value->>'index')::integer AS corpus_index,
        entry.value->>'title' AS title,
        entry.value->>'excerpt' AS excerpt,
        entry.value->>'category' AS category,
        ARRAY(
            SELECT tag.value
            FROM jsonb_array_elements_text(entry.value->'tags') AS tag(value)
        )::text[] AS tags
    FROM jsonb_array_elements((@CorpusJson::jsonb)->'entries') AS entry(value)
), vocabulary AS (
    SELECT
        ARRAY[
            'experimental', 'media-analysis', 'observational', 'textual', 'video'
        ]::text[] AS evidence_types
), generated AS (
    SELECT
        series.node_index,
        CASE
            WHEN series.node_index = 0 THEN 'root'
            WHEN @Shape <> 'deep'
                AND series.node_index >= @NodeCount - @CounterCandidateCount
                THEN 'objection'
            WHEN @Shape <> 'deep'
                AND (
                    series.node_index % 5 = 0
                    OR (
                        series.node_index >= @NodeCount - (2 * @CounterCandidateCount)
                        AND series.node_index < @NodeCount - @CounterCandidateCount
                        AND series.node_index % 5 = 1
                    )
                )
                THEN 'evidence'
            WHEN @Shape = 'deep' AND series.node_index % 5 = 0 THEN 'evidence'
            WHEN @Shape = 'deep' AND series.node_index % 10 = 2 THEN 'objection'
            ELSE 'claim'
        END AS kind,
        corpus.title,
        corpus.excerpt,
        corpus.category,
        corpus.tags,
        vocabulary.evidence_types[1 + ((series.node_index / 5) % 5)] AS evidence_type,
        CASE
            WHEN @Shape <> 'deep' THEN 50
            ELSE 35 + (5 * ((series.node_index / 5) % 7))
        END AS evidence_score
    FROM generate_series(0, @NodeCount - 1) AS series(node_index)
    INNER JOIN corpus
        ON corpus.corpus_index = series.node_index % @CorpusEntryCount
    CROSS JOIN vocabulary
), payload AS (
    SELECT
        generated.*,
        format('n-%s', lpad(generated.node_index::text, 5, '0')) AS node_id,
        CASE
            WHEN generated.kind = 'evidence' THEN ln(
                generated.evidence_score::double precision /
                (100 - generated.evidence_score)::double precision
            )
            ELSE 0::double precision
        END AS authored_log_odds
    FROM generated
)
INSERT INTO public.nodes (
    id,
    graph_id,
    kind,
    title,
    body_text,
    category,
    tags,
    prior_odds,
    posterior_odds,
    evidence,
    created_at,
    updated_at
)
SELECT
    payload.node_id,
    graph.id,
    payload.kind,
    payload.title,
    format(
        '%s %s — %s',
        initcap(payload.kind),
        lpad(payload.node_index::text, 5, '0'),
        payload.excerpt
    ),
    payload.category,
    payload.tags,
    CASE
        WHEN @Shape <> 'deep' AND payload.kind = 'root'
            THEN @InitialTargetLogOdds
        ELSE 0
    END,
    CASE
        WHEN @Shape <> 'deep' AND payload.kind = 'root'
            THEN @InitialTargetLogOdds
        WHEN @Shape <> 'deep' AND payload.kind = 'objection'
            THEN @CounterLeafLogBayesFactor
        ELSE payload.authored_log_odds
    END,
    CASE
        WHEN payload.kind = 'evidence' THEN jsonb_build_object(
            'type', payload.evidence_type,
            'score', payload.evidence_score,
            'rationale', format(
                'Synthetic %s record %s assigns score %s for repeatable rhetoric graph stress testing.',
                payload.evidence_type,
                payload.node_id,
                payload.evidence_score
            )
        )
        ELSE NULL
    END,
    TIMESTAMPTZ '2026-08-15 00:00:00+00',
    TIMESTAMPTZ '2026-08-15 00:00:00+00'
FROM payload
CROSS JOIN LATERAL (
    SELECT id
    FROM public.graphs
    WHERE slug = @Slug
) AS graph;

WITH generated_edges AS (
    SELECT
        series.node_index,
        CASE @Shape
            WHEN 'balanced' THEN (series.node_index - 1) / 4
            WHEN 'wide' THEN 0
            WHEN 'deep' THEN series.node_index - 1
            WHEN 'shared-diamond' THEN (series.node_index - 1) / 4
        END AS parent_index
    FROM generate_series(1, @NodeCount - 1) AS series(node_index)
)
INSERT INTO public.edges (
    id,
    graph_id,
    from_node_id,
    to_node_id,
    kind,
    probability_given_parent,
    probability_given_not_parent,
    created_at,
    updated_at
)
SELECT
    format('e-p-%s', lpad(generated_edges.node_index::text, 5, '0')),
    graph.id,
    format('n-%s', lpad(generated_edges.node_index::text, 5, '0')),
    format('n-%s', lpad(generated_edges.parent_index::text, 5, '0')),
    CASE
        WHEN @Shape = 'deep' AND generated_edges.node_index % 2 = 0 THEN 'rebut'
        ELSE 'support'
    END,
    CASE
        WHEN @Shape = 'deep' AND generated_edges.node_index % 2 = 1 THEN 0.5005
        WHEN @Shape = 'deep' THEN 0.4995
        ELSE @ProbabilityGivenParent
    END,
    CASE
        WHEN @Shape = 'deep' THEN 0.5
        ELSE @ProbabilityGivenNotParent
    END,
    TIMESTAMPTZ '2026-08-15 00:00:00+00',
    TIMESTAMPTZ '2026-08-15 00:00:00+00'
FROM generated_edges
CROSS JOIN LATERAL (
    SELECT id
    FROM public.graphs
    WHERE slug = @Slug
) AS graph;

WITH local_diamonds AS (
    SELECT
        primary_parents.node_index,
        sibling_groups.first_sibling_index +
            ((primary_parents.primary_parent_index - sibling_groups.first_sibling_index + 1) % 4)
            AS alternate_parent_index
    FROM (
        SELECT
            series.node_index,
            (series.node_index - 1) / 4 AS primary_parent_index
        FROM generate_series(5, @NodeCount - 1) AS series(node_index)
        WHERE @Shape = 'shared-diamond'
    ) AS primary_parents
    CROSS JOIN LATERAL (
        SELECT
            (4 * ((primary_parents.primary_parent_index - 1) / 4)) + 1
                AS first_sibling_index
    ) AS sibling_groups
)
INSERT INTO public.edges (
    id,
    graph_id,
    from_node_id,
    to_node_id,
    kind,
    probability_given_parent,
    probability_given_not_parent,
    created_at,
    updated_at
)
SELECT
    format('e-a-%s', lpad(local_diamonds.node_index::text, 5, '0')),
    graph.id,
    format('n-%s', lpad(local_diamonds.node_index::text, 5, '0')),
    format('n-%s', lpad(local_diamonds.alternate_parent_index::text, 5, '0')),
    'support',
    @ProbabilityGivenParent,
    @ProbabilityGivenNotParent,
    TIMESTAMPTZ '2026-08-15 00:00:00+00',
    TIMESTAMPTZ '2026-08-15 00:00:00+00'
FROM local_diamonds
CROSS JOIN LATERAL (
    SELECT id
    FROM public.graphs
    WHERE slug = @Slug
) AS graph;

SELECT pg_catalog.setval(
    pg_catalog.pg_get_serial_sequence('public.graphs', 'id'),
    (SELECT MAX(id) FROM public.graphs),
    true
);
