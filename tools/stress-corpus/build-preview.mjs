#!/usr/bin/env node

import { readFile, writeFile, mkdir } from 'node:fs/promises'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'
import {
  countCharacters,
  loadSourceCandidates,
  sha256,
  slugify,
} from './corpus-lib.mjs'

const here = path.dirname(fileURLToPath(import.meta.url))
const repositoryRoot = path.resolve(here, '../..')
const manifestPath = path.join(here, 'corpus-sources.v1.json')
const selectionPaths = [
  path.join(here, 'drafts/selection-classics.json'),
  path.join(here, 'drafts/selection-arguments.json'),
  path.join(here, 'drafts/selection-modern.json'),
]
const reviewDirectory = path.join(
  repositoryRoot,
  'docs/review/stress-corpus/round-01',
)
const jsonOutputPath = path.join(reviewDirectory, 'corpus-preview.json')
const markdownOutputPath = path.join(reviewDirectory, 'corpus-preview.md')
const fixtureOutputPath = path.join(
  repositoryRoot,
  'frontend/src/fixtures/review/insightsNodeTitleCorpus.ts',
)

const sourceDirectoryFlag = process.argv.indexOf('--source-dir')
const sourceDirectory =
  sourceDirectoryFlag >= 0 ? process.argv[sourceDirectoryFlag + 1] : null
const checkOnly = process.argv.includes('--check')

if (!sourceDirectory) {
  throw new Error(
    'Usage: node tools/stress-corpus/build-preview.mjs --source-dir SOURCE_DIRECTORY [--check]',
  )
}

const manifest = JSON.parse(await readFile(manifestPath, 'utf8'))
const selectionFragments = await Promise.all(
  selectionPaths.map(async (selectionPath) =>
    JSON.parse(await readFile(selectionPath, 'utf8')),
  ),
)
const selection = {
  schemaVersion: 1,
  corpusId: manifest.corpusId,
  sources: selectionFragments.flatMap((fragment) => fragment.sources),
}
const toPortablePath = (value) => value.split(path.sep).join('/')

const invariant = (condition, message) => {
  if (!condition) throw new Error(message)
}

const kindForIndex = (index) => {
  if (index === 0) return 'root'
  if (index % 5 === 0) return 'evidence'
  if (index % 10 === 2) return 'objection'
  return 'claim'
}

const sourceLookups = new Map()
for (const source of manifest.sources) {
  const loaded = await loadSourceCandidates(source, sourceDirectory)
  sourceLookups.set(
    source.id,
    new Map(loaded.candidates.map((candidate) => [candidate.id, candidate])),
  )
}

invariant(selection.schemaVersion === 1, 'Selection schemaVersion must be 1.')
invariant(selection.corpusId === manifest.corpusId, 'Corpus IDs do not match.')
invariant(selection.sources.length === manifest.sources.length, 'Every source needs selections.')

const selectionBySource = new Map(
  selection.sources.map((source) => [source.sourceId, source]),
)
const selectedTitles = new Set()
const selectedCandidateGroups = new Set()
const selectedCandidateIds = new Set()

const buildEntry = (source, selected, ordinal, sampleClass) => {
  const lookup = sourceLookups.get(source.id)
  invariant(lookup, `No candidates loaded for ${source.id}.`)
  invariant(
    Array.isArray(selected.candidateIds) && selected.candidateIds.length >= 1 && selected.candidateIds.length <= 2,
    `${source.id} selection ${ordinal} must contain one or two sentences.`,
  )

  const candidates = selected.candidateIds.map((candidateId) => {
    const candidate = lookup.get(candidateId)
    invariant(candidate, `Unknown candidate ${candidateId}.`)
    return candidate
  })
  if (candidates.length === 2) {
    invariant(
      candidates[0].paragraph === candidates[1].paragraph &&
        candidates[1].sentenceIndex === candidates[0].sentenceIndex + 1,
      `${selected.candidateIds.join(', ')} are not adjacent source sentences.`,
    )
  }

  const candidateGroup = selected.candidateIds.join('|')
  invariant(!selectedCandidateGroups.has(candidateGroup), `Duplicate excerpt ${candidateGroup}.`)
  selectedCandidateGroups.add(candidateGroup)
  for (const candidateId of selected.candidateIds) {
    invariant(!selectedCandidateIds.has(candidateId), `Reused source sentence ${candidateId}.`)
    selectedCandidateIds.add(candidateId)
  }

  const title = selected.title.normalize('NFC').trim()
  const titleKey = title.toLocaleLowerCase('en-US')
  const titleWords = title.split(/\s+/).length
  invariant(titleWords >= 3 && titleWords <= 6, `${title} must contain 3–6 words.`)
  invariant(countCharacters(title) <= 35, `${title} exceeds 35 characters.`)
  invariant(!selectedTitles.has(titleKey), `Duplicate title: ${title}.`)
  invariant(!/\b(?:root|claim|evidence|objection)\s+\d/i.test(title), `${title} contains a kind and ID.`)
  selectedTitles.add(titleKey)

  const candidateSegments = candidates.map((candidate) => candidate.text)
  if (selected.combineAsOneSentence) {
    invariant(
      sampleClass === 'edge-case' && candidateSegments.length === 2,
      `${candidateGroup} may rejoin a tokenizer boundary only as a two-segment edge case.`,
    )
  }
  const sentences = selected.combineAsOneSentence
    ? [candidateSegments.join(' ')]
    : candidateSegments
  for (const sentence of sentences) {
    invariant(/[.!?][”’"')\]]?$/.test(sentence), `Incomplete sentence: ${sentence}`)
  }
  const excerpt = sentences.join(' ')
  invariant(!excerpt.endsWith('…'), `Excerpt appears truncated: ${excerpt}`)

  const kind = kindForIndex(ordinal)
  const displayKind = `${kind[0].toUpperCase()}${kind.slice(1)}`
  const bodyPrefix = `${displayKind} ${String(ordinal).padStart(5, '0')} — `
  const bodyText = `${bodyPrefix}${excerpt}`
  invariant(countCharacters(bodyText) <= 250, `${bodyText} exceeds 250 characters.`)

  const section = candidates[0].section
  invariant(
    candidates.every((candidate) => candidate.section === section),
    `${candidateGroup} crosses source sections.`,
  )
  const tags = [
    `author:${slugify(source.author)}`,
    `corpus:${manifest.corpusId}`,
    'public-domain',
    `sample:${sampleClass}`,
    `section:${section}`,
    'stress',
    `work:${source.id}`,
  ].sort()

  return {
    id: `corpus-preview-${String(ordinal).padStart(3, '0')}`,
    ordinal,
    sampleClass,
    nodeIndex: ordinal,
    kind,
    title,
    titleCharacters: countCharacters(title),
    titleWords,
    sentences,
    sentenceCount: sentences.length,
    excerpt,
    bodyText,
    bodyCharacters: countCharacters(bodyText),
    bodyUtf16Units: bodyText.length,
    tags,
    provenance: {
      type: 'public-domain-source',
      sourceId: source.id,
      ebookNumber: source.ebookNumber,
      work: source.title,
      author: source.author,
      translator: source.translator,
      canonicalUrl: source.canonicalUrl,
      publicDomainStatus: 'Public domain in the USA',
      retrievedOn: manifest.retrievedOn,
      sourceSha256: source.sha256,
      section,
      candidateIds: selected.candidateIds,
      sourceBlockLineStart: Math.min(
        ...candidates.map((candidate) => candidate.sourceBlockLineStart),
      ),
      sourceBlockLineEnd: Math.max(
        ...candidates.map((candidate) => candidate.sourceBlockLineEnd),
      ),
      excerptSha256: sha256(excerpt),
    },
    review: {
      ...(sampleClass === 'edge-case'
        ? {
            edgeCaseId: selected.caseId,
            rationale: selected.rationale,
          }
        : {}),
      ...(selected.originalText ? { originalText: selected.originalText } : {}),
      ...(selected.combineAsOneSentence
        ? { rejoinedTokenizerBoundary: true }
        : {}),
      ...(selected.reviewFlags ? { flags: [...selected.reviewFlags].sort() } : {}),
      ...(selected.reviewNote ? { note: selected.reviewNote } : {}),
      status: 'candidate',
    },
  }
}

for (const source of manifest.sources) {
  const sourceSelection = selectionBySource.get(source.id)
  invariant(sourceSelection, `Missing selections for ${source.id}.`)
  invariant(sourceSelection.representative.length === 16, `${source.id} needs 16 representative selections.`)
  invariant(sourceSelection.edgeCases.length === 2, `${source.id} needs 2 edge cases.`)
}

const entries = []
for (let round = 0; round < 16; round += 1) {
  for (const source of manifest.sources) {
    entries.push(
      buildEntry(
        source,
        selectionBySource.get(source.id).representative[round],
        entries.length,
        'representative',
      ),
    )
  }
}
for (let round = 0; round < 2; round += 1) {
  for (const source of manifest.sources) {
    entries.push(
      buildEntry(
        source,
        selectionBySource.get(source.id).edgeCases[round],
        entries.length,
        'edge-case',
      ),
    )
  }
}

const countBy = (values) =>
  Object.fromEntries(
    [...new Set(values)].sort().map((value) => [value, values.filter((item) => item === value).length]),
  )

const preview = {
  schemaVersion: 1,
  artifactId: 'stress-corpus-preview-r01-data',
  corpusId: manifest.corpusId,
  reviewRound: 1,
  status: 'candidate',
  scope: 'Review-only corpus preview; not consumed by database reset or runtime stress seeds.',
  futurePlacementContract: {
    authorized: false,
    shapeNeutralTextAndTags: true,
    prefixRule:
      'If separately accepted for runtime placement, every graph shape and size will draw from this same ordinal stream so 1K is a prefix of 10K and 10K is a prefix of 100K.',
  },
  titleMethod: 'Codex-curated semantic summaries, revision 1',
  ordering: 'Sixteen representative rounds interleaved by source, followed by two edge-case rounds interleaved by source.',
  constraints: {
    representativeEntries: 160,
    edgeCaseEntries: 20,
    sentencesPerEntry: [1, 2],
    maximumBodyCharactersIncludingPrefix: 250,
    titleCharacters: { minimum: 1, maximum: 35 },
    titleWords: { minimum: 3, maximum: 6 },
    truncationAllowed: false,
  },
  rights: {
    status: 'Each underlying edition is marked public domain in the USA by Project Gutenberg.',
    caveat: 'Users outside the United States should check local law.',
    projectGutenbergPermissionUrl: 'https://www.gutenberg.org/policy/permission',
    projectGutenbergLicenseUrl: 'https://www.gutenberg.org/policy/license',
  },
  sourceTextNormalization: {
    version: 1,
    unicode: 'NFC',
    whitespace: 'Collapsed to single spaces and trimmed.',
    displayMarkup:
      'Project Gutenberg emphasis markers, page markers, numeric footnote markers, and Aristotle Bekker page markers are removed before sentence selection.',
    punctuation: 'Double hyphens are normalized to em dashes.',
    paragraphLeadTypography:
      'A paragraph-leading word printed in all capitals is normalized to sentence case.',
    clipping: false,
  },
  sourceManifest: {
    path: 'tools/stress-corpus/corpus-sources.v1.json',
    hashMethod: 'SHA-256 of JSON.stringify(parsed JSON)',
    sha256: sha256(JSON.stringify(manifest)),
    sourceCount: manifest.sources.length,
  },
  selectionManifest: {
    paths: selectionPaths.map((selectionPath) =>
      toPortablePath(path.relative(repositoryRoot, selectionPath)),
    ),
    hashMethod: 'SHA-256 of JSON.stringify(parsed selection documents in path order)',
    sha256: sha256(JSON.stringify(selectionFragments)),
  },
  stats: {
    totalEntries: entries.length,
    sampleClasses: countBy(entries.map((entry) => entry.sampleClass)),
    kinds: countBy(entries.map((entry) => entry.kind)),
    sources: countBy(entries.map((entry) => entry.provenance.sourceId)),
    sentenceCounts: countBy(entries.map((entry) => String(entry.sentenceCount))),
    bodyCharacters: {
      minimum: Math.min(...entries.map((entry) => entry.bodyCharacters)),
      maximum: Math.max(...entries.map((entry) => entry.bodyCharacters)),
      under100: entries.filter((entry) => entry.bodyCharacters < 100).length,
    },
    titleCharacters: {
      minimum: Math.min(...entries.map((entry) => entry.titleCharacters)),
      maximum: Math.max(...entries.map((entry) => entry.titleCharacters)),
    },
  },
  entriesFingerprint: sha256(JSON.stringify(entries)),
  entries,
}

const jsonOutput = `${JSON.stringify(preview, null, 2)}\n`

const markdownLines = [
  '# Stress corpus preview — round 01',
  '',
  '> Candidate review artifact only. These excerpts are not wired into database reset or any runtime stress graph.',
  '',
  `This batch contains **${entries.length} entries**: ${preview.stats.sampleClasses.representative} representative candidates and ${preview.stats.sampleClasses['edge-case']} deliberate edge cases. Bodies contain one or two complete public-domain source sentences; the node kind and five-digit ID prefix count toward the 250-character ceiling.`,
  '',
  '## Review contract',
  '',
  '- Titles are semantic summaries of 3–6 words and at most 35 characters.',
  '- Bodies are never clipped, padded, or ended with a synthetic ellipsis.',
  '- Every source edition is pinned by SHA-256 and linked to its Project Gutenberg catalog record.',
  '- Display-only Gutenberg markers, page and numeric footnote markers, repeated whitespace, double-hyphen dashes, and paragraph-leading all-caps typography are normalized consistently; source block-line locators remain available for comparison.',
  '- Source acceptance and runtime-placement acceptance are separate decisions; both remain pending.',
  '- The Storybook title grid renders these titles at the production 230×112 node size and narrow parent-node title width.',
  '',
  '## Sources',
  '',
  '| Source | Author | Translator | Entries | Project Gutenberg |',
  '| --- | --- | --- | ---: | --- |',
  ...manifest.sources.map(
    (source) =>
      `| ${source.title} | ${source.author} | ${source.translator ?? '—'} | ${preview.stats.sources[source.id]} | [#${source.ebookNumber}](${source.canonicalUrl}) |`,
  ),
  '',
  '## Candidates',
  '',
]

for (const entry of entries) {
  markdownLines.push(
    `### ${String(entry.ordinal).padStart(3, '0')} · ${entry.kind} · ${entry.sampleClass}`,
    '',
    `**Title:** ${entry.title} — ${entry.titleCharacters} chars / ${entry.titleWords} words`,
    '',
    `**Body:** ${entry.bodyText}`,
    '',
    `**Body measurement:** ${entry.bodyCharacters} chars / ${entry.sentenceCount} source sentence${entry.sentenceCount === 1 ? '' : 's'}`,
    '',
    `**Source:** [${entry.provenance.work}](${entry.provenance.canonicalUrl}) by ${entry.provenance.author}${entry.provenance.translator ? `, translated by ${entry.provenance.translator}` : ''}; section \`${entry.provenance.section}\`; source block lines ${entry.provenance.sourceBlockLineStart}–${entry.provenance.sourceBlockLineEnd}`,
    '',
    `**Tags:** ${entry.tags.map((tag) => `\`${tag}\``).join(', ')}`,
    '',
  )
  if (entry.review.edgeCaseId) {
    markdownLines.push(
      `**Edge-case focus:** \`${entry.review.edgeCaseId}\` — ${entry.review.rationale}`,
      '',
    )
  }
  if (entry.review.flags) {
    markdownLines.push(
      `**Review flags:** ${entry.review.flags.map((flag) => `\`${flag}\``).join(', ')}`,
      '',
    )
  }
  if (entry.review.note) {
    markdownLines.push(`**Review note:** ${entry.review.note}`, '')
  }
  if (entry.review.originalText) {
    markdownLines.push('**Original text before material normalization:**', '', entry.review.originalText, '')
  }
}

const markdownOutput = `${markdownLines.join('\n')}\n`

const fixtureLines = [
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
  ...entries.flatMap((entry) => [
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
]
const fixtureOutput = fixtureLines.join('\n')

const outputs = [
  [jsonOutputPath, jsonOutput],
  [markdownOutputPath, markdownOutput],
  [fixtureOutputPath, fixtureOutput],
]

if (checkOnly) {
  for (const [outputPath, expected] of outputs) {
    const actual = await readFile(outputPath, 'utf8')
    invariant(actual === expected, `${path.relative(repositoryRoot, outputPath)} is stale.`)
  }
  process.stdout.write(`Verified ${entries.length} corpus preview entries.\n`)
} else {
  for (const [outputPath, content] of outputs) {
    await mkdir(path.dirname(outputPath), { recursive: true })
    await writeFile(outputPath, content)
  }
  process.stdout.write(`Generated ${entries.length} corpus preview entries.\n`)
}
