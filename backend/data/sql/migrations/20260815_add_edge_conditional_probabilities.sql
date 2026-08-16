-- Adds the conditional probabilities required by the Bayes-factor recurrence.
-- This migration is idempotent and preserves existing edges with neutral values.

ALTER TABLE public.edges
    ADD COLUMN IF NOT EXISTS probability_given_parent numeric(10,9)
        DEFAULT 0.5 NOT NULL,
    ADD COLUMN IF NOT EXISTS probability_given_not_parent numeric(10,9)
        DEFAULT 0.5 NOT NULL;

UPDATE public.edges
SET
    probability_given_parent = COALESCE(probability_given_parent, 0.5),
    probability_given_not_parent = COALESCE(probability_given_not_parent, 0.5)
WHERE probability_given_parent IS NULL
   OR probability_given_not_parent IS NULL;

ALTER TABLE public.edges
    ALTER COLUMN probability_given_parent SET DEFAULT 0.5,
    ALTER COLUMN probability_given_parent SET NOT NULL,
    ALTER COLUMN probability_given_not_parent SET DEFAULT 0.5,
    ALTER COLUMN probability_given_not_parent SET NOT NULL;

DO $migration$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_edges_probability_given_parent'
          AND conrelid = 'public.edges'::regclass
    ) THEN
        ALTER TABLE public.edges
            ADD CONSTRAINT ck_edges_probability_given_parent
            CHECK (probability_given_parent BETWEEN 0 AND 1);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_edges_probability_given_not_parent'
          AND conrelid = 'public.edges'::regclass
    ) THEN
        ALTER TABLE public.edges
            ADD CONSTRAINT ck_edges_probability_given_not_parent
            CHECK (probability_given_not_parent BETWEEN 0 AND 1);
    END IF;
END
$migration$;
