--
-- Parameterized deterministic stress-graph generator.
-- Expected Dapper parameters: GraphId, Slug, Title, Description, Shape,
-- NodeCount, InitialTargetLogOdds, EffectiveCounterContributionLogOdds,
-- CorpusJson, CorpusEntryCount.
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
            WHEN series.node_index % 5 = 0 THEN 'evidence'
            WHEN series.node_index % 10 = 2 THEN 'objection'
            ELSE 'claim'
        END AS kind,
        corpus.title,
        corpus.excerpt,
        corpus.category,
        corpus.tags,
        vocabulary.evidence_types[1 + ((series.node_index / 5) % 5)] AS evidence_type,
        35 + (5 * ((series.node_index / 5) % 7)) AS evidence_score
    FROM generate_series(0, @NodeCount - 1) AS series(node_index)
    INNER JOIN corpus
        ON corpus.corpus_index = series.node_index % @CorpusEntryCount
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
        WHEN payload.kind IN ('evidence', 'objection') THEN 0
        ELSE payload.prior_odds
    END,
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
    CASE WHEN generated_edges.node_index % 2 = 1 THEN 'support' ELSE 'rebut' END,
    CASE WHEN generated_edges.node_index % 2 = 1 THEN 0.5005 ELSE 0.4995 END,
    0.5,
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
    CASE WHEN local_diamonds.node_index % 2 = 1 THEN 'support' ELSE 'rebut' END,
    CASE WHEN local_diamonds.node_index % 2 = 1 THEN 0.5005 ELSE 0.4995 END,
    0.5,
    TIMESTAMPTZ '2026-08-15 00:00:00+00',
    TIMESTAMPTZ '2026-08-15 00:00:00+00'
FROM local_diamonds
CROSS JOIN LATERAL (
    SELECT id
    FROM public.graphs
    WHERE slug = @Slug
) AS graph;

-- Calibrate the non-deep benchmark workload for the legacy counter-set
-- analytics retained by this branch. Evidence keeps its authored Bayesian
-- posterior. Only the root and objections are updated below.
WITH RECURSIVE active_paths AS (
    SELECT
        active_node.graph_id,
        active_node.id AS active_node_id,
        edge.to_node_id AS ancestor_node_id,
        ln(edge.probability_given_parent::double precision) -
            ln(edge.probability_given_not_parent::double precision)
            AS path_log_likelihood
    FROM public.nodes AS active_node
    INNER JOIN public.edges AS edge
        ON edge.graph_id = active_node.graph_id
        AND edge.from_node_id = active_node.id
    INNER JOIN public.graphs AS graph ON graph.id = active_node.graph_id
    WHERE graph.slug = @Slug
      AND @Shape IN ('balanced', 'wide')
      AND active_node.kind IN ('evidence', 'objection')

    UNION ALL

    SELECT
        active_paths.graph_id,
        active_paths.active_node_id,
        parent_edge.to_node_id,
        active_paths.path_log_likelihood +
            ln(parent_edge.probability_given_parent::double precision) -
            ln(parent_edge.probability_given_not_parent::double precision)
    FROM active_paths
    INNER JOIN public.edges AS parent_edge
        ON parent_edge.from_node_id = active_paths.ancestor_node_id
       AND parent_edge.graph_id = active_paths.graph_id
), generic_path_extremes AS (
    SELECT
        ancestor_node_id,
        active_node_id,
        min(path_log_likelihood) AS minimum_path,
        max(path_log_likelihood) AS maximum_path
    FROM active_paths
    GROUP BY ancestor_node_id, active_node_id
), diamond_frontiers (
    graph_id,
    active_node_id,
    primary_ancestor_index,
    alternate_ancestor_index,
    minimum_path,
    maximum_path
) AS (
    -- A shared-diamond node has two sibling parents. Both outgoing edges from
    -- that node have the same numeric weight, so the min/max path to both
    -- parents is identical. The two siblings, in turn, share the same next
    -- parent pair. One min/max frontier per active node and level therefore
    -- preserves every path extreme without materializing all 2^depth paths.
    SELECT
        graph.id,
        format('n-%s', lpad(series.node_index::text, 5, '0')),
        direct_parent.primary_ancestor_index,
        CASE
            WHEN series.node_index >= 5 THEN
                direct_group.first_sibling_index + (
                    (
                        direct_parent.primary_ancestor_index -
                        direct_group.first_sibling_index +
                        1
                    ) % 4
                )
            ELSE NULL::integer
        END,
        CASE
            WHEN series.node_index % 2 = 1
                THEN ln(0.5005::numeric) - ln(0.5::numeric)
            ELSE ln(0.4995::numeric) - ln(0.5::numeric)
        END,
        CASE
            WHEN series.node_index % 2 = 1
                THEN ln(0.5005::numeric) - ln(0.5::numeric)
            ELSE ln(0.4995::numeric) - ln(0.5::numeric)
        END
    FROM generate_series(1, @NodeCount - 1) AS series(node_index)
    CROSS JOIN LATERAL (
        SELECT (series.node_index - 1) / 4 AS primary_ancestor_index
    ) AS direct_parent
    CROSS JOIN LATERAL (
        SELECT
            (4 * ((direct_parent.primary_ancestor_index - 1) / 4)) + 1
                AS first_sibling_index
    ) AS direct_group
    CROSS JOIN LATERAL (
        SELECT id
        FROM public.graphs
        WHERE slug = @Slug
    ) AS graph
    WHERE @Shape = 'shared-diamond'
      AND (
          series.node_index % 5 = 0
          OR series.node_index % 10 = 2
      )

    UNION ALL

    SELECT
        diamond_frontiers.graph_id,
        diamond_frontiers.active_node_id,
        next_parent.primary_ancestor_index,
        CASE
            WHEN diamond_frontiers.primary_ancestor_index >= 5 THEN
                next_group.first_sibling_index + (
                    (
                        next_parent.primary_ancestor_index -
                        next_group.first_sibling_index +
                        1
                    ) % 4
                )
            ELSE NULL::integer
        END,
        diamond_frontiers.minimum_path + least(
            CASE
                WHEN diamond_frontiers.primary_ancestor_index % 2 = 1
                    THEN ln(0.5005::numeric) - ln(0.5::numeric)
                ELSE ln(0.4995::numeric) - ln(0.5::numeric)
            END,
            CASE
                WHEN diamond_frontiers.alternate_ancestor_index % 2 = 1
                    THEN ln(0.5005::numeric) - ln(0.5::numeric)
                ELSE ln(0.4995::numeric) - ln(0.5::numeric)
            END
        ),
        diamond_frontiers.maximum_path + greatest(
            CASE
                WHEN diamond_frontiers.primary_ancestor_index % 2 = 1
                    THEN ln(0.5005::numeric) - ln(0.5::numeric)
                ELSE ln(0.4995::numeric) - ln(0.5::numeric)
            END,
            CASE
                WHEN diamond_frontiers.alternate_ancestor_index % 2 = 1
                    THEN ln(0.5005::numeric) - ln(0.5::numeric)
                ELSE ln(0.4995::numeric) - ln(0.5::numeric)
            END
        )
    FROM diamond_frontiers
    CROSS JOIN LATERAL (
        SELECT
            (diamond_frontiers.primary_ancestor_index - 1) / 4
                AS primary_ancestor_index
    ) AS next_parent
    CROSS JOIN LATERAL (
        SELECT
            (4 * ((next_parent.primary_ancestor_index - 1) / 4)) + 1
                AS first_sibling_index
    ) AS next_group
    WHERE diamond_frontiers.primary_ancestor_index > 0
), diamond_path_extremes AS (
    SELECT
        format(
            'n-%s',
            lpad(diamond_frontiers.primary_ancestor_index::text, 5, '0')
        ) AS ancestor_node_id,
        diamond_frontiers.active_node_id,
        diamond_frontiers.minimum_path,
        diamond_frontiers.maximum_path
    FROM diamond_frontiers

    UNION ALL

    SELECT
        format(
            'n-%s',
            lpad(diamond_frontiers.alternate_ancestor_index::text, 5, '0')
        ),
        diamond_frontiers.active_node_id,
        diamond_frontiers.minimum_path,
        diamond_frontiers.maximum_path
    FROM diamond_frontiers
    WHERE diamond_frontiers.alternate_ancestor_index IS NOT NULL
), path_extremes AS (
    SELECT
        ancestor_node_id,
        active_node_id,
        minimum_path,
        maximum_path
    FROM generic_path_extremes

    UNION ALL

    SELECT
        ancestor_node_id,
        active_node_id,
        minimum_path,
        maximum_path
    FROM diamond_path_extremes
), calibration_targets AS MATERIALIZED (
    SELECT
        node.graph_id,
        node.id,
        node.kind,
        node.prior_odds,
        node.posterior_odds
    FROM public.nodes AS node
    INNER JOIN public.graphs AS graph ON graph.id = node.graph_id
    WHERE graph.slug = @Slug
      AND @Shape IN ('balanced', 'wide', 'shared-diamond')
      AND (
          node.id = 'n-00000'
          OR node.kind = 'objection'
      )
), strongest_paths AS MATERIALIZED (
    SELECT
        ancestor_node_id,
        active_node_id,
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
    INNER JOIN calibration_targets
        ON calibration_targets.id = strongest_paths.ancestor_node_id
    GROUP BY ancestor_node_id
), counter_paths_to_root AS (
    SELECT
        active_node_id,
        path_log_likelihood
    FROM strongest_paths
    WHERE ancestor_node_id = 'n-00000'
), calibrated_priors AS (
    -- Preserve topology and edge likelihood ratios while solving the two node
    -- priors that define the minimal-counter-set benchmark. Objections are a
    -- deliberate stress-fixture exception to the normal neutral-prior rule.
    SELECT
        calibration_targets.*,
        CASE
            WHEN calibration_targets.id = 'n-00000' THEN
                @InitialTargetLogOdds -
                COALESCE(contributions.total_log_likelihood, 0)
            ELSE
                @EffectiveCounterContributionLogOdds -
                COALESCE(counter_paths_to_root.path_log_likelihood, 0) -
                COALESCE(contributions.total_log_likelihood, 0)
        END AS calibrated_prior_odds
    FROM calibration_targets
    LEFT JOIN contributions
        ON contributions.ancestor_node_id = calibration_targets.id
    LEFT JOIN counter_paths_to_root
        ON counter_paths_to_root.active_node_id = calibration_targets.id
), calibrated_nodes AS (
    SELECT
        calibrated_priors.graph_id,
        calibrated_priors.id,
        calibrated_priors.calibrated_prior_odds,
        CASE
            WHEN calibrated_priors.id = 'n-00000' THEN
                @InitialTargetLogOdds
            ELSE
                calibrated_priors.calibrated_prior_odds +
                (
                    calibrated_priors.posterior_odds -
                    calibrated_priors.prior_odds
                )
        END AS calibrated_posterior_odds
    FROM calibrated_priors
)
UPDATE public.nodes AS node
SET
    prior_odds = calibrated_nodes.calibrated_prior_odds,
    posterior_odds = calibrated_nodes.calibrated_posterior_odds,
    updated_at = TIMESTAMPTZ '2026-08-15 00:00:00+00'
FROM calibrated_nodes
WHERE node.graph_id = calibrated_nodes.graph_id
  AND node.id = calibrated_nodes.id;

SELECT pg_catalog.setval(
    pg_catalog.pg_get_serial_sequence('public.graphs', 'id'),
    (SELECT MAX(id) FROM public.graphs),
    true
);
