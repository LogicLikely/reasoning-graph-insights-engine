import { createHash } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import path from 'node:path'

const segmenter = new Intl.Segmenter('en', { granularity: 'sentence' })

const segmentEndsAbbreviation = (value) =>
  /(?:\b(?:Mr|Mrs|Ms|Dr|Prof|Rev|St|Jr|Sr|Messrs|Plin|viz)\.|(?:\b[A-HJ-Z]\.){1,4}|\b[A-Z]{2,4}\.)[”"')\]]?$/u.test(
    value,
  )

export const countCharacters = (value) => [...value].length

export const sha256 = (value) =>
  createHash('sha256').update(value).digest('hex')

export const slugify = (value) =>
  value
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '')

export const normalizeSourceText = (value) =>
  value
    .normalize('NFC')
    .replace(/[_*]/g, '')
    .replace(/\[Pg [^\]]+\]/gi, ' ')
    .replace(/\[\d{4}[a-z]\]/gi, ' ')
    .replace(/\[(?:Footnote )?\d+\]/gi, ' ')
    .replace(/--/g, '—')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/^([A-Z]{3,})(?=[\s,;:.!?])/, (word) =>
      /^[IVXLC]+$/.test(word)
        ? word
        : `${word[0]}${word.slice(1).toLowerCase()}`,
    )

const headingPatterns = [
  /^(?:BOOK|CHAPTER)\s+[IVXLC\d]+\.?(?:\s.*)?$/i,
  /^(?:OF|ON)\s+[A-Z][A-Z\s,;'’\-]+\.?$/,
  /^Of [A-Z][A-Za-z'’\- ]+$/,
  /^[IVXLC]+\.$/,
]

const isHeading = (text) =>
  text.length <= 100 && headingPatterns.some((pattern) => pattern.test(text))

const sectionForHeading = (heading, state) => {
  if (/^BOOK\s+/i.test(heading)) {
    state.book = slugify(heading)
    state.chapter = null
  } else if (/^CHAPTER\s+/i.test(heading)) {
    state.chapter = slugify(heading)
  } else {
    state.book = null
    state.chapter = slugify(heading.replace(/^Of /, ''))
  }

  return [state.book, state.chapter].filter(Boolean).join('-')
}

const toBlocks = (lines, firstLineNumber) => {
  const blocks = []
  let blockLines = []
  let blockStart = firstLineNumber

  const flush = (endLine) => {
    if (blockLines.length === 0) return
    blocks.push({
      raw: blockLines.join('\n'),
      startLine: blockStart,
      endLine,
    })
    blockLines = []
  }

  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index]
    const lineNumber = firstLineNumber + index
    if (line.trim().length === 0) {
      flush(lineNumber - 1)
      blockStart = lineNumber + 1
    } else {
      if (blockLines.length === 0) blockStart = lineNumber
      blockLines.push(line)
    }
  }
  flush(firstLineNumber + lines.length - 1)
  return blocks
}

export async function loadSourceCandidates(source, sourceDirectory) {
  const sourcePath = path.join(sourceDirectory, source.fileName)
  const raw = await readFile(sourcePath, 'utf8')
  const digest = sha256(raw)
  if (digest !== source.sha256) {
    throw new Error(`${source.id}: expected ${source.sha256}, received ${digest}`)
  }

  const allLines = raw.replace(/\r\n?/g, '\n').split('\n')
  const lines = allLines.slice(source.contentLines.start - 1, source.contentLines.end)
  const blocks = toBlocks(lines, source.contentLines.start)
  const headingState = { book: null, chapter: null }
  let section = source.defaultSection
  let paragraph = 0
  const candidates = []

  for (const block of blocks) {
    const blockText = normalizeSourceText(block.raw)
    if (/^\s*\[(?:Footnote\s+)?\d+(?:\s*:|\])/i.test(block.raw)) {
      if (blockText) paragraph += 1
      continue
    }
    if (!blockText) continue
    if (isHeading(blockText)) {
      section = sectionForHeading(blockText, headingState) || source.defaultSection
      continue
    }

    const sentences = [...segmenter.segment(blockText)].map(({ segment }) =>
      normalizeSourceText(segment),
    )
    for (let sentenceIndex = 0; sentenceIndex < sentences.length; sentenceIndex += 1) {
      const text = sentences[sentenceIndex]
      const words = text.split(/\s+/).length
      if (
        countCharacters(text) >= 24 &&
        countCharacters(text) <= 215 &&
        words >= 4 &&
        words <= 34 &&
        /[.!?][”’"')\]]?$/.test(text) &&
        !/^\d+[.)]?\s/.test(text) &&
        !/PROJECT GUTENBERG|www\.|https?:|ebook/i.test(text)
      ) {
        candidates.push({
          id: `${source.id}:${paragraph}:${sentenceIndex}`,
          sourceId: source.id,
          section,
          paragraph,
          sentenceIndex,
          sourceBlockLineStart: block.startLine,
          sourceBlockLineEnd: block.endLine,
          chars: countCharacters(text),
          words,
          text,
          precededByAbbreviationBoundary:
            sentenceIndex > 0 &&
            segmentEndsAbbreviation(sentences[sentenceIndex - 1]),
        })
      }
    }
    paragraph += 1
  }

  return { source, candidates }
}
