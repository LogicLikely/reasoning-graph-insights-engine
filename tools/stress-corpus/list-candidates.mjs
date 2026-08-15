#!/usr/bin/env node

import { readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'
import { loadSourceCandidates } from './corpus-lib.mjs'

const here = path.dirname(fileURLToPath(import.meta.url))
const manifest = JSON.parse(
  await readFile(path.join(here, 'corpus-sources.v1.json'), 'utf8'),
)
const sourceDirectory = process.argv[2]
const outputPath = process.argv[3]

if (!sourceDirectory) {
  throw new Error('Usage: node tools/stress-corpus/list-candidates.mjs SOURCE_DIRECTORY')
}

const result = []

for (const source of manifest.sources) {
  const { candidates } = await loadSourceCandidates(source, sourceDirectory)
  result.push({ source: source.id, candidates })
}

const rendered = `${JSON.stringify(result, null, 2)}\n`
if (outputPath) {
  await writeFile(outputPath, rendered)
} else {
  process.stdout.write(rendered)
}
