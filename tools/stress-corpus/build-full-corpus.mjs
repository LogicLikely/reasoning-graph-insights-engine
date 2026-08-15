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
const nodeVersionPath = path.join(repositoryRoot, '.nvmrc')

const CORPUS_SIZE = 10_000
const REVIEWED_PREFIX_SIZE = 180
const SHARD_SIZE = 1_000
const MAX_EXCERPT_CHARACTERS = 232
const EXPECTED_REVIEWED_CONTENT_FINGERPRINT =
  '7a568f92d744cae8d790361380a7df8622e8d93c47f3b662e85698281a9443b0'

const args = process.argv.slice(2)
assertKnownArguments(args, {
  valueFlags: ['--source-dir', '--content-policy'],
  booleanFlags: ['--check'],
})
const sourceDirectory = argumentValue(args, '--source-dir')
const contentPolicyPath = argumentValue(args, '--content-policy')
const checkOnly = args.includes('--check')

if (!sourceDirectory || !contentPolicyPath) {
  throw new Error(
    'Usage: node tools/stress-corpus/build-full-corpus.mjs --source-dir SOURCE_DIRECTORY ' +
      '--content-policy POLICY_PATH [--check]. Canonical builds require the explicit, ' +
      'complete maintainer policy; the tracked example is intentionally incomplete.',
  )
}

const sensitiveContentPolicy = await loadSensitiveContentPolicy(contentPolicyPath, {
  requireComplete: true,
})

const invariant = (condition, message) => {
  if (!condition) throw new Error(message)
}

const json = (value) => `${JSON.stringify(value, null, 2)}\n`
const portablePath = (value) =>
  path.relative(repositoryRoot, value).split(path.sep).join('/')
const ordinalKey = (value) => value.normalize('NFC').toUpperCase()
const comparableExcerptKey = (value) =>
  ordinalKey(value)
    .normalize('NFKD')
    .replace(/[\p{P}\p{S}]/gu, ' ')
    .replace(/\s+/g, ' ')
    .trim()
const countBy = (values) =>
  Object.fromEntries(
    [...new Set(values)]
      .sort()
      .map((value) => [value, values.filter((candidate) => candidate === value).length]),
  )
const sortedRecord = (record) =>
  Object.fromEntries(Object.entries(record).sort(([left], [right]) => left.localeCompare(right)))

const sourceManifestBytes = await readFile(sourceManifestPath)
const sourceManifest = JSON.parse(sourceManifestBytes.toString('utf8'))
const preview = JSON.parse(await readFile(previewPath, 'utf8'))
const expectedNodeVersion = (await readFile(nodeVersionPath, 'utf8')).trim()

invariant(sourceManifest.schemaVersion === 1, 'Source manifest schemaVersion must be 1.')
invariant(sourceManifest.corpusId === preview.corpusId, 'Corpus IDs do not match.')
invariant(
  process.versions.node === expectedNodeVersion,
  `Use Node ${expectedNodeVersion}; received ${process.versions.node}.`,
)
invariant(
  preview.entries.length === REVIEWED_PREFIX_SIZE,
  `Reviewed prefix must contain ${REVIEWED_PREFIX_SIZE} entries.`,
)

const reviewedContent = preview.entries.map(({ ordinal, title, excerpt }) => ({
  index: ordinal,
  title,
  excerpt,
}))
invariant(
  sha256(JSON.stringify(reviewedContent)) === EXPECTED_REVIEWED_CONTENT_FINGERPRINT,
  'The committed 180-entry review corpus changed; intentionally update the frozen fingerprint before rebuilding.',
)

const sourceById = new Map(sourceManifest.sources.map((source) => [source.id, source]))
invariant(
  sourceById.size === sourceManifest.sources.length,
  'Source IDs must be unique.',
)
for (const source of sourceManifest.sources) {
  invariant(source.category?.trim(), `${source.id} must define a category.`)
}
for (const previewEntry of preview.entries) {
  invariant(
    sourceById.has(previewEntry.provenance.sourceId),
    `Reviewed entry ${previewEntry.ordinal} has an unknown source.`,
  )
}

const minorTitleWords = new Set([
  'a',
  'about',
  'after',
  'again',
  'almost',
  'also',
  'an',
  'and',
  'any',
  'as',
  'at',
  'be',
  'because',
  'been',
  'before',
  'being',
  'but',
  'by',
  'can',
  'certainly',
  'could',
  'did',
  'do',
  'does',
  'either',
  'even',
  'ever',
  'especially',
  'for',
  'from',
  'had',
  'has',
  'have',
  'here',
  'how',
  'however',
  'if',
  'in',
  'indeed',
  'into',
  'is',
  'just',
  'like',
  'may',
  'might',
  'more',
  'most',
  'must',
  'nearly',
  'never',
  'nor',
  'not',
  'now',
  'of',
  'on',
  'only',
  'or',
  'other',
  'over',
  'per',
  'perhaps',
  'particularly',
  'probably',
  'really',
  'shall',
  'should',
  'so',
  'some',
  'such',
  'than',
  'the',
  'then',
  'there',
  'to',
  'upon',
  'very',
  'via',
  'was',
  'were',
  'what',
  'when',
  'where',
  'whether',
  'which',
  'while',
  'who',
  'whom',
  'whose',
  'why',
  'will',
  'with',
  'without',
  'would',
  'yet',
])
const weakTitleWords = new Set([
  ...minorTitleWords,
  'he',
  'her',
  'hers',
  'him',
  'his',
  'i',
  'it',
  'its',
  'me',
  'my',
  'our',
  'ours',
  'she',
  'that',
  'their',
  'theirs',
  'them',
  'these',
  'they',
  'this',
  'those',
  'us',
  'we',
  'you',
  'your',
])
const attributionWords = new Set([
  'answered',
  'asked',
  'cried',
  'exclaimed',
  'replied',
  'said',
])

const artifactPatterns = [
  /\bproject gutenberg\b/iu,
  /\b(?:ebook|e-book)\b/iu,
  /\b(?:footnote|illustration)\b/iu,
  /\b(?:contents|index)\b/iu,
  /\bchapter\s+(?:\d+|[ivxlcdm]+)\b/iu,
  /\b(?:fig|figure|plate|table)\.?(?:\s|$)/iu,
  /(?:^|\s)(?:p|pp)\.\s*\d/iu,
  /\b(?:page|volume)\s+\d/iu,
  /\bcopyright\b/iu,
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

const firstLetterIsUppercase = (text) => {
  const match = text.match(/\p{L}/u)
  if (!match) return false
  const [letter] = match
  return letter === letter.toLocaleUpperCase('en-US')
}

const filterReason = (candidate, source) => {
  const { text, words } = candidate
  const chars = countCharacters(text)
  if (chars > MAX_EXCERPT_CHARACTERS) return 'too-long'
  if (chars < 28 || words < 5) return 'too-short'
  if (!firstLetterIsUppercase(text)) return 'lowercase-fragment'
  if (candidate.precededByAbbreviationBoundary) {
    return 'abbreviation-boundary-fragment'
  }
  if (/^[\s—–\-”’(){}\]<>]/.test(text)) return 'boundary-fragment'
  if (!/[.!?][”’"')\]]?$/.test(text) || /(?:…|\.\.\.)$/.test(text)) {
    return 'incomplete-ending'
  }
  if (/(?:…|\.{3,})/.test(text)) return 'source-artifact'
  if (/[\[\]{}|]|\/\*/.test(text)) return 'markup-artifact'
  if (
    (text.match(/\(/g) ?? []).length !== (text.match(/\)/g) ?? []).length
  ) {
    return 'unbalanced-parentheses'
  }
  if (
    /(?:\b(?:Mr|Mrs|Ms|Dr|Prof|Rev|St|Sir|Jr|Sr|vs|etc|No|Messrs|Plin|viz)\.|(?:\b[A-HJ-Z]\.){1,4}|\b[A-Z]{2,4}\.)[”"')\]]?$/u.test(
      text,
    )
  ) {
    return 'abbreviation-ending-fragment'
  }
  if (outlineHeadingPatterns.some((pattern) => pattern.test(text))) {
    return 'outline-heading'
  }
  if (
    source.excludedOutlineLineRanges?.some(
      (range) =>
        candidate.sourceBlockLineStart >= range.start &&
        candidate.sourceBlockLineEnd <= range.end,
    )
  ) {
    return 'outline-heading'
  }
  if (artifactPatterns.some((pattern) => pattern.test(text))) return 'source-artifact'
  const sensitiveContentReason = sensitiveContentRejectionReason(
    text,
    sensitiveContentPolicy,
  )
  if (sensitiveContentReason) return sensitiveContentReason

  const asciiQuotes = (text.match(/"/g) ?? []).length
  const curlyOpeningQuotes = (text.match(/“/g) ?? []).length
  const curlyClosingQuotes = (text.match(/”/g) ?? []).length
  if (asciiQuotes % 2 !== 0 || curlyOpeningQuotes !== curlyClosingQuotes) {
    return 'unbalanced-quotation'
  }

  const letters = [...text].filter((character) => /\p{L}/u.test(character))
  const uppercaseLetters = letters.filter(
    (character) => character === character.toLocaleUpperCase('en-US'),
  )
  if (letters.length > 0 && uppercaseLetters.length / letters.length > 0.55) {
    return 'heading-or-list'
  }
  if ((text.match(/\b\d+[.)]?/g) ?? []).length >= 3) return 'numeric-list'
  return null
}

const wordTokens = (text) =>
  text.match(/\p{L}[\p{L}\p{M}]*(?:[’'-]\p{L}[\p{L}\p{M}]*)*/gu) ?? []

const titleWord = (word, position) => {
  const lower = word.toLocaleLowerCase('en-US')
  if (position > 0 && minorTitleWords.has(lower)) return lower
  if (/^[A-Z]{2,4}$/u.test(word)) return word
  return `${lower[0].toLocaleUpperCase('en-US')}${lower.slice(1)}`
}

const renderTitle = (words) => words.map(titleWord).join(' ')

const titleOptions = (excerpt) => {
  const words = wordTokens(excerpt)
  const options = new Map()

  const offer = (selection, score, mode) => {
    if (selection.length < 3 || selection.length > 6) return
    const lowered = selection.map((word) => word.toLocaleLowerCase('en-US'))
    if (weakTitleWords.has(lowered[0]) || weakTitleWords.has(lowered.at(-1))) return
    if (lowered.filter((word) => !weakTitleWords.has(word)).length < 3) return
    if (selection.some((word) => countCharacters(word) === 1 && !/^[ai]$/iu.test(word))) {
      return
    }
    const title = renderTitle(selection).normalize('NFC')
    if (countCharacters(title) > 35) return
    if (/\d/u.test(title)) return
    if (/\b(?:root|claim|evidence|objection)\s+\d/iu.test(title)) return

    const key = ordinalKey(title)
    const attributionPenalty = lowered.some((word) => attributionWords.has(word)) ? 22 : 0
    const repeatedPenalty = new Set(lowered).size === lowered.length ? 0 : 18
    const candidate = {
      title,
      score: score - attributionPenalty - repeatedPenalty,
      mode,
    }
    const current = options.get(key)
    if (!current || candidate.score > current.score) options.set(key, candidate)
  }

  const preferredWordCount = new Map([
    [3, 34],
    [4, 48],
    [5, 44],
    [6, 30],
  ])
  for (let start = 0; start < Math.min(words.length, 24); start += 1) {
    for (let size = 3; size <= 6 && start + size <= words.length; size += 1) {
      const selection = words.slice(start, start + size)
      const contentLetters = selection
        .filter((word) => !weakTitleWords.has(word.toLocaleLowerCase('en-US')))
        .reduce((sum, word) => sum + countCharacters(word), 0)
      offer(
        selection,
        200 + preferredWordCount.get(size) - start * 2 + Math.min(contentLetters, 28),
        'contiguous-source-phrase',
      )
    }
  }

  const contentWords = words.filter(
    (word) =>
      !weakTitleWords.has(word.toLocaleLowerCase('en-US')) &&
      !attributionWords.has(word.toLocaleLowerCase('en-US')) &&
      (countCharacters(word) > 1 || /^[ai]$/iu.test(word)),
  )
  for (let start = 0; start < Math.min(contentWords.length, 18); start += 1) {
    for (let size = 3; size <= 6 && start + size <= contentWords.length; size += 1) {
      offer(
        contentWords.slice(start, start + size),
        240 +
          preferredWordCount.get(size) -
          start +
          Math.min(
            contentWords
              .slice(start, start + size)
              .reduce((sum, word) => sum + countCharacters(word), 0),
            28,
          ),
        'ordered-source-keywords',
      )
    }
  }

  return [...options.values()].sort(
    (left, right) =>
      right.score - left.score ||
      left.title.localeCompare(right.title, 'en-US', { sensitivity: 'variant' }),
  )
}

const usedCandidateIds = new Set(
  preview.entries.flatMap((entry) => entry.provenance.candidateIds),
)
const usedExcerptKeys = new Set(preview.entries.map((entry) => ordinalKey(entry.excerpt)))
const usedComparableExcerptKeys = new Set(
  preview.entries.map((entry) => comparableExcerptKey(entry.excerpt)),
)
const usedTitleKeys = new Set(preview.entries.map((entry) => ordinalKey(entry.title)))
invariant(
  usedExcerptKeys.size === REVIEWED_PREFIX_SIZE &&
    usedComparableExcerptKeys.size === REVIEWED_PREFIX_SIZE &&
    usedTitleKeys.size === REVIEWED_PREFIX_SIZE,
  'Reviewed prefix titles and excerpts must be unique ignoring case and punctuation.',
)

const loadedSources = []
const filterCounts = {}
const candidateExcerptKeys = new Set(usedExcerptKeys)
const candidateComparableExcerptKeys = new Set(usedComparableExcerptKeys)
const incrementFilter = (reason) => {
  filterCounts[reason] = (filterCounts[reason] ?? 0) + 1
}

for (const source of sourceManifest.sources) {
  const loaded = await loadSourceCandidates(source, sourceDirectory)
  const sourceBytes = await readFile(path.join(sourceDirectory, source.fileName))
  const eligible = []
  const sourceExcerptKeys = new Set()

  for (const candidate of loaded.candidates) {
    if (usedCandidateIds.has(candidate.id)) {
      incrementFilter('reserved-reviewed-candidate')
      continue
    }
    const excerptKey = ordinalKey(candidate.text)
    if (candidateExcerptKeys.has(excerptKey) || sourceExcerptKeys.has(excerptKey)) {
      incrementFilter('duplicate-excerpt')
      continue
    }
    const comparableKey = comparableExcerptKey(candidate.text)
    if (candidateComparableExcerptKeys.has(comparableKey)) {
      incrementFilter('near-duplicate-excerpt')
      continue
    }
    const reason = filterReason(candidate, source)
    if (reason) {
      incrementFilter(reason)
      continue
    }
    const options = titleOptions(candidate.text)
    if (options.length === 0) {
      incrementFilter('no-valid-title')
      continue
    }
    sourceExcerptKeys.add(excerptKey)
    candidateExcerptKeys.add(excerptKey)
    candidateComparableExcerptKeys.add(comparableKey)
    eligible.push({ ...candidate, titleOptions: options })
  }

  loadedSources.push({
    source,
    sourceBytes: sourceBytes.length,
    rawCandidateCount: loaded.candidates.length,
    candidateById: new Map(
      loaded.candidates.map((candidate) => [candidate.id, candidate]),
    ),
    eligible,
    cursor: 0,
    selectedGeneratedCount: 0,
  })
}

const loadedSourceById = new Map(
  loadedSources.map((loaded) => [loaded.source.id, loaded]),
)
for (const entry of preview.entries) {
  const loaded = loadedSourceById.get(entry.provenance.sourceId)
  const candidates = entry.provenance.candidateIds.map((candidateId) => {
    const candidate = loaded?.candidateById.get(candidateId)
    invariant(
      candidate,
      `Reviewed entry ${entry.ordinal} references unknown candidate ${candidateId}.`,
    )
    return candidate
  })
  invariant(
    entry.excerpt === candidates.map((candidate) => candidate.text).join(' '),
    `Reviewed entry ${entry.ordinal} no longer matches normalized source text.`,
  )
  invariant(
    entry.provenance.sourceBlockLineStart ===
      Math.min(...candidates.map((candidate) => candidate.sourceBlockLineStart)) &&
      entry.provenance.sourceBlockLineEnd ===
        Math.max(...candidates.map((candidate) => candidate.sourceBlockLineEnd)),
    `Reviewed entry ${entry.ordinal} source lines changed.`,
  )
}

const canonicalEntries = preview.entries.map((entry) => {
  const source = sourceById.get(entry.provenance.sourceId)
  const tags = [...entry.tags]
  invariant(
    JSON.stringify(tags) === JSON.stringify([...tags].sort()),
    `Reviewed entry ${entry.ordinal} tags are not ordinal-sorted.`,
  )
  invariant(
    countCharacters(entry.excerpt) <= MAX_EXCERPT_CHARACTERS,
    `Reviewed entry ${entry.ordinal} exceeds the excerpt budget.`,
  )
  return {
    index: entry.ordinal,
    title: entry.title,
    excerpt: entry.excerpt,
    category: source.category,
    tags,
    sourceId: source.id,
    section: entry.provenance.section,
    candidateIds: [...entry.provenance.candidateIds],
    sourceBlockLineStart: entry.provenance.sourceBlockLineStart,
    sourceBlockLineEnd: entry.provenance.sourceBlockLineEnd,
    sampleClass: entry.sampleClass,
    titleMethod: 'curated-review',
  }
})

let sourceCursor = 0
while (canonicalEntries.length < CORPUS_SIZE) {
  let selected = null

  for (let attempts = 0; attempts < loadedSources.length; attempts += 1) {
    const loaded = loadedSources[sourceCursor]
    sourceCursor = (sourceCursor + 1) % loadedSources.length

    while (loaded.cursor < loaded.eligible.length) {
      const candidate = loaded.eligible[loaded.cursor]
      loaded.cursor += 1
      const option = candidate.titleOptions.find(
        ({ title }) => !usedTitleKeys.has(ordinalKey(title)),
      )
      if (!option) {
        incrementFilter('title-options-exhausted')
        continue
      }

      usedTitleKeys.add(ordinalKey(option.title))
      usedExcerptKeys.add(ordinalKey(candidate.text))
      loaded.selectedGeneratedCount += 1
      selected = { loaded, candidate, option }
      break
    }

    if (selected) break
  }

  invariant(
    selected,
    `Candidate pool exhausted after ${canonicalEntries.length} entries.`,
  )

  const { source } = selected.loaded
  const { candidate, option } = selected
  const tags = [
    `author:${slugify(source.author)}`,
    `corpus:${sourceManifest.corpusId}`,
    'public-domain',
    'sample:generated',
    `section:${candidate.section}`,
    'stress',
    `work:${source.id}`,
  ].sort()
  canonicalEntries.push({
    index: canonicalEntries.length,
    title: option.title,
    excerpt: candidate.text,
    category: source.category,
    tags,
    sourceId: source.id,
    section: candidate.section,
    candidateIds: [candidate.id],
    sourceBlockLineStart: candidate.sourceBlockLineStart,
    sourceBlockLineEnd: candidate.sourceBlockLineEnd,
    sampleClass: 'generated',
    titleMethod: option.mode,
  })
}

const runtimeEntries = canonicalEntries.map(
  ({ index, title, excerpt, category, tags }) => ({
    index,
    title,
    excerpt,
    category,
    tags,
  }),
)
const runtime = {
  schemaVersion: 1,
  corpusId: sourceManifest.corpusId,
  entryCount: CORPUS_SIZE,
  entries: runtimeEntries,
}
const runtimeOutput = json(runtime)

const shardOutputs = []
for (let shardIndex = 0; shardIndex < CORPUS_SIZE / SHARD_SIZE; shardIndex += 1) {
  const indexStart = shardIndex * SHARD_SIZE
  const indexEnd = indexStart + SHARD_SIZE - 1
  const shardPath = path.join(
    fullDirectory,
    `corpus-${String(indexStart).padStart(5, '0')}-${String(indexEnd).padStart(5, '0')}.json`,
  )
  const shard = {
    schemaVersion: 1,
    corpusId: sourceManifest.corpusId,
    shardIndex,
    indexStart,
    indexEnd,
    entryCount: SHARD_SIZE,
    entries: canonicalEntries.slice(indexStart, indexEnd + 1),
  }
  shardOutputs.push({ path: shardPath, content: json(shard), shard })
}

const selectedBySource = countBy(canonicalEntries.map((entry) => entry.sourceId))
const titleMethodCounts = countBy(canonicalEntries.map((entry) => entry.titleMethod))
const candidateCapacity = loadedSources.reduce(
  (sum, loaded) => sum + loaded.eligible.length,
  0,
)
const manifest = {
  schemaVersion: 1,
  corpusId: sourceManifest.corpusId,
  entryCount: CORPUS_SIZE,
  shardSize: SHARD_SIZE,
  reviewedPrefixSize: REVIEWED_PREFIX_SIZE,
  constraints: {
    titleWords: { minimum: 3, maximum: 6 },
    titleCharactersMaximum: 35,
    excerptSentences: { minimum: 1, maximum: 2 },
    excerptCharactersMaximum: MAX_EXCERPT_CHARACTERS,
    runtimeBodyCharactersMaximum: 250,
    runtimePrefixWorstCase: 'Objection 99999 — ',
    titleUniqueness: 'case-insensitive',
    excerptUniqueness: 'case-insensitive',
  },
  generation: {
    script: portablePath(fileURLToPath(import.meta.url)),
    nodeVersion: process.versions.node,
    icuVersion: process.versions.icu,
    ordering:
      'Frozen reviewed prefix followed by source-order round-robin over eligible candidates.',
    generatedTitleMethod:
      'Highest-scoring globally unique 3–6-word ordered source keywords; a natural contiguous source phrase is used when it scores higher.',
    hundredThousandReuse: 'runtime entry at nodeIndex % 10000',
  },
  sources: {
    manifestPath: portablePath(sourceManifestPath),
    manifestSha256: sha256(sourceManifestBytes),
    sourceCount: sourceManifest.sources.length,
    entries: loadedSources.map((loaded) => ({
      id: loaded.source.id,
      ebookNumber: loaded.source.ebookNumber,
      fileName: loaded.source.fileName,
      sourceSha256: loaded.source.sha256,
      sourceBytes: loaded.sourceBytes,
      rawCandidates: loaded.rawCandidateCount,
      eligibleCandidates: loaded.eligible.length,
      selectedEntries: selectedBySource[loaded.source.id] ?? 0,
    })),
  },
  filtering: {
    policy:
      'Light mechanical filtering removes overt slurs, particularly graphic wording, source artifacts, incomplete boundaries, and unusable titles. The frozen reviewed prefix is preserved unchanged.',
    eligibleCandidateCapacity: candidateCapacity,
    generatedEntriesSelected: CORPUS_SIZE - REVIEWED_PREFIX_SIZE,
    eligibleCandidatesNotSelected:
      candidateCapacity - (CORPUS_SIZE - REVIEWED_PREFIX_SIZE),
    rejectedCandidatesByReason: sortedRecord(filterCounts),
  },
  statistics: {
    entriesBySource: selectedBySource,
    titleMethods: titleMethodCounts,
    titleCharacters: {
      minimum: Math.min(...runtimeEntries.map((entry) => countCharacters(entry.title))),
      maximum: Math.max(...runtimeEntries.map((entry) => countCharacters(entry.title))),
    },
    excerptCharacters: {
      minimum: Math.min(...runtimeEntries.map((entry) => countCharacters(entry.excerpt))),
      maximum: Math.max(...runtimeEntries.map((entry) => countCharacters(entry.excerpt))),
      under100: runtimeEntries.filter((entry) => countCharacters(entry.excerpt) < 100).length,
    },
    uniqueTitleWords: new Set(
      runtimeEntries.flatMap((entry) =>
        wordTokens(entry.title).map((word) => word.toLocaleLowerCase('en-US')),
      ),
    ).size,
  },
  fingerprints: {
    method: 'SHA-256 of JSON.stringify(ordered runtime entry array prefix)',
    reviewedContent: {
      method: 'SHA-256 of JSON.stringify([{index,title,excerpt}, ...])',
      count: REVIEWED_PREFIX_SIZE,
      sha256: EXPECTED_REVIEWED_CONTENT_FINGERPRINT,
    },
    first1000: sha256(JSON.stringify(runtimeEntries.slice(0, 1_000))),
    first10000: sha256(JSON.stringify(runtimeEntries)),
  },
  shards: shardOutputs.map(({ path: shardPath, content, shard }) => ({
    path: portablePath(shardPath),
    indexStart: shard.indexStart,
    indexEnd: shard.indexEnd,
    entryCount: shard.entryCount,
    bytes: Buffer.byteLength(content),
    sha256: sha256(content),
  })),
  runtime: {
    path: portablePath(runtimePath),
    bytes: Buffer.byteLength(runtimeOutput),
    sha256: sha256(runtimeOutput),
  },
}
const manifestOutput = json(manifest)

const outputs = [
  ...shardOutputs.map(({ path: outputPath, content }) => [outputPath, content]),
  [runtimePath, runtimeOutput],
  [corpusManifestPath, manifestOutput],
]

if (checkOnly) {
  for (const [outputPath, expected] of outputs) {
    const actual = await readFile(outputPath, 'utf8')
    invariant(actual === expected, `${portablePath(outputPath)} is stale.`)
  }
  process.stdout.write(
    `Verified deterministic ${CORPUS_SIZE}-entry corpus (${candidateCapacity} eligible candidates).\n`,
  )
} else {
  for (const [outputPath, content] of outputs) {
    await mkdir(path.dirname(outputPath), { recursive: true })
    await writeFile(outputPath, content)
  }
  process.stdout.write(
    `Generated ${CORPUS_SIZE} entries from ${candidateCapacity} eligible candidates.\n`,
  )
}
