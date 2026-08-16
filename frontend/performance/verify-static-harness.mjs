#!/usr/bin/env node

import { createServer } from 'node:http'
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import process from 'node:process'
import { chromium } from 'playwright'

const staticRoot = path.resolve('storybook-static')
const storyId = 'performance-insightsbrowserharness--journey'
const contentTypes = new Map([
  ['.css', 'text/css; charset=utf-8'],
  ['.html', 'text/html; charset=utf-8'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8'],
  ['.svg', 'image/svg+xml'],
])

const server = createServer(async (request, response) => {
  try {
    const requestPath = decodeURIComponent(new URL(request.url ?? '/', 'http://127.0.0.1').pathname)
    const relativePath = requestPath === '/' ? 'index.html' : requestPath.slice(1)
    const filePath = path.resolve(staticRoot, relativePath)
    if (!filePath.startsWith(`${staticRoot}${path.sep}`) && filePath !== staticRoot) {
      response.writeHead(403).end()
      return
    }
    const body = await readFile(filePath)
    response.writeHead(200, {
      'Content-Type': contentTypes.get(path.extname(filePath)) ?? 'application/octet-stream',
    })
    response.end(body)
  } catch {
    response.writeHead(404).end()
  }
})

await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve))
const address = server.address()
if (!address || typeof address === 'string') {
  throw new Error('Static Storybook verification server did not bind an HTTP port.')
}

const browser = await chromium.launch({ headless: true })
const failures = []

async function runJourney({
  id,
  action,
  searchQuery = null,
  resultPayload = null,
  sampleId,
}) {
  const context = await browser.newContext()
  const page = await context.newPage()
  const browserErrors = []
  page.on('console', (message) => {
    if (message.type() === 'error') browserErrors.push(`console: ${message.text()}`)
  })
  page.on('pageerror', (error) => browserErrors.push(`page: ${error.message}`))
  await page.addInitScript((input) => {
    globalThis.__logiclikelyInsightsBenchmarkInput = input
  }, {
    runId: '11111111-1111-1111-1111-111111111111',
    sampleId,
    scenarioId: `static-fixture-${id}`,
    action,
    searchQuery,
    resultPayload,
  })

  const url = new URL(`http://127.0.0.1:${address.port}/iframe.html`)
  url.searchParams.set('id', storyId)
  url.searchParams.set('viewMode', 'story')
  url.searchParams.set('benchmarkAction', action)
  if (searchQuery) url.searchParams.set('benchmarkSearchQuery', searchQuery)
  await page.goto(url.href, { waitUntil: 'domcontentloaded' })
  await page.waitForFunction(() => (
    globalThis.__logiclikelyInsightsBenchmark?.state === 'completed'
  ), null, { timeout: 30_000 })
  const result = await page.evaluate(() => globalThis.__logiclikelyInsightsBenchmark?.result)

  if (result?.status !== 'succeeded') {
    failures.push(`${id}: terminal status was ${result?.status ?? 'missing'}`)
  }
  if (result?.evidence?.harnessBuildIdentity !== 'storybook-production-profiling') {
    failures.push(
      `${id}: expected storybook-production-profiling harness identity, observed ` +
      `${result?.evidence?.harnessBuildIdentity ?? 'missing'}`,
    )
  }
  const reactLayer = action === 'result-render' ? 'lab-result' : 'graph-map'
  const reactCommit = result?.phases?.find(({ layer, phase }) => (
    layer === reactLayer && phase === 'react-commit'
  ))
  if (!reactCommit) {
    failures.push(`${id}: ${reactLayer}/react-commit was not emitted by static profiling ReactDOM`)
  } else if (reactCommit.evidence?.harnessBuildIdentity !==
      'storybook-production-profiling') {
    failures.push(`${id}: React commit did not retain the production profiling identity`)
  } else if (action !== 'result-render' && (
    reactCommit.evidence?.journeyAction !== action ||
    reactCommit.evidence?.selection !==
      'last-profiler-commit-from-action-start-through-stable-view' ||
    !(reactCommit.evidence?.commitTimeMilliseconds >=
      reactCommit.evidence?.actionStartMilliseconds)
  )) {
    failures.push(`${id}: React commit evidence was not selected from the requested action window`)
  }
  const requiredPhases = action === 'result-render'
    ? [
        ['lab-result', 'react-commit', 'directly-instrumented'],
        ['lab-result', 'result-render', 'directly-instrumented'],
      ]
    : [
        ['browser-data', 'graph-map-adapter', 'directly-instrumented'],
        ['graph-map', 'react-commit', 'directly-instrumented'],
        ['graph-map', 'node-edge-materialization', 'externally-observed'],
        ['graph-map', 'viewport-fit', 'estimated'],
        ...(action === 'search'
          ? [['browser-data', 'search-completion', 'externally-observed']]
          : []),
      ]
  for (const [layer, phase, provenance] of requiredPhases) {
    const observed = result?.phases?.find((item) => (
      item.layer === layer && item.phase === phase
    ))
    if (!observed || observed.timingBoundaryProvenance !== provenance) {
      failures.push(`${id}: required ${layer}/${phase} provenance ${provenance} was not emitted`)
    }
  }

  if (id === 'compact-search') {
    const searchPhase = result?.phases?.find(({ layer, phase }) => (
      layer === 'browser-data' && phase === 'search-completion'
    ))
    if (!searchPhase || result.matchCount !== 1 || result.requiredAncestorUnionCount !== 3 ||
        result.totalResultCardinality !== 1 ||
        searchPhase.evidence?.totalResultCardinality !== 1) {
      failures.push(
        `${id}: expected match/required-union/cardinality 1/3/1, observed ` +
        `${result?.matchCount ?? 'missing'}/${result?.requiredAncestorUnionCount ?? 'missing'}/` +
        `${result?.totalResultCardinality ?? 'missing'}`,
      )
    }
    if (result?.requiredAncestorNodeIds?.length !== 3) {
      failures.push(`${id}: complete visible required-node union IDs were not retained`)
    }
  }
  if (id === 'no-hit-search') {
    const searchPhase = result?.phases?.find(({ layer, phase }) => (
      layer === 'browser-data' && phase === 'search-completion'
    ))
    if (result?.matchCount !== 0 || result?.requiredAncestorUnionCount !== 0 ||
        result?.totalResultCardinality !== 0 ||
        searchPhase?.evidence?.totalResultCardinality !== 0) {
      failures.push(
        `${id}: expected match/required-union/cardinality 0/0/0, observed ` +
        `${result?.matchCount ?? 'missing'}/${result?.requiredAncestorUnionCount ?? 'missing'}/` +
        `${result?.totalResultCardinality ?? 'missing'}`,
      )
    }
    if (!Array.isArray(result?.requiredAncestorNodeIds) ||
        result.requiredAncestorNodeIds.length !== 0) {
      failures.push(`${id}: required-node union IDs must be an empty complete set`)
    }
  }
  if (action === 'full-expansion') {
    if (result?.actualNodeCount !== 10 || result?.actualEdgeCount !== 9 ||
        result?.renderedNodeCount !== result?.actualNodeCount ||
        result?.renderedEdgeCount !== result?.actualEdgeCount) {
      failures.push(
        `${id}: expected complete fixture counts 10/9, observed actual ` +
        `${result?.actualNodeCount ?? 'missing'}/${result?.actualEdgeCount ?? 'missing'} and rendered ` +
        `${result?.renderedNodeCount ?? 'missing'}/${result?.renderedEdgeCount ?? 'missing'}`,
      )
    }
  }
  if (action === 'result-render') {
    const presentation = await page.evaluate(() => {
      const resultRoot = document.querySelector('[data-testid="bounded-analysis-result"]')
      const cellLengths = [...(resultRoot?.querySelectorAll('tbody td') ?? [])]
        .map((cell) => cell.textContent?.length ?? 0)
      return {
        rowCount: resultRoot?.querySelectorAll('tbody tr').length ?? 0,
        pathCount: resultRoot?.querySelectorAll('.insights-browser-result__paths > li').length ?? 0,
        itemDisclosure: document.querySelector('[data-testid="bounded-result-item-count"]')
          ?.textContent?.trim(),
        pathDisclosure: document.querySelector('[data-testid="bounded-result-path-count"]')
          ?.textContent?.trim(),
        maxCellTextLength: Math.max(0, ...cellLengths),
        bodyText: document.body.textContent ?? '',
      }
    })
    if (result?.totalResultCardinality !== 125 ||
        result?.boundedResultItemCount !== 100 ||
        presentation.rowCount !== 100 || presentation.pathCount !== 20 ||
        presentation.itemDisclosure !== 'Rendering 100 of 125 result items.' ||
        presentation.pathDisclosure !== 'Rendering 20 of 25 ordered paths.' ||
        presentation.maxCellTextLength > 512 ||
        !presentation.bodyText.includes('sha256:static-bounded-complete') ||
        !presentation.bodyText.includes('more items') ||
        !presentation.bodyText.includes('more nodes')) {
      failures.push(
        `${id}: bounded result DOM did not preserve identity and enforce every render cap: ` +
        JSON.stringify({
          terminalCardinality: result?.totalResultCardinality,
          terminalBoundedItems: result?.boundedResultItemCount,
          rowCount: presentation.rowCount,
          pathCount: presentation.pathCount,
          itemDisclosure: presentation.itemDisclosure,
          pathDisclosure: presentation.pathDisclosure,
          maxCellTextLength: presentation.maxCellTextLength,
          hasDigest: presentation.bodyText.includes('sha256:static-bounded-complete'),
          hasStructuredOmission: presentation.bodyText.includes('more items'),
          hasPathOmission: presentation.bodyText.includes('more nodes'),
        }),
      )
    }
  }
  failures.push(...browserErrors.map((error) => `${id}: ${error}`))
  await context.close()
  return {
    id,
    action,
    status: result?.status,
    phases: result?.phases?.map(({ layer, phase }) => `${layer}/${phase}`) ?? [],
    matchCount: result?.matchCount,
    requiredAncestorUnionCount: result?.requiredAncestorUnionCount,
    totalResultCardinality: result?.totalResultCardinality,
  }
}

try {
  const results = [
    await runJourney({
      id: 'collapsed',
      action: 'collapsed',
      sampleId: '22222222-2222-2222-2222-222222222222',
    }),
    await runJourney({
      id: 'full-expansion',
      action: 'full-expansion',
      sampleId: '33333333-3333-3333-3333-333333333333',
    }),
    await runJourney({
      id: 'compact-search',
      action: 'search',
      searchQuery: 'Photographs',
      sampleId: '44444444-4444-4444-4444-444444444444',
    }),
    await runJourney({
      id: 'no-hit-search',
      action: 'search',
      searchQuery: 'definitely-no-matching-node',
      sampleId: '55555555-5555-5555-5555-555555555555',
    }),
    await runJourney({
      id: 'bounded-result-render',
      action: 'result-render',
      sampleId: '66666666-6666-6666-6666-666666666666',
      resultPayload: {
        operationId: 'strongest-path-v1',
        status: 'succeeded',
        title: 'Static bounded result verification',
        totalResultCardinality: 125,
        resultDigest: 'sha256:static-bounded-complete',
        summary: { completePathCount: 125 },
        distribution: {
          nodeIds: Array.from({ length: 1_000 }, (_, index) => `distribution-${index}`),
        },
        items: Array.from({ length: 125 }, (_, index) => ({
          rank: index + 1,
          terminalNodeId: `n-${String(index).padStart(5, '0')}`,
          pathNodeIds: index === 0
            ? Array.from({ length: 1_000 }, (__, pathIndex) => `cell-${pathIndex}`)
            : [`n-${index}`, `n-${index + 1}`],
        })),
        orderedPaths: Array.from({ length: 25 }, (_, index) => ({
          pathId: `path-${index}`,
          nodeIds: Array.from({ length: 130 }, (__, nodeIndex) => (
            `path-${index}-node-${nodeIndex}`
          )),
          score: 1 - index / 100,
        })),
      },
    }),
  ]
  if (failures.length > 0) {
    throw new Error(failures.join('\n'))
  }
  process.stdout.write(`${JSON.stringify({ staticStorybookProfiling: 'passed', results })}\n`)
} finally {
  await browser.close()
  await new Promise((resolve, reject) => server.close((error) => (
    error ? reject(error) : resolve()
  )))
}
