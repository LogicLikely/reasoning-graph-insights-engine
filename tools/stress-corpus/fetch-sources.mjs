#!/usr/bin/env node

import { mkdir, readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'
import { sha256 } from './corpus-lib.mjs'

const here = path.dirname(fileURLToPath(import.meta.url))
const destination = process.argv[2]
const manifestFlag = process.argv.indexOf('--manifest')
if (manifestFlag >= 0 && !process.argv[manifestFlag + 1]) {
  throw new Error('--manifest requires a path.')
}
const manifestPath =
  manifestFlag >= 0
    ? path.resolve(process.cwd(), process.argv[manifestFlag + 1])
    : path.join(here, 'corpus-sources.v1.json')
const manifest = JSON.parse(
  await readFile(manifestPath, 'utf8'),
)

if (!destination) {
  throw new Error(
    'Usage: node tools/stress-corpus/fetch-sources.mjs DESTINATION [--manifest MANIFEST]',
  )
}

await mkdir(destination, { recursive: true })

for (const source of manifest.sources) {
  const response = await fetch(source.plainTextUrl)
  if (!response.ok) {
    throw new Error(`${source.id}: download failed with HTTP ${response.status}.`)
  }
  const bytes = Buffer.from(await response.arrayBuffer())
  const digest = sha256(bytes)
  if (digest !== source.sha256) {
    throw new Error(
      `${source.id}: upstream bytes changed; expected ${source.sha256}, received ${digest}.`,
    )
  }
  await writeFile(path.join(destination, source.fileName), bytes)
  process.stdout.write(`${source.id} · ${bytes.length} bytes · ${digest}\n`)
}
