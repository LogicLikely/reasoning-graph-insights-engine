# Public-domain stress corpus preview

This directory builds the review-only, 180-entry corpus in
`docs/review/stress-corpus/round-01`. It does not alter the database reset path,
the stress SQL generator, or any installed graph.

The ten selected Project Gutenberg editions are recorded in
`corpus-sources.v1.json`, including the exact bytes reviewed on 2026-08-15.
Project Gutenberg marks each edition public domain in the USA. Its wrapper and
trademark language are excluded from every excerpt; users outside the United
States should check local law.

The builder performs display normalization before selection: Unicode NFC,
collapsed whitespace, removal of Project Gutenberg emphasis/page/numeric
footnote markers and Aristotle Bekker page markers, double hyphens changed to
em dashes, and paragraph-leading all-capital words changed to sentence case.
It never clips excerpts. Every generated entry retains the enclosing source
block's line range and the pinned source hash so reviewers can compare the
normalized text.

Use the Node version pinned by the repository's `.nvmrc` (24.13.0; the reviewed
build uses ICU 77.1) so `Intl.Segmenter` produces the same candidate boundaries.

To fetch the pinned sources and verify the committed review artifacts without
overwriting them:

```sh
node tools/stress-corpus/fetch-sources.mjs /tmp/insights-stress-corpus
node tools/stress-corpus/build-preview.mjs --source-dir /tmp/insights-stress-corpus --check
node tools/stress-corpus/validate-preview.mjs
cd frontend
npm run test:unit -- src/fixtures/review/insightsNodeTitleCorpus.test.ts
npm run build-storybook
```

After intentionally editing a selection, regenerate the JSON, Markdown, and
Storybook fixture with:

```sh
node tools/stress-corpus/build-preview.mjs --source-dir /tmp/insights-stress-corpus
```

That command writes:

- `docs/review/stress-corpus/round-01/corpus-preview.json`
- `docs/review/stress-corpus/round-01/corpus-preview.md`
- `frontend/src/fixtures/review/insightsNodeTitleCorpus.ts`

The portable review inventory is
`docs/review/stress-corpus/round-01/artifact-catalog.json`.

The visual review surface is the Storybook story
`Review/Stress Corpus Titles`, especially its narrow `ParentWidth` variant.
Visual acceptance remains manual: the static build verifies that the story
compiles, while this repository's Storybook Vitest integration is currently
disabled and does not execute the story's `play` assertions.

Project Gutenberg download URLs are retrieval locations, not immutable
archives. The recorded hashes are authoritative: fetching fails closed if the
upstream bytes change. The source books are not vendored here, so long-term
recovery of an older round requires the exact matching bytes from a review
archive or another trustworthy copy of the pinned edition.

Selection files under `drafts/` are intentionally review candidates. Accepting
their wording does not authorize installing the corpus into runtime seeds; that
placement decision remains separate.
