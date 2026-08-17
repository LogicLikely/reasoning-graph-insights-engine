-- Evidence and objection nodes use neutral prior log odds. Their existing
-- posterior log odds remain unchanged and therefore become their authored
-- local log Bayes factors under the current leaf base case.

UPDATE public.nodes
SET
    prior_odds = 0,
    updated_at = now()
WHERE kind IN ('evidence', 'objection')
  AND prior_odds <> 0;
