# Public-domain stress corpus

This tooling builds the deterministic 10,000-entry text corpus used by the
optional stress graphs. The canonical corpus is split into ten readable
1,000-entry shards under `tools/stress-corpus/full/`; the runtime-only
projection is `backend/data/seed/insights_stress_corpus.json`.

The corpus contract is deliberately small:

- indexes 0–179 preserve the approved preview titles and excerpts exactly;
- the first 1,000 entries are the prefix used by every 1K graph;
- all 10,000 entries are used in the same order by every 10K graph;
- a 100K graph uses `nodeIndex % 10000`, repeating the corpus exactly ten
  times;
- titles contain 3–6 words, use at most 35 Unicode characters, and are unique
  without regard to case;
- excerpts contain one or two complete source sentences, use at most 232
  Unicode characters, and are unique without regard to case;
- the 232-character excerpt ceiling leaves room for the 18-character worst
  case `Objection 99999 — ` prefix inside a 250-character node body;
- categories and sorted tags are derived consistently from source provenance.

Generated entries use lightly screened public-domain prose. Mechanical filters
drop source artifacts, abbreviation-split fragments, overt slurs, and
particularly graphic wording. The reviewed 180-entry prefix remains unchanged.
Generated titles are short ordered keyword summaries made from the excerpt;
natural contiguous source phrases are preferred when they score better. No
numeric suffix is used to make a title unique.

## Rebuild and verify

Source books are not committed. Fetch the exact thirteen editions into a
temporary directory using the full source manifest:

```sh
node tools/stress-corpus/fetch-sources.mjs /tmp/insights-stress-corpus \
  --manifest tools/stress-corpus/full/corpus-sources.v1.json
```

Every source download fails closed if its SHA-256 differs from the pinned
manifest. Project Gutenberg URLs identify editions but their bytes can change,
so the recorded hashes are authoritative.

Use the Node version in `.nvmrc`; sentence boundaries depend on its ICU data.
Generate and then verify all outputs with:

```sh
node tools/stress-corpus/build-full-corpus.mjs \
  --source-dir /tmp/insights-stress-corpus
node tools/stress-corpus/validate-full-corpus.mjs \
  --source-dir /tmp/insights-stress-corpus
node tools/stress-corpus/build-full-corpus.mjs \
  --source-dir /tmp/insights-stress-corpus --check
```

`--check` regenerates everything in memory and fails if any committed shard,
runtime asset, manifest statistic, hash, or fingerprint is stale. The validator
also checks the exact reviewed prefix, case-insensitive uniqueness, Unicode and
length limits, sorted tags, source locators, the 1K/10K fingerprints, and the
ten-repeat 100K mapping.

Generated outputs are:

- `tools/stress-corpus/full/corpus-00000-00999.json` through
  `corpus-09000-09999.json`: readable canonical shards with source locators;
- `tools/stress-corpus/full/corpus-manifest.v1.json`: compact source, shard,
  filter, statistics, hash, and prefix metadata;
- `backend/data/seed/insights_stress_corpus.json`: the exact compact runtime
  schema consumed by database reset.

Do not hand-edit generated outputs. Change the source manifest or generator and
rebuild instead.

## Original 180-entry review batch

The earlier preview remains reproducible and visually reviewable independently:

```sh
node tools/stress-corpus/fetch-sources.mjs /tmp/insights-preview-corpus
node tools/stress-corpus/build-preview.mjs \
  --source-dir /tmp/insights-preview-corpus --check
node tools/stress-corpus/validate-preview.mjs
```

The full generator freezes a fingerprint of those 180 approved title/excerpt
pairs. Changing the preview therefore fails loudly instead of silently moving
the runtime prefix.
