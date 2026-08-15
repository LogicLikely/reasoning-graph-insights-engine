#!/usr/bin/env node

import { readFile } from 'node:fs/promises'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'
import {
  countCharacters,
  loadSourceCandidates,
  sha256,
} from './corpus-lib.mjs'
import {
  argumentValue,
  assertKnownArguments,
  loadSensitiveContentPolicy,
  sensitiveContentRejectionReason,
} from './content-policy.mjs'

const here = path.dirname(fileURLToPath(import.meta.url))
const repositoryRoot = path.resolve(here, '../..')
const fullDirectory = path.join(here, 'full')
const sourceManifestPath = path.join(fullDirectory, 'corpus-sources.v1.json')
const corpusManifestPath = path.join(fullDirectory, 'corpus-manifest.v1.json')
const previewPath = path.join(
  repositoryRoot,
  'docs/review/stress-corpus/round-01/corpus-preview.json',
)
const runtimePath = path.join(
  repositoryRoot,
  'backend/data/seed/insights_stress_corpus.json',
)
const args = process.argv.slice(2)
assertKnownArguments(args, {
  valueFlags: ['--source-dir', '--content-policy'],
})
const sourceDirectory = argumentValue(args, '--source-dir')
const contentPolicyPath = argumentValue(args, '--content-policy')
const sensitiveContentPolicy = contentPolicyPath
  ? await loadSensitiveContentPolicy(contentPolicyPath, { requireComplete: true })
  : null

const invariant = (condition, message) => {
  if (!condition) throw new Error(message)
}
const ordinalKey = (value) => value.normalize('NFC').toUpperCase()
const comparableExcerptKey = (value) =>
  ordinalKey(value)
    .normalize('NFKD')
    .replace(/[\p{P}\p{S}]/gu, ' ')
    .replace(/\s+/g, ' ')
    .trim()
const wordCount = (value) => value.trim().split(/\s+/).length
const jsonEqual = (left, right) => JSON.stringify(left) === JSON.stringify(right)
const countBy = (values) =>
  Object.fromEntries(
    [...new Set(values)]
      .sort()
      .map((value) => [value, values.filter((candidate) => candidate === value).length]),
  )

const [sourceManifestBytes, corpusManifestBytes, previewBytes, runtimeBytes] =
  await Promise.all([
    readFile(sourceManifestPath),
    readFile(corpusManifestPath),
    readFile(previewPath),
    readFile(runtimePath),
  ])
const sourceManifest = JSON.parse(sourceManifestBytes.toString('utf8'))
const corpusManifest = JSON.parse(corpusManifestBytes.toString('utf8'))
const preview = JSON.parse(previewBytes.toString('utf8'))
const runtime = JSON.parse(runtimeBytes.toString('utf8'))
const sourceById = new Map(sourceManifest.sources.map((source) => [source.id, source]))

invariant(corpusManifest.schemaVersion === 1, 'Corpus manifest schemaVersion must be 1.')
invariant(runtime.schemaVersion === 1, 'Runtime schemaVersion must be 1.')
invariant(
  corpusManifest.corpusId === sourceManifest.corpusId &&
    runtime.corpusId === sourceManifest.corpusId &&
    preview.corpusId === sourceManifest.corpusId,
  'Corpus IDs do not match.',
)
invariant(runtime.entryCount === 10_000, 'Runtime entryCount must be 10,000.')
invariant(runtime.entries.length === 10_000, 'Runtime must contain 10,000 entries.')
invariant(
  jsonEqual(Object.keys(runtime), ['schemaVersion', 'corpusId', 'entryCount', 'entries']),
  'Runtime root has an unexpected shape.',
)
invariant(
  corpusManifest.sources.manifestSha256 === sha256(sourceManifestBytes),
  'Source-manifest hash is stale.',
)
invariant(
  corpusManifest.runtime.path === 'backend/data/seed/insights_stress_corpus.json' &&
    corpusManifest.runtime.bytes === runtimeBytes.length &&
    corpusManifest.runtime.sha256 === sha256(runtimeBytes),
  'Runtime bytes do not match the corpus manifest.',
)

const shardEntries = []
for (const [shardIndex, shardRecord] of corpusManifest.shards.entries()) {
  const shardPath = path.join(repositoryRoot, shardRecord.path)
  const shardBytes = await readFile(shardPath)
  invariant(shardRecord.bytes === shardBytes.length, `${shardRecord.path} byte count is stale.`)
  invariant(
    shardRecord.sha256 === sha256(shardBytes),
    `${shardRecord.path} hash is stale.`,
  )
  const shard = JSON.parse(shardBytes.toString('utf8'))
  const indexStart = shardIndex * 1_000
  const indexEnd = indexStart + 999
  invariant(
    shard.schemaVersion === 1 &&
      shard.corpusId === runtime.corpusId &&
      shard.shardIndex === shardIndex &&
      shard.indexStart === indexStart &&
      shard.indexEnd === indexEnd &&
      shard.entryCount === 1_000 &&
      shard.entries.length === 1_000,
    `Bad shard metadata at ${shardRecord.path}.`,
  )
  invariant(
    shardRecord.indexStart === indexStart &&
      shardRecord.indexEnd === indexEnd &&
      shardRecord.entryCount === 1_000,
    `Bad manifest shard metadata at ${shardRecord.path}.`,
  )
  shardEntries.push(...shard.entries)
}
invariant(corpusManifest.shards.length === 10, 'Corpus must contain ten shards.')
invariant(shardEntries.length === 10_000, 'Shards must contain 10,000 entries.')

const titleKeys = new Set()
const excerptKeys = new Set()
const comparableExcerptKeys = new Set()
const runtimeEntryKeys = ['index', 'title', 'excerpt', 'category', 'tags']
const canonicalEntryKeys = [
  ...runtimeEntryKeys,
  'sourceId',
  'section',
  'candidateIds',
  'sourceBlockLineStart',
  'sourceBlockLineEnd',
  'sampleClass',
  'titleMethod',
]
const outlineHeadingPatterns = [
  /^Natural Selection:\s+its power\b/iu,
  /^Extinction caused by\b/iu,
  /^Part\s+[IVXLC]+[.—-]/u,
  /^(?:Causes?|Difficulty|Summary|Affinity) of\b/iu,
  /^Absence or rarity of\b/iu,
  /^Means of dispersal\b/iu,
  /^Dispersal during\b/iu,
  /^First, for the\b/iu,
  /^On (?:the (?:generality|absence|nature|vast|intermittence|sudden|slow|affinities|state|succession)|their (?:sudden|different))\b/iu,
]
const abbreviationEndingPattern =
  /(?:\b(?:Mr|Mrs|Ms|Dr|Prof|Rev|St|Sir|Jr|Sr|vs|etc|No|Messrs|Plin|viz)\.|(?:\b[A-HJ-Z]\.){1,4}|\b[A-Z]{2,4}\.)[”"')\]]?$/u

for (const [index, entry] of runtime.entries.entries()) {
  const canonical = shardEntries[index]
  invariant(
    jsonEqual(Object.keys(entry), runtimeEntryKeys),
    `Runtime entry ${index} has an unexpected shape.`,
  )
  invariant(
    jsonEqual(Object.keys(canonical), canonicalEntryKeys),
    `Canonical entry ${index} has an unexpected shape.`,
  )
  invariant(entry.index === index && canonical.index === index, `Bad index at ${index}.`)
  invariant(
    jsonEqual(
      entry,
      Object.fromEntries(runtimeEntryKeys.map((key) => [key, canonical[key]])),
    ),
    `Runtime and canonical entries differ at ${index}.`,
  )

  for (const [field, value] of [
    ['title', entry.title],
    ['excerpt', entry.excerpt],
    ['category', entry.category],
  ]) {
    invariant(typeof value === 'string' && value.length > 0, `Blank ${field} at ${index}.`)
    invariant(value === value.trim(), `Untrimmed ${field} at ${index}.`)
    invariant(value === value.normalize('NFC'), `Non-NFC ${field} at ${index}.`)
  }

  invariant(wordCount(entry.title) >= 3 && wordCount(entry.title) <= 6, `Bad title words at ${index}.`)
  invariant(countCharacters(entry.title) <= 35, `Title too long at ${index}.`)
  invariant(!/\b(?:root|claim|evidence|objection)\s+\d/iu.test(entry.title), `Kind/ID title at ${index}.`)
  const titleKey = ordinalKey(entry.title)
  invariant(!titleKeys.has(titleKey), `Case-insensitive duplicate title at ${index}.`)
  titleKeys.add(titleKey)

  invariant(
    countCharacters(entry.excerpt) <= 232,
    `Excerpt exceeds the 232-character budget at ${index}.`,
  )
  invariant(
    countCharacters(`Objection 99999 — ${entry.excerpt}`) <= 250,
    `Worst-case runtime body exceeds 250 characters at ${index}.`,
  )
  invariant(/[.!?][”’"')\]]?$/.test(entry.excerpt), `Incomplete excerpt at ${index}.`)
  invariant(
    index < 180 || !/(?:…|\.{3,})/.test(entry.excerpt),
    `Clipped or source-artifact excerpt at ${index}.`,
  )
  const excerptKey = ordinalKey(entry.excerpt)
  invariant(!excerptKeys.has(excerptKey), `Case-insensitive duplicate excerpt at ${index}.`)
  excerptKeys.add(excerptKey)
  const comparableKey = comparableExcerptKey(entry.excerpt)
  invariant(
    !comparableExcerptKeys.has(comparableKey),
    `Punctuation-normalized duplicate excerpt at ${index}.`,
  )
  comparableExcerptKeys.add(comparableKey)

  invariant(Array.isArray(entry.tags) && entry.tags.length > 0, `Missing tags at ${index}.`)
  invariant(new Set(entry.tags).size === entry.tags.length, `Duplicate tags at ${index}.`)
  invariant(jsonEqual(entry.tags, [...entry.tags].sort()), `Unsorted tags at ${index}.`)
  for (const tag of entry.tags) {
    invariant(tag && tag === tag.trim(), `Blank or untrimmed tag at ${index}.`)
    invariant(tag === tag.normalize('NFC'), `Non-NFC tag at ${index}.`)
  }

  invariant(sourceById.has(canonical.sourceId), `Unknown source at ${index}.`)
  const canonicalSource = sourceById.get(canonical.sourceId)
  invariant(canonical.section?.trim(), `Missing section at ${index}.`)
  invariant(
    canonical.candidateIds.length >= 1 && canonical.candidateIds.length <= 2,
    `Bad candidate count at ${index}.`,
  )
  invariant(
    canonical.sourceBlockLineStart <= canonical.sourceBlockLineEnd,
    `Bad source lines at ${index}.`,
  )
  if (index >= 180) {
    invariant(canonical.sampleClass === 'generated', `Bad sample class at ${index}.`)
    invariant(!/^[<>]/.test(entry.excerpt), `Leading source artifact remains at ${index}.`)
    invariant(
      canonical.titleMethod === 'contiguous-source-phrase' ||
        canonical.titleMethod === 'ordered-source-keywords',
      `Bad title method at ${index}.`,
    )
    if (sensitiveContentPolicy) {
      invariant(
        !sensitiveContentRejectionReason(entry.excerpt, sensitiveContentPolicy),
        `Filtered content remains at ${index}.`,
      )
    }
    invariant(
      (entry.excerpt.match(/\(/g) ?? []).length ===
        (entry.excerpt.match(/\)/g) ?? []).length,
      `Unbalanced parentheses remain at ${index}.`,
    )
    invariant(
      !outlineHeadingPatterns.some((pattern) => pattern.test(entry.excerpt)),
      `Outline heading remains at ${index}.`,
    )
    invariant(
      !canonicalSource.excludedOutlineLineRanges?.some(
        (range) =>
          canonical.sourceBlockLineStart >= range.start &&
          canonical.sourceBlockLineEnd <= range.end,
      ),
      `Source-declared outline block remains at ${index}.`,
    )
    invariant(
      !abbreviationEndingPattern.test(entry.excerpt),
      `Abbreviation-ending fragment remains at ${index}.`,
    )
  }
}

const expectedReviewedContent = preview.entries.map(({ ordinal, title, excerpt }) => ({
  index: ordinal,
  title,
  excerpt,
}))
const actualReviewedContent = runtime.entries
  .slice(0, 180)
  .map(({ index, title, excerpt }) => ({ index, title, excerpt }))
invariant(
  jsonEqual(actualReviewedContent, expectedReviewedContent),
  'Runtime indexes 0–179 differ from the committed preview.',
)
invariant(
  sha256(JSON.stringify(actualReviewedContent)) ===
    corpusManifest.fingerprints.reviewedContent.sha256,
  'Reviewed-prefix fingerprint is stale.',
)
invariant(
  corpusManifest.fingerprints.first1000 ===
    sha256(JSON.stringify(runtime.entries.slice(0, 1_000))),
  '1K prefix fingerprint is stale.',
)
invariant(
  corpusManifest.fingerprints.first10000 === sha256(JSON.stringify(runtime.entries)),
  '10K fingerprint is stale.',
)

invariant(
  jsonEqual(
    corpusManifest.statistics.entriesBySource,
    countBy(shardEntries.map((entry) => entry.sourceId)),
  ),
  'Source statistics are stale.',
)
invariant(
  jsonEqual(
    corpusManifest.statistics.titleMethods,
    countBy(shardEntries.map((entry) => entry.titleMethod)),
  ),
  'Title-method statistics are stale.',
)
for (const sourceRecord of corpusManifest.sources.entries) {
  const source = sourceById.get(sourceRecord.id)
  invariant(source, `Unknown manifest source ${sourceRecord.id}.`)
  invariant(
    sourceRecord.sourceSha256 === source.sha256 &&
      sourceRecord.selectedEntries ===
        shardEntries.filter((entry) => entry.sourceId === source.id).length,
    `Source metadata is stale for ${source.id}.`,
  )
}

// Materialize only indexes, not another 100K corpus: this verifies the agreed
// ten exact ordered repetitions without producing a redundant artifact.
for (let nodeIndex = 0; nodeIndex < 100_000; nodeIndex += 1) {
  invariant(
    runtime.entries[nodeIndex % 10_000].index === nodeIndex % 10_000,
    `100K modulo reuse failed at ${nodeIndex}.`,
  )
}

if (sourceDirectory) {
  const candidateBySource = new Map()
  for (const source of sourceManifest.sources) {
    const { candidates } = await loadSourceCandidates(source, sourceDirectory)
    candidateBySource.set(
      source.id,
      new Map(candidates.map((candidate) => [candidate.id, candidate])),
    )
  }

  for (const [index, canonical] of shardEntries.entries()) {
    const candidateLookup = candidateBySource.get(canonical.sourceId)
    const candidates = canonical.candidateIds.map((candidateId) => {
      const candidate = candidateLookup.get(candidateId)
      invariant(candidate, `Unknown source candidate ${candidateId} at ${index}.`)
      return candidate
    })
    invariant(
      canonical.excerpt === candidates.map((candidate) => candidate.text).join(' '),
      `Source text mismatch at ${index}.`,
    )
    invariant(
      canonical.sourceBlockLineStart ===
        Math.min(...candidates.map((candidate) => candidate.sourceBlockLineStart)) &&
        canonical.sourceBlockLineEnd ===
          Math.max(...candidates.map((candidate) => candidate.sourceBlockLineEnd)),
      `Source line mismatch at ${index}.`,
    )
    if (index >= 180) {
      invariant(candidates.length === 1, `Generated excerpt ${index} must use one sentence.`)
      invariant(
        !candidates[0].precededByAbbreviationBoundary,
        `Abbreviation-boundary fragment remains at ${index}.`,
      )
    }
  }
}

process.stdout.write(
  `Validated ${runtime.entries.length} corpus entries, ${titleKeys.size} titles, ${excerptKeys.size} excerpts${sourceDirectory ? ', and all pinned source locators' : ''}.\n`,
)
process.stdout.write(
  sensitiveContentPolicy
    ? 'Editorial scan: PASSED using the explicitly supplied complete policy.\n'
    : 'Editorial scan: SKIPPED (no --content-policy supplied).\n',
)
