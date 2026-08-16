-- Reset-safe storage for explicit Insights Lab benchmark runs.
--
-- This schema is initialized independently from graph seed/reset SQL. All
-- relationships remain inside the benchmark schema so rebuilding graph data
-- cannot cascade into benchmark history.

CREATE SCHEMA IF NOT EXISTS benchmark;

CREATE TABLE IF NOT EXISTS benchmark.runs (
    run_id uuid PRIMARY KEY,
    name text NOT NULL CHECK (length(btrim(name)) > 0),
    status text NOT NULL CHECK (
        status IN (
            'queued',
            'running',
            'succeeded',
            'failed',
            'timed-out',
            'cancelled',
            'crashed',
            'skipped'
        )
    ),
    failure_kind text CHECK (
        failure_kind IS NULL OR failure_kind IN (
            'validation',
            'execution',
            'timeout',
            'cancellation',
            'crash',
            'skip'
        )
    ),
    started_at timestamp with time zone NOT NULL,
    completed_at timestamp with time zone,
    runner_type text NOT NULL CHECK (
        runner_type IN (
            'command-line',
            'lab-user-interface',
            'benchmark-dot-net',
            'api-browser-journey'
        )
    ),
    profile_key text NOT NULL,
    scenario_key text NOT NULL CHECK (length(btrim(scenario_key)) > 0),
    operation_key text NOT NULL CHECK (length(btrim(operation_key)) > 0),
    graph_slug text NOT NULL CHECK (length(btrim(graph_slug)) > 0),
    dataset_input_fingerprint text NOT NULL CHECK (
        dataset_input_fingerprint ~ '^sha256:[0-9a-f]{64}$'
    ),
    algorithm_semantic_identity text NOT NULL CHECK (
        algorithm_semantic_identity ~ '^[a-z][a-z0-9]*(-[a-z0-9]+)*-v(0|[1-9][0-9]*)$'
    ),
    parameter_digest text NOT NULL CHECK (
        parameter_digest ~ '^sha256:[0-9a-f]{64}$'
    ),
    environment_profile text NOT NULL CHECK (length(btrim(environment_profile)) > 0),
    build_mode text NOT NULL CHECK (length(btrim(build_mode)) > 0),
    actual_strategy text,
    sample_mode text NOT NULL,
    measurement_units jsonb NOT NULL CHECK (jsonb_typeof(measurement_units) = 'object'),
    manifest_json jsonb NOT NULL CHECK (jsonb_typeof(manifest_json) = 'object'),
    inserted_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT ck_benchmark_runs_failure_matches_status CHECK (
        (status IN ('queued', 'running', 'succeeded') AND failure_kind IS NULL)
        OR (status = 'failed' AND failure_kind IN ('validation', 'execution'))
        OR (status = 'timed-out' AND failure_kind = 'timeout')
        OR (status = 'cancelled' AND failure_kind = 'cancellation')
        OR (status = 'crashed' AND failure_kind = 'crash')
        OR (status = 'skipped' AND failure_kind = 'skip')
    ),
    CONSTRAINT ck_benchmark_runs_completion_matches_status CHECK (
        (status IN ('queued', 'running') AND completed_at IS NULL)
        OR (
            status IN ('succeeded', 'failed', 'timed-out', 'cancelled', 'crashed', 'skipped')
            AND completed_at IS NOT NULL
        )
    ),
    CONSTRAINT ck_benchmark_runs_completion_not_before_start CHECK (
        completed_at IS NULL OR completed_at >= started_at
    ),
    CONSTRAINT ck_benchmark_runs_profile_key_nonempty CHECK (
        length(btrim(profile_key)) > 0
    ),
    CONSTRAINT ck_benchmark_runs_actual_strategy_nonempty CHECK (
        actual_strategy IS NULL OR length(btrim(actual_strategy)) > 0
    ),
    CONSTRAINT ck_benchmark_runs_sample_mode CHECK (
        sample_mode IN ('warm', 'cold', 'legacy-unspecified')
    ),
    CONSTRAINT ck_benchmark_runs_manifest_identity CHECK (
        (manifest_json->>'runId')::uuid = run_id
        AND manifest_json->>'name' = name
        AND manifest_json#>>'{execution,status}' = status
        AND manifest_json#>>'{execution,failure,kind}' IS NOT DISTINCT FROM failure_kind
        AND (manifest_json->>'startedAt')::timestamp with time zone = started_at
        AND (
            (manifest_json->'completedAt' = 'null'::jsonb AND completed_at IS NULL)
            OR (manifest_json->>'completedAt')::timestamp with time zone = completed_at
        )
        AND manifest_json->>'runnerType' = runner_type
        AND manifest_json ? 'profileKey'
        AND manifest_json->>'profileKey' = profile_key
        AND manifest_json->>'scenarioKey' = scenario_key
        AND manifest_json->>'operationKey' = operation_key
        AND manifest_json#>>'{graph,slug}' = graph_slug
        AND manifest_json#>>'{dataset,datasetInputFingerprint}' = dataset_input_fingerprint
        AND manifest_json#>>'{algorithm,semanticIdentity}' = algorithm_semantic_identity
        AND manifest_json#>>'{canonicalParameters,digest}' = parameter_digest
        AND manifest_json->>'environmentProfile' = environment_profile
        AND manifest_json->>'buildMode' = build_mode
        AND manifest_json#>>'{strategy,used}' IS NOT DISTINCT FROM actual_strategy
        AND manifest_json#>>'{samplingPolicy,sampleMode}' = sample_mode
        AND manifest_json->'measurementUnits' = measurement_units
    )
);

-- Phase 4 Goal 2 makes the selected runner profile, actual strategy, and
-- run-level warm/cold sample mode explicit comparison selectors. Existing
-- pre-Goal-2 rows remain readable but are conservatively assigned the
-- incompatible legacy-unspecified profile/mode instead of being guessed warm.
DO $phase4_goal2_runs$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'benchmark'
          AND table_name = 'runs'
          AND column_name = 'profile_key'
    ) THEN
        ALTER TABLE benchmark.runs
            DROP CONSTRAINT IF EXISTS ck_benchmark_runs_manifest_identity;

        ALTER TABLE benchmark.runs
            ADD COLUMN profile_key text,
            ADD COLUMN actual_strategy text,
            ADD COLUMN sample_mode text;

        UPDATE benchmark.runs
        SET
            profile_key = 'legacy-unspecified',
            actual_strategy = manifest_json#>>'{strategy,used}',
            sample_mode = 'legacy-unspecified',
            manifest_json = jsonb_set(
                jsonb_set(
                    manifest_json,
                    '{profileKey}',
                    '"legacy-unspecified"'::jsonb,
                    true
                ),
                '{samplingPolicy,sampleMode}',
                '"legacy-unspecified"'::jsonb,
                true
            );

        ALTER TABLE benchmark.runs
            ALTER COLUMN profile_key SET NOT NULL,
            ALTER COLUMN sample_mode SET NOT NULL;

        ALTER TABLE benchmark.runs
            ADD CONSTRAINT ck_benchmark_runs_profile_key_nonempty CHECK (
                length(btrim(profile_key)) > 0
            ),
            ADD CONSTRAINT ck_benchmark_runs_actual_strategy_nonempty CHECK (
                actual_strategy IS NULL OR length(btrim(actual_strategy)) > 0
            ),
            ADD CONSTRAINT ck_benchmark_runs_sample_mode CHECK (
                sample_mode IN ('warm', 'cold', 'legacy-unspecified')
            ),
            ADD CONSTRAINT ck_benchmark_runs_manifest_identity CHECK (
                (manifest_json->>'runId')::uuid = run_id
                AND manifest_json->>'name' = name
                AND manifest_json#>>'{execution,status}' = status
                AND manifest_json#>>'{execution,failure,kind}' IS NOT DISTINCT FROM failure_kind
                AND (manifest_json->>'startedAt')::timestamp with time zone = started_at
                AND (
                    (manifest_json->'completedAt' = 'null'::jsonb AND completed_at IS NULL)
                    OR (manifest_json->>'completedAt')::timestamp with time zone = completed_at
                )
                AND manifest_json->>'runnerType' = runner_type
                AND manifest_json ? 'profileKey'
                AND manifest_json->>'profileKey' = profile_key
                AND manifest_json->>'scenarioKey' = scenario_key
                AND manifest_json->>'operationKey' = operation_key
                AND manifest_json#>>'{graph,slug}' = graph_slug
                AND manifest_json#>>'{dataset,datasetInputFingerprint}' = dataset_input_fingerprint
                AND manifest_json#>>'{algorithm,semanticIdentity}' = algorithm_semantic_identity
                AND manifest_json#>>'{canonicalParameters,digest}' = parameter_digest
                AND manifest_json->>'environmentProfile' = environment_profile
                AND manifest_json->>'buildMode' = build_mode
                AND manifest_json#>>'{strategy,used}' IS NOT DISTINCT FROM actual_strategy
                AND manifest_json#>>'{samplingPolicy,sampleMode}' = sample_mode
                AND manifest_json->'measurementUnits' = measurement_units
            );
    END IF;
END
$phase4_goal2_runs$;

CREATE TABLE IF NOT EXISTS benchmark.samples (
    entry_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    run_id uuid NOT NULL,
    sample_id uuid NOT NULL,
    scenario_key text NOT NULL CHECK (length(btrim(scenario_key)) > 0),
    operation_key text NOT NULL CHECK (length(btrim(operation_key)) > 0),
    iteration integer NOT NULL CHECK (iteration >= 0),
    layer text NOT NULL CHECK (length(btrim(layer)) > 0),
    phase text NOT NULL CHECK (length(btrim(phase)) > 0),
    timing_boundary_provenance text NOT NULL,
    wall_clock_duration numeric NOT NULL CHECK (wall_clock_duration >= 0),
    status text NOT NULL CHECK (
        status IN (
            'queued',
            'running',
            'succeeded',
            'failed',
            'timed-out',
            'cancelled',
            'crashed',
            'skipped'
        )
    ),
    failure_kind text CHECK (
        failure_kind IS NULL OR failure_kind IN (
            'validation',
            'execution',
            'timeout',
            'cancellation',
            'crash',
            'skip'
        )
    ),
    sample_json jsonb NOT NULL CHECK (jsonb_typeof(sample_json) = 'object'),
    inserted_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT fk_benchmark_samples_run
        FOREIGN KEY (run_id) REFERENCES benchmark.runs(run_id) ON DELETE CASCADE,
    CONSTRAINT ck_benchmark_samples_timing_boundary_provenance CHECK (
        timing_boundary_provenance IN (
            'directly-instrumented',
            'externally-observed',
            'estimated'
        )
    ),
    CONSTRAINT ck_benchmark_samples_failure_matches_status CHECK (
        (status IN ('queued', 'running', 'succeeded') AND failure_kind IS NULL)
        OR (status = 'failed' AND failure_kind IN ('validation', 'execution'))
        OR (status = 'timed-out' AND failure_kind = 'timeout')
        OR (status = 'cancelled' AND failure_kind = 'cancellation')
        OR (status = 'crashed' AND failure_kind = 'crash')
        OR (status = 'skipped' AND failure_kind = 'skip')
    ),
    CONSTRAINT ck_benchmark_samples_payload_identity CHECK (
        (sample_json->>'runId')::uuid = run_id
        AND (sample_json->>'sampleId')::uuid = sample_id
        AND sample_json->>'scenarioKey' = scenario_key
        AND sample_json->>'operationKey' = operation_key
        AND (sample_json->>'iteration')::integer = iteration
        AND sample_json->>'layer' = layer
        AND sample_json->>'phase' = phase
        AND sample_json ? 'timingBoundaryProvenance'
        AND sample_json->>'timingBoundaryProvenance' = timing_boundary_provenance
        AND sample_json ? 'operationCounters'
        AND (sample_json->>'wallClockDuration')::numeric = wall_clock_duration
        AND sample_json#>>'{execution,status}' = status
        AND sample_json#>>'{execution,failure,kind}' IS NOT DISTINCT FROM failure_kind
    )
);

-- Phase 4 adds explicit timing-boundary provenance and operation counters to
-- every raw sample. Stores created before the benchmark runner existed have no
-- durable provenance evidence, so their boundary is conservatively reconciled
-- as estimated while preserving every row and unrelated JSON member.
DO $phase4_samples$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'benchmark'
          AND table_name = 'samples'
          AND column_name = 'timing_boundary_provenance'
    ) THEN
        ALTER TABLE benchmark.samples
            ADD COLUMN timing_boundary_provenance text;

        UPDATE benchmark.samples
        SET
            timing_boundary_provenance = 'estimated',
            sample_json = jsonb_set(
                jsonb_set(
                    sample_json,
                    '{timingBoundaryProvenance}',
                    '"estimated"'::jsonb,
                    true
                ),
                '{operationCounters}',
                'null'::jsonb,
                true
            );

        ALTER TABLE benchmark.samples
            ALTER COLUMN timing_boundary_provenance SET NOT NULL;

        ALTER TABLE benchmark.samples
            ADD CONSTRAINT ck_benchmark_samples_timing_boundary_provenance CHECK (
                timing_boundary_provenance IN (
                    'directly-instrumented',
                    'externally-observed',
                    'estimated'
                )
            );

        ALTER TABLE benchmark.samples
            DROP CONSTRAINT IF EXISTS ck_benchmark_samples_payload_identity;

        ALTER TABLE benchmark.samples
            ADD CONSTRAINT ck_benchmark_samples_payload_identity CHECK (
                (sample_json->>'runId')::uuid = run_id
                AND (sample_json->>'sampleId')::uuid = sample_id
                AND sample_json->>'scenarioKey' = scenario_key
                AND sample_json->>'operationKey' = operation_key
                AND (sample_json->>'iteration')::integer = iteration
                AND sample_json->>'layer' = layer
                AND sample_json->>'phase' = phase
                AND sample_json ? 'timingBoundaryProvenance'
                AND sample_json->>'timingBoundaryProvenance' = timing_boundary_provenance
                AND sample_json ? 'operationCounters'
                AND (sample_json->>'wallClockDuration')::numeric = wall_clock_duration
                AND sample_json#>>'{execution,status}' = status
                AND sample_json#>>'{execution,failure,kind}' IS NOT DISTINCT FROM failure_kind
            );
    END IF;
END
$phase4_samples$;

CREATE TABLE IF NOT EXISTS benchmark.outputs (
    entry_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    run_id uuid NOT NULL,
    sample_id uuid NOT NULL,
    scenario_key text NOT NULL CHECK (length(btrim(scenario_key)) > 0),
    operation_key text NOT NULL CHECK (length(btrim(operation_key)) > 0),
    algorithm_semantic_identity text NOT NULL CHECK (
        algorithm_semantic_identity ~ '^[a-z][a-z0-9]*(-[a-z0-9]+)*-v(0|[1-9][0-9]*)$'
    ),
    status text NOT NULL CHECK (
        status IN (
            'queued',
            'running',
            'succeeded',
            'failed',
            'timed-out',
            'cancelled',
            'crashed',
            'skipped'
        )
    ),
    failure_kind text CHECK (
        failure_kind IS NULL OR failure_kind IN (
            'validation',
            'execution',
            'timeout',
            'cancellation',
            'crash',
            'skip'
        )
    ),
    total_result_cardinality bigint NOT NULL CHECK (total_result_cardinality >= 0),
    result_digest text NOT NULL CHECK (result_digest ~ '^sha256:[0-9a-f]{64}$'),
    output_json jsonb NOT NULL CHECK (jsonb_typeof(output_json) = 'object'),
    inserted_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT fk_benchmark_outputs_run
        FOREIGN KEY (run_id) REFERENCES benchmark.runs(run_id) ON DELETE CASCADE,
    CONSTRAINT ck_benchmark_outputs_failure_matches_status CHECK (
        (status IN ('queued', 'running', 'succeeded') AND failure_kind IS NULL)
        OR (status = 'failed' AND failure_kind IN ('validation', 'execution'))
        OR (status = 'timed-out' AND failure_kind = 'timeout')
        OR (status = 'cancelled' AND failure_kind = 'cancellation')
        OR (status = 'crashed' AND failure_kind = 'crash')
        OR (status = 'skipped' AND failure_kind = 'skip')
    ),
    CONSTRAINT ck_benchmark_outputs_payload_identity CHECK (
        (output_json->>'runId')::uuid = run_id
        AND (output_json->>'sampleId')::uuid = sample_id
        AND output_json->>'scenarioKey' = scenario_key
        AND output_json->>'operationKey' = operation_key
        AND output_json->>'algorithmSemanticIdentity' = algorithm_semantic_identity
        AND output_json#>>'{execution,status}' = status
        AND output_json#>>'{execution,failure,kind}' IS NOT DISTINCT FROM failure_kind
        AND (output_json->>'totalResultCardinality')::bigint = total_result_cardinality
        AND output_json->>'resultDigest' = result_digest
        AND jsonb_typeof(output_json->'items') = 'array'
        AND jsonb_array_length(output_json->'items') <= 100
        AND jsonb_array_length(output_json->'items') <= total_result_cardinality
    )
);

-- Phase 3.5 revises the pre-baseline v1 contract in place. Reconcile stores
-- initialized by the earlier v1 DDL without deleting runs, samples, outputs,
-- or any unrelated JSON data. Each table is reconciled only while its retired
-- column is present, so current stores do not scan payloads or replace constraints.
DO $phase35_samples$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'benchmark'
          AND table_name = 'samples'
          AND column_name = 'visualization_admission'
    ) THEN
        ALTER TABLE benchmark.samples
            DROP CONSTRAINT IF EXISTS ck_benchmark_samples_payload_identity;

        UPDATE benchmark.samples
        SET sample_json = sample_json - 'visualizationAdmission' - 'warnings'
        WHERE sample_json ? 'visualizationAdmission' OR sample_json ? 'warnings';

        ALTER TABLE benchmark.samples
            DROP COLUMN IF EXISTS visualization_admission;

        ALTER TABLE benchmark.samples
            ADD CONSTRAINT ck_benchmark_samples_payload_identity CHECK (
                (sample_json->>'runId')::uuid = run_id
                AND (sample_json->>'sampleId')::uuid = sample_id
                AND sample_json->>'scenarioKey' = scenario_key
                AND sample_json->>'operationKey' = operation_key
                AND (sample_json->>'iteration')::integer = iteration
                AND sample_json->>'layer' = layer
                AND sample_json->>'phase' = phase
                AND sample_json ? 'timingBoundaryProvenance'
                AND sample_json->>'timingBoundaryProvenance' = timing_boundary_provenance
                AND sample_json ? 'operationCounters'
                AND (sample_json->>'wallClockDuration')::numeric = wall_clock_duration
                AND sample_json#>>'{execution,status}' = status
                AND sample_json#>>'{execution,failure,kind}' IS NOT DISTINCT FROM failure_kind
            );
    END IF;
END
$phase35_samples$;

DO $phase35_outputs$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'benchmark'
          AND table_name = 'outputs'
          AND column_name = 'visualization_admission'
    ) THEN
        ALTER TABLE benchmark.outputs
            DROP CONSTRAINT IF EXISTS ck_benchmark_outputs_payload_identity;

        UPDATE benchmark.outputs
        SET output_json = output_json - 'visualizationAdmission' - 'warnings'
        WHERE output_json ? 'visualizationAdmission' OR output_json ? 'warnings';

        ALTER TABLE benchmark.outputs
            DROP COLUMN IF EXISTS visualization_admission;

        ALTER TABLE benchmark.outputs
            ADD CONSTRAINT ck_benchmark_outputs_payload_identity CHECK (
                (output_json->>'runId')::uuid = run_id
                AND (output_json->>'sampleId')::uuid = sample_id
                AND output_json->>'scenarioKey' = scenario_key
                AND output_json->>'operationKey' = operation_key
                AND output_json->>'algorithmSemanticIdentity' = algorithm_semantic_identity
                AND output_json#>>'{execution,status}' = status
                AND output_json#>>'{execution,failure,kind}' IS NOT DISTINCT FROM failure_kind
                AND (output_json->>'totalResultCardinality')::bigint = total_result_cardinality
                AND output_json->>'resultDigest' = result_digest
                AND jsonb_typeof(output_json->'items') = 'array'
                AND jsonb_array_length(output_json->'items') <= 100
                AND jsonb_array_length(output_json->'items') <= total_result_cardinality
            );
    END IF;
END
$phase35_outputs$;

-- CREATE INDEX IF NOT EXISTS cannot extend an older index that already has
-- this name. Compare its ordered key columns and replace it only when needed,
-- leaving the steady-state index (and its OID) untouched on repeated startup.
DO $phase4_goal2_comparison_index$
DECLARE
    expected_columns text[] := ARRAY[
        'scenario_key',
        'profile_key',
        'operation_key',
        'dataset_input_fingerprint',
        'algorithm_semantic_identity',
        'parameter_digest',
        'actual_strategy',
        'environment_profile',
        'build_mode',
        'sample_mode',
        'measurement_units'
    ];
    current_columns text[];
BEGIN
    SELECT array_agg(attribute.attname::text ORDER BY index_key.ordinality)
    INTO current_columns
    FROM pg_catalog.pg_index AS index_metadata
    CROSS JOIN LATERAL unnest(index_metadata.indkey)
        WITH ORDINALITY AS index_key(attnum, ordinality)
    JOIN pg_catalog.pg_attribute AS attribute
      ON attribute.attrelid = index_metadata.indrelid
     AND attribute.attnum = index_key.attnum
    WHERE index_metadata.indexrelid =
        to_regclass('benchmark.ix_benchmark_runs_comparison');

    IF current_columns IS DISTINCT FROM expected_columns THEN
        DROP INDEX IF EXISTS benchmark.ix_benchmark_runs_comparison;
        CREATE INDEX ix_benchmark_runs_comparison
            ON benchmark.runs (
                scenario_key,
                profile_key,
                operation_key,
                dataset_input_fingerprint,
                algorithm_semantic_identity,
                parameter_digest,
                actual_strategy,
                environment_profile,
                build_mode,
                sample_mode,
                measurement_units
            );
    END IF;
END
$phase4_goal2_comparison_index$;

CREATE INDEX IF NOT EXISTS ix_benchmark_runs_status_started
    ON benchmark.runs (status, started_at DESC);

CREATE INDEX IF NOT EXISTS ix_benchmark_samples_run_entry
    ON benchmark.samples (run_id, entry_id);

CREATE INDEX IF NOT EXISTS ix_benchmark_samples_phase
    ON benchmark.samples (run_id, layer, phase, iteration, entry_id);

CREATE INDEX IF NOT EXISTS ix_benchmark_outputs_run_entry
    ON benchmark.outputs (run_id, entry_id);
