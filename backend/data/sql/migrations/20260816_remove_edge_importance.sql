-- Makes the two conditional probabilities the sole persisted edge weights.
-- For edges that still have the original neutral probability defaults, retain
-- the old likelihood ratio by encoding importance/10 over 0.1.

UPDATE public.edges
SET
    probability_given_parent = importance_to_parent / 10.0,
    probability_given_not_parent = 0.1
WHERE probability_given_parent = 0.5
  AND probability_given_not_parent = 0.5;

-- The old probability constraints admitted zero. Clamp those boundary values
-- to the schema precision floor so every edge has a finite derived ratio.
UPDATE public.edges
SET
    probability_given_parent = GREATEST(probability_given_parent, 0.000000001),
    probability_given_not_parent = GREATEST(probability_given_not_parent, 0.000000001);

ALTER TABLE public.edges
    DROP CONSTRAINT IF EXISTS ck_edges_importance_to_parent,
    DROP CONSTRAINT IF EXISTS ck_edges_probability_given_parent,
    DROP CONSTRAINT IF EXISTS ck_edges_probability_given_not_parent,
    DROP COLUMN IF EXISTS importance_to_parent;

ALTER TABLE public.edges
    ADD CONSTRAINT ck_edges_probability_given_parent
        CHECK (probability_given_parent > 0 AND probability_given_parent <= 1),
    ADD CONSTRAINT ck_edges_probability_given_not_parent
        CHECK (probability_given_not_parent > 0 AND probability_given_not_parent <= 1);
