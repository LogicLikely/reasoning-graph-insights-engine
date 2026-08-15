--
-- Parameterized deterministic stress-graph generator.
-- Expected Dapper parameters: GraphId, Slug, Title, Description, Shape, NodeCount.
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

WITH vocabulary AS (
    SELECT
        ARRAY[
            'advocate', 'analyst', 'critic', 'historian',
            'investigator', 'observer', 'scholar', 'witness'
        ]::text[] AS subjects,
        ARRAY[
            'affirms', 'challenges', 'clarifies', 'compares',
            'concedes', 'questions', 'rebuts', 'supports'
        ]::text[] AS verbs,
        ARRAY[
            'assumption', 'conclusion', 'evidence', 'inference',
            'premise', 'principle', 'testimony', 'warrant'
        ]::text[] AS objects,
        ARRAY[
            'by analogy', 'from observation', 'in context', 'through definition',
            'under scrutiny', 'with a counterexample', 'with corroboration', 'with qualification'
        ]::text[] AS qualifiers,
        ARRAY[
            'alpha', 'beta', 'gamma', 'delta',
            'epsilon', 'zeta', 'eta', 'theta',
            'iota', 'kappa', 'lambda', 'mu',
            'nu', 'xi', 'omicron', 'pi'
        ]::text[] AS markers,
        ARRAY[
            'experimental', 'media-analysis', 'observational', 'textual', 'video'
        ]::text[] AS evidence_types
), generated AS (
    SELECT
        series.node_index,
        CASE
            WHEN series.node_index = 0 THEN 'root'
            WHEN series.node_index % 5 = 0 THEN 'evidence'
            WHEN series.node_index % 10 = 2 THEN 'objection'
            ELSE 'claim'
        END AS kind,
        vocabulary.subjects[1 + ((series.node_index * 3) % 8)] AS subject,
        vocabulary.verbs[1 + ((series.node_index * 5) % 8)] AS verb,
        vocabulary.objects[1 + ((series.node_index * 7) % 8)] AS object,
        vocabulary.qualifiers[1 + ((series.node_index * 11) % 8)] AS qualifier,
        vocabulary.markers[1 + ((series.node_index * 13) % 16)] AS marker,
        vocabulary.evidence_types[1 + ((series.node_index / 5) % 5)] AS evidence_type,
        35 + (5 * ((series.node_index / 5) % 7)) AS evidence_score
    FROM generate_series(0, @NodeCount - 1) AS series(node_index)
    CROSS JOIN vocabulary
), payload AS (
    SELECT
        generated.*,
        format('n-%s', lpad(generated.node_index::text, 5, '0')) AS node_id,
        CASE
            WHEN generated.kind = 'evidence'
                THEN ln(
                    generated.evidence_score::double precision /
                    (100 - generated.evidence_score)::double precision
                )
            ELSE 0::double precision
        END AS prior_odds
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
    format(
        '%s %s: %s %s %s',
        initcap(payload.kind),
        lpad(payload.node_index::text, 5, '0'),
        payload.subject,
        payload.verb,
        payload.object
    ),
    format(
        'The %s %s the %s %s. Search marker %s identifies this deterministic rhetoric stress record.',
        payload.subject,
        payload.verb,
        payload.object,
        payload.qualifier,
        payload.marker
    ),
    payload.object,
    ARRAY['synthetic', 'stress-v1', 'rhetoric', payload.marker]::text[],
    payload.prior_odds,
    payload.prior_odds,
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
    importance_to_parent,
    created_at,
    updated_at
)
SELECT
    format('e-p-%s', lpad(generated_edges.node_index::text, 5, '0')),
    graph.id,
    format('n-%s', lpad(generated_edges.node_index::text, 5, '0')),
    format('n-%s', lpad(generated_edges.parent_index::text, 5, '0')),
    CASE WHEN generated_edges.node_index % 2 = 1 THEN 'support' ELSE 'rebut' END,
    CASE WHEN generated_edges.node_index % 2 = 1 THEN 1.001 ELSE 0.999 END,
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
    importance_to_parent,
    created_at,
    updated_at
)
SELECT
    format('e-a-%s', lpad(local_diamonds.node_index::text, 5, '0')),
    graph.id,
    format('n-%s', lpad(local_diamonds.node_index::text, 5, '0')),
    format('n-%s', lpad(local_diamonds.alternate_parent_index::text, 5, '0')),
    CASE WHEN local_diamonds.node_index % 2 = 1 THEN 'support' ELSE 'rebut' END,
    CASE WHEN local_diamonds.node_index % 2 = 1 THEN 1.001 ELSE 0.999 END,
    TIMESTAMPTZ '2026-08-15 00:00:00+00',
    TIMESTAMPTZ '2026-08-15 00:00:00+00'
FROM local_diamonds
CROSS JOIN LATERAL (
    SELECT id
    FROM public.graphs
    WHERE slug = @Slug
) AS graph;

-- Match GraphLikelihoodCalculator: for every evidence/objection descendant,
-- select the path whose accumulated log likelihood is farthest from neutral,
-- sum those contributions with the node prior, and clamp to the engine range.
-- The deep-chain case has a single path between any two connected nodes. Its
-- suffix windows compute the same path sums in O(N), avoiding an O(N^2)
-- evidence-to-ancestor recursive expansion at 10,000 nodes.
WITH path_positions AS (
    SELECT
        series.node_index,
        sum(
            CASE
                WHEN series.node_index = 0 THEN 0::double precision
                WHEN series.node_index % 2 = 1 THEN ln(1.001::double precision)
                ELSE ln(0.999::double precision)
            END
        ) OVER (ORDER BY series.node_index) AS path_to_root
    FROM generate_series(0, @NodeCount - 1) AS series(node_index)
    WHERE @Shape = 'deep'
), suffix_contributions AS (
    SELECT
        path_positions.node_index,
        COALESCE(
            sum(path_positions.path_to_root) FILTER (
                WHERE path_positions.node_index > 0
                  AND (
                      path_positions.node_index % 5 = 0
                      OR path_positions.node_index % 10 = 2
                  )
            ) OVER (
                ORDER BY path_positions.node_index
                ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING
            ),
            0::double precision
        ) - (
            COALESCE(
                count(*) FILTER (
                    WHERE path_positions.node_index > 0
                      AND (
                          path_positions.node_index % 5 = 0
                          OR path_positions.node_index % 10 = 2
                      )
                ) OVER (
                    ORDER BY path_positions.node_index
                    ROWS BETWEEN 1 FOLLOWING AND UNBOUNDED FOLLOWING
                ),
                0
            ) * path_positions.path_to_root
        ) AS total_log_likelihood
    FROM path_positions
)
UPDATE public.nodes AS node
SET
    posterior_odds = greatest(
        -100::double precision,
        least(
            100::double precision,
            node.prior_odds + suffix_contributions.total_log_likelihood
        )
    ),
    updated_at = TIMESTAMPTZ '2026-08-15 00:00:00+00'
FROM public.graphs AS graph
INNER JOIN suffix_contributions ON true
WHERE graph.slug = @Slug
  AND node.graph_id = graph.id
  AND node.id = format(
      'n-%s',
      lpad(suffix_contributions.node_index::text, 5, '0')
  );

WITH RECURSIVE active_paths AS (
    SELECT
        active_node.graph_id,
        active_node.id AS active_node_id,
        edge.to_node_id AS ancestor_node_id,
        ln(edge.importance_to_parent) AS path_log_likelihood
    FROM public.nodes AS active_node
    INNER JOIN public.edges AS edge
        ON edge.graph_id = active_node.graph_id
        AND edge.from_node_id = active_node.id
    INNER JOIN public.graphs AS graph ON graph.id = active_node.graph_id
    WHERE graph.slug = @Slug
      AND @Shape <> 'deep'
      AND active_node.kind IN ('evidence', 'objection')

    UNION ALL

    SELECT
        active_paths.graph_id,
        active_paths.active_node_id,
        parent_edge.to_node_id,
        active_paths.path_log_likelihood + ln(parent_edge.importance_to_parent)
    FROM active_paths
    INNER JOIN public.edges AS parent_edge
        ON parent_edge.from_node_id = active_paths.ancestor_node_id
       AND parent_edge.graph_id = active_paths.graph_id
), path_extremes AS (
    SELECT
        ancestor_node_id,
        active_node_id,
        min(path_log_likelihood) AS minimum_path,
        max(path_log_likelihood) AS maximum_path
    FROM active_paths
    GROUP BY ancestor_node_id, active_node_id
), strongest_paths AS (
    SELECT
        ancestor_node_id,
        CASE
            WHEN abs(minimum_path) > abs(maximum_path) THEN minimum_path
            ELSE maximum_path
        END AS path_log_likelihood
    FROM path_extremes
), contributions AS (
    SELECT
        ancestor_node_id,
        sum(path_log_likelihood) AS total_log_likelihood
    FROM strongest_paths
    GROUP BY ancestor_node_id
)
UPDATE public.nodes AS node
SET
    posterior_odds = greatest(
        -100::double precision,
        least(
            100::double precision,
            node.prior_odds + COALESCE(contributions.total_log_likelihood, 0)
        )
    ),
    updated_at = TIMESTAMPTZ '2026-08-15 00:00:00+00'
FROM public.graphs AS graph
LEFT JOIN contributions ON true
WHERE graph.slug = @Slug
  AND node.graph_id = graph.id
  AND contributions.ancestor_node_id = node.id;

SELECT pg_catalog.setval(
    pg_catalog.pg_get_serial_sequence('public.graphs', 'id'),
    (SELECT MAX(id) FROM public.graphs),
    true
);
