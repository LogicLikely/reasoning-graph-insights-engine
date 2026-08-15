#!/usr/bin/env node

import { readFile } from 'node:fs/promises'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'
import { countCharacters, sha256, slugify } from './corpus-lib.mjs'

const here = path.dirname(fileURLToPath(import.meta.url))
const repositoryRoot = path.resolve(here, '../..')
const manifestPath = path.join(here, 'corpus-sources.v1.json')
const selectionPaths = [
  path.join(here, 'drafts/selection-classics.json'),
  path.join(here, 'drafts/selection-arguments.json'),
  path.join(here, 'drafts/selection-modern.json'),
]
const previewPath = path.join(
  repositoryRoot,
  'docs/review/stress-corpus/round-01/corpus-preview.json',
)
const markdownPath = path.join(
  repositoryRoot,
  'docs/review/stress-corpus/round-01/corpus-preview.md',
)
const fixturePath = path.join(
  repositoryRoot,
  'frontend/src/fixtures/review/insightsNodeTitleCorpus.ts',
)
const [previewBytes, manifestBytes, markdownBytes, fixtureBytes, ...selectionBytes] = await Promise.all([
  readFile(previewPath),
  readFile(manifestPath),
  readFile(markdownPath),
  readFile(fixturePath),
  ...selectionPaths.map((selectionPath) => readFile(selectionPath)),
])
const preview = JSON.parse(previewBytes.toString('utf8'))
const manifest = JSON.parse(manifestBytes.toString('utf8'))
const selectionFragments = selectionBytes.map((bytes) => JSON.parse(bytes.toString('utf8')))
const sourceById = new Map(manifest.sources.map((source) => [source.id, source]))
const toPortablePath = (value) => value.split(path.sep).join('/')
const selectionBySource = new Map(
  selectionFragments
    .flatMap((fragment) => fragment.sources)
    .map((source) => [source.sourceId, source]),
)

const invariant = (condition, message) => {
  if (!condition) throw new Error(message)
}
const kindForIndex = (index) => {
  if (index === 0) return 'root'
  if (index % 5 === 0) return 'evidence'
  if (index % 10 === 2) return 'objection'
  return 'claim'
}

invariant(preview.schemaVersion === 1, 'Unexpected schema version.')
invariant(preview.status === 'candidate', 'Preview must remain a candidate artifact.')
invariant(
  preview.sourceTextNormalization?.version === 1 &&
    preview.sourceTextNormalization.clipping === false,
  'Source normalization must explicitly prohibit clipping.',
)
invariant(
  preview.sourceManifest?.path === 'tools/stress-corpus/corpus-sources.v1.json' &&
    preview.sourceManifest.hashMethod === 'SHA-256 of JSON.stringify(parsed JSON)' &&
    preview.sourceManifest.sha256 === sha256(JSON.stringify(manifest)) &&
    preview.sourceManifest.sourceCount === manifest.sources.length,
  'Source manifest metadata does not match.',
)
invariant(
  JSON.stringify(preview.selectionManifest?.paths) ===
    JSON.stringify(
      selectionPaths.map((selectionPath) =>
        toPortablePath(path.relative(repositoryRoot, selectionPath)),
      ),
    ) &&
    preview.selectionManifest.hashMethod ===
      'SHA-256 of JSON.stringify(parsed selection documents in path order)' &&
    preview.selectionManifest.sha256 === sha256(JSON.stringify(selectionFragments)),
  'Selection manifest metadata does not match.',
)
invariant(preview.entries.length === 180, 'Preview must contain exactly 180 entries.')
invariant(
  preview.entries.filter(({ sampleClass }) => sampleClass === 'representative').length === 160,
  'Preview must contain 160 representative entries.',
)
invariant(
  preview.entries.filter(({ sampleClass }) => sampleClass === 'edge-case').length === 20,
  'Preview must contain 20 edge cases.',
)

const ids = new Set()
const titles = new Set()
const excerpts = new Set()
const edgeCaseIds = new Set()
const candidateIds = new Set()
for (const [index, entry] of preview.entries.entries()) {
  invariant(entry.ordinal === index && entry.nodeIndex === index, `Bad ordinal at ${index}.`)
  invariant(entry.id === `corpus-preview-${String(index).padStart(3, '0')}`, `Bad ID at ${index}.`)
  invariant(entry.kind === kindForIndex(index), `Bad kind at ${index}.`)
  invariant(!ids.has(entry.id), `Duplicate ID ${entry.id}.`)
  ids.add(entry.id)

  const titleKey = entry.title.toLocaleLowerCase('en-US')
  invariant(!titles.has(titleKey), `Duplicate title ${entry.title}.`)
  titles.add(titleKey)
  invariant(entry.title === entry.title.trim(), `Untrimmed title at ${index}.`)
  invariant(countCharacters(entry.title) === entry.titleCharacters, `Bad title count at ${index}.`)
  invariant(
    entry.title.trim().split(/\s+/).length === entry.titleWords,
    `Bad title word count metadata at ${index}.`,
  )
  invariant(entry.titleCharacters <= 35, `Title too long at ${index}.`)
  invariant(entry.titleWords >= 3 && entry.titleWords <= 6, `Bad title word count at ${index}.`)

  invariant(entry.sentences.length === entry.sentenceCount, `Bad sentence count at ${index}.`)
  invariant(entry.sentenceCount === 1 || entry.sentenceCount === 2, `Bad sentence range at ${index}.`)
  invariant(entry.excerpt === entry.sentences.join(' '), `Bad excerpt assembly at ${index}.`)
  invariant(!excerpts.has(entry.excerpt), `Duplicate excerpt at ${index}.`)
  excerpts.add(entry.excerpt)
  invariant(
    entry.sentences.every((sentence) => /[.!?][”’"')\]]?$/.test(sentence)),
    `Incomplete sentence at ${index}.`,
  )
  invariant(!/(?:…|\.\.\.)$/.test(entry.excerpt), `Truncated excerpt at ${index}.`)

  const kindLabel = `${entry.kind[0].toUpperCase()}${entry.kind.slice(1)}`
  const expectedBody = `${kindLabel} ${String(index).padStart(5, '0')} — ${entry.excerpt}`
  invariant(entry.bodyText === expectedBody, `Bad body prefix at ${index}.`)
  invariant(countCharacters(entry.bodyText) === entry.bodyCharacters, `Bad body count at ${index}.`)
  invariant(entry.bodyText.length === entry.bodyUtf16Units, `Bad UTF-16 count at ${index}.`)
  invariant(entry.bodyCharacters <= 250, `Body too long at ${index}.`)

  const sortedTags = [...entry.tags].sort()
  invariant(JSON.stringify(sortedTags) === JSON.stringify(entry.tags), `Unsorted tags at ${index}.`)
  invariant(new Set(entry.tags).size === entry.tags.length, `Duplicate tags at ${index}.`)
  invariant(entry.tags.every((tag) => tag === tag.toLowerCase()), `Uppercase tag at ${index}.`)
  for (const required of ['public-domain', 'stress']) {
    invariant(entry.tags.includes(required), `Missing ${required} tag at ${index}.`)
  }
  for (const prefix of ['author:', 'corpus:', 'sample:', 'section:', 'work:']) {
    invariant(entry.tags.some((tag) => tag.startsWith(prefix)), `Missing ${prefix} tag at ${index}.`)
  }

  invariant(entry.provenance.type === 'public-domain-source', `Bad provenance at ${index}.`)
  const source = sourceById.get(entry.provenance.sourceId)
  invariant(source, `Unknown source at ${index}.`)
  invariant(
    source.id === manifest.sources[index % manifest.sources.length].id,
    `Source interleave mismatch at ${index}.`,
  )
  const selectionRound = index < 160 ? Math.floor(index / 10) : Math.floor((index - 160) / 10)
  const expectedSampleClass = index < 160 ? 'representative' : 'edge-case'
  const expectedSelection = selectionBySource.get(source.id)?.[
    expectedSampleClass === 'representative' ? 'representative' : 'edgeCases'
  ]?.[selectionRound]
  invariant(expectedSelection, `Missing source selection at ${index}.`)
  invariant(
    entry.sampleClass === expectedSampleClass &&
      entry.title === expectedSelection.title &&
      JSON.stringify(entry.provenance.candidateIds) ===
        JSON.stringify(expectedSelection.candidateIds),
    `Selection mismatch at ${index}.`,
  )
  invariant(
    entry.review.edgeCaseId ===
      (expectedSampleClass === 'edge-case' ? expectedSelection.caseId : undefined) &&
      entry.review.rationale ===
        (expectedSampleClass === 'edge-case' ? expectedSelection.rationale : undefined) &&
      entry.review.rejoinedTokenizerBoundary ===
        (expectedSelection.combineAsOneSentence ? true : undefined) &&
      JSON.stringify(entry.review.flags) ===
        JSON.stringify(
          expectedSelection.reviewFlags
            ? [...expectedSelection.reviewFlags].sort()
            : undefined,
        ) &&
      entry.review.note === expectedSelection.reviewNote,
    `Review metadata mismatch at ${index}.`,
  )
  invariant(
    entry.provenance.ebookNumber === source.ebookNumber &&
      entry.provenance.work === source.title &&
      entry.provenance.author === source.author &&
      entry.provenance.translator === source.translator &&
      entry.provenance.canonicalUrl === source.canonicalUrl &&
      entry.provenance.sourceSha256 === source.sha256 &&
      entry.provenance.publicDomainStatus === 'Public domain in the USA' &&
      entry.provenance.retrievedOn === manifest.retrievedOn,
    `Source provenance mismatch at ${index}.`,
  )
  invariant(
    entry.provenance.sourceBlockLineStart >= source.contentLines.start &&
      entry.provenance.sourceBlockLineEnd <= source.contentLines.end &&
      entry.provenance.sourceBlockLineStart <= entry.provenance.sourceBlockLineEnd,
    `Source locator out of range at ${index}.`,
  )
  invariant(
    entry.provenance.candidateIds.length >= 1 &&
      entry.provenance.candidateIds.length <= 2 &&
      entry.provenance.candidateIds.every((candidateId) =>
        candidateId.startsWith(`${source.id}:`),
      ),
    `Bad candidate provenance at ${index}.`,
  )
  for (const candidateId of entry.provenance.candidateIds) {
    invariant(!candidateIds.has(candidateId), `Reused source sentence ${candidateId}.`)
    candidateIds.add(candidateId)
  }
  const expectedTags = [
    `author:${slugify(source.author)}`,
    `corpus:${manifest.corpusId}`,
    'public-domain',
    `sample:${entry.sampleClass}`,
    `section:${entry.provenance.section}`,
    'stress',
    `work:${source.id}`,
  ].sort()
  invariant(
    JSON.stringify(entry.tags) === JSON.stringify(expectedTags),
    `Tag provenance mismatch at ${index}.`,
  )
  invariant(
    entry.provenance.excerptSha256 === sha256(entry.excerpt),
    `Bad excerpt hash at ${index}.`,
  )
  if (entry.sampleClass === 'edge-case') {
    invariant(entry.review.edgeCaseId, `Missing edge-case ID at ${index}.`)
    invariant(!edgeCaseIds.has(entry.review.edgeCaseId), `Duplicate edge-case ID at ${index}.`)
    edgeCaseIds.add(entry.review.edgeCaseId)
    invariant(entry.review.rationale, `Missing edge-case rationale at ${index}.`)
  }
  if (entry.review.flags) {
    invariant(
      JSON.stringify([...entry.review.flags].sort()) === JSON.stringify(entry.review.flags),
      `Unsorted review flags at ${index}.`,
    )
    invariant(
      entry.review.flags.every((flag) => /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(flag)),
      `Invalid review flag at ${index}.`,
    )
  }
  invariant(entry.review.status === 'candidate', `Bad review status at ${index}.`)
}

invariant(
  preview.entriesFingerprint === sha256(JSON.stringify(preview.entries)),
  'Entry fingerprint does not match.',
)
invariant(
  Object.values(preview.stats.sources).every((count) => count === 18),
  'Every source must contribute exactly 18 entries.',
)
const countBy = (values) =>
  Object.fromEntries(
    [...new Set(values)]
      .sort()
      .map((value) => [value, values.filter((item) => item === value).length]),
  )
invariant(
  JSON.stringify(preview.stats.sources) ===
    JSON.stringify(countBy(preview.entries.map((entry) => entry.provenance.sourceId))) &&
    JSON.stringify(preview.stats.kinds) ===
      JSON.stringify(countBy(preview.entries.map((entry) => entry.kind))) &&
    JSON.stringify(preview.stats.sampleClasses) ===
      JSON.stringify(countBy(preview.entries.map((entry) => entry.sampleClass))) &&
    JSON.stringify(preview.stats.sentenceCounts) ===
      JSON.stringify(countBy(preview.entries.map((entry) => String(entry.sentenceCount)))),
  'Preview aggregate statistics do not match the entries.',
)
invariant(
  preview.stats.totalEntries === preview.entries.length &&
    preview.stats.bodyCharacters.minimum ===
      Math.min(...preview.entries.map((entry) => entry.bodyCharacters)) &&
    preview.stats.bodyCharacters.maximum ===
      Math.max(...preview.entries.map((entry) => entry.bodyCharacters)) &&
    preview.stats.bodyCharacters.under100 ===
      preview.entries.filter((entry) => entry.bodyCharacters < 100).length &&
    preview.stats.titleCharacters.minimum ===
      Math.min(...preview.entries.map((entry) => entry.titleCharacters)) &&
    preview.stats.titleCharacters.maximum ===
      Math.max(...preview.entries.map((entry) => entry.titleCharacters)),
  'Preview range statistics do not match the entries.',
)

const expectedFixture = [
  "import type { GraphNodeKind } from '../sampleGraph'",
  '',
  'export interface InsightsNodeTitleCorpusEntry {',
  '  id: string',
  '  kind: GraphNodeKind',
  '  title: string',
  '  bodyText: string',
  "  sampleClass: 'representative' | 'edge-case'",
  '  sourceId: string',
  '  titleCharacters: number',
  '}',
  '',
  'export const insightsNodeTitleCorpus: readonly InsightsNodeTitleCorpusEntry[] = [',
  ...preview.entries.flatMap((entry) => [
    '  {',
    `    id: ${JSON.stringify(entry.id)},`,
    `    kind: ${JSON.stringify(entry.kind)},`,
    `    title: ${JSON.stringify(entry.title)},`,
    `    bodyText: ${JSON.stringify(entry.bodyText)},`,
    `    sampleClass: ${JSON.stringify(entry.sampleClass)},`,
    `    sourceId: ${JSON.stringify(entry.provenance.sourceId)},`,
    `    titleCharacters: ${entry.titleCharacters},`,
    '  },',
  ]),
  ']',
  '',
].join('\n')
invariant(fixtureBytes.toString('utf8') === expectedFixture, 'Generated TypeScript fixture is stale.')

const markdown = markdownBytes.toString('utf8')
for (const entry of preview.entries) {
  invariant(
    markdown.includes(
      `### ${String(entry.ordinal).padStart(3, '0')} · ${entry.kind} · ${entry.sampleClass}`,
    ) &&
      markdown.includes(
        `**Title:** ${entry.title} — ${entry.titleCharacters} chars / ${entry.titleWords} words`,
      ) &&
      markdown.includes(`**Body:** ${entry.bodyText}`),
    `Generated Markdown is stale at ${entry.ordinal}.`,
  )
}

process.stdout.write(
  `Validated ${preview.entries.length} entries, ${titles.size} titles, and ${edgeCaseIds.size} edge cases.\n`,
)
