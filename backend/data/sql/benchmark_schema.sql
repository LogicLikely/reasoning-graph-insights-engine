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
        AND manifest_json->>'scenarioKey' = scenario_key
        AND manifest_json->>'operationKey' = operation_key
        AND manifest_json#>>'{graph,slug}' = graph_slug
        AND manifest_json#>>'{dataset,datasetInputFingerprint}' = dataset_input_fingerprint
        AND manifest_json#>>'{algorithm,semanticIdentity}' = algorithm_semantic_identity
        AND manifest_json#>>'{canonicalParameters,digest}' = parameter_digest
        AND manifest_json->>'environmentProfile' = environment_profile
        AND manifest_json->>'buildMode' = build_mode
        AND manifest_json->'measurementUnits' = measurement_units
    )
);

CREATE TABLE IF NOT EXISTS benchmark.samples (
    entry_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    run_id uuid NOT NULL,
    sample_id uuid NOT NULL,
    scenario_key text NOT NULL CHECK (length(btrim(scenario_key)) > 0),
    operation_key text NOT NULL CHECK (length(btrim(operation_key)) > 0),
    iteration integer NOT NULL CHECK (iteration >= 0),
    layer text NOT NULL CHECK (length(btrim(layer)) > 0),
    phase text NOT NULL CHECK (length(btrim(phase)) > 0),
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
        AND (sample_json->>'wallClockDuration')::numeric = wall_clock_duration
        AND sample_json#>>'{execution,status}' = status
        AND sample_json#>>'{execution,failure,kind}' IS NOT DISTINCT FROM failure_kind
    )
);

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

CREATE INDEX IF NOT EXISTS ix_benchmark_runs_comparison
    ON benchmark.runs (
        scenario_key,
        operation_key,
        dataset_input_fingerprint,
        algorithm_semantic_identity,
        parameter_digest,
        environment_profile,
        build_mode
    );

CREATE INDEX IF NOT EXISTS ix_benchmark_runs_status_started
    ON benchmark.runs (status, started_at DESC);

CREATE INDEX IF NOT EXISTS ix_benchmark_samples_run_entry
    ON benchmark.samples (run_id, entry_id);

CREATE INDEX IF NOT EXISTS ix_benchmark_samples_phase
    ON benchmark.samples (run_id, layer, phase, iteration, entry_id);

CREATE INDEX IF NOT EXISTS ix_benchmark_outputs_run_entry
    ON benchmark.outputs (run_id, entry_id);
