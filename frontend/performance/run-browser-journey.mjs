#!/usr/bin/env node

import { createRequire } from 'node:module'
import { once } from 'node:events'
import { performance } from 'node:perf_hooks'
import { readFileSync } from 'node:fs'
import path from 'node:path'
import process from 'node:process'
import readline from 'node:readline'

const frontendDirectory = path.resolve(readOption('--frontend-dir') ?? process.cwd())
const requireFromFrontend = createRequire(path.join(frontendDirectory, 'package.json'))
const { chromium } = requireFromFrontend('playwright')
const playwrightVersion = requireFromFrontend('playwright/package.json').version
const graphMapVersion = JSON.parse(readFileSync(
  path.join(frontendDirectory, 'node_modules', '@logiclikely', 'graphmap', 'package.json'),
  'utf8',
)).version

let sequence = 0

function readOption(name) {
  const index = process.argv.indexOf(name)
  return index >= 0 ? process.argv[index + 1] : undefined
}

async function writeFrame(frame) {
  const line = `${JSON.stringify({ ...frame, sequence: sequence++ })}\n`
  if (!process.stdout.write(line)) {
    await once(process.stdout, 'drain')
  }
}

function environment(browserVersion) {
  return {
    nodeVersion: process.version,
    browserVersion,
    playwrightVersion,
    graphMapVersion,
  }
}

function failureTerminal(request, code, message, exceptionType, status = 'failed') {
  return {
    version: 'phase-4-browser-v1',
    scenarioId: request.scenarioId,
    runId: request.runId,
    sampleId: request.sampleId,
    status,
    actualNodeCount: null,
    actualEdgeCount: null,
    renderedNodeCount: null,
    renderedEdgeCount: null,
    matchCount: null,
    requiredAncestorUnionCount: null,
    requiredAncestorNodeIds: null,
    matchNodeIds: null,
    totalResultCardinality: null,
    boundedResultItemCount: null,
    requestBytes: null,
    responseBytes: null,
    responsePayloadSha256: null,
    identityLimitation: null,
    driverPayload: null,
    failure: { code, message, exceptionType },
    unexpectedConsoleErrors: [],
    pageErrors: [],
    exactSuppressions: [],
    evidence: null,
    environment: null,
  }
}

function harnessUrl(request) {
  const url = new URL(request.harnessUrl)
  if (url.pathname === '/' || url.pathname === '') {
    url.pathname = '/iframe.html'
  }
  url.searchParams.set('id', 'performance-insightsbrowserharness--journey')
  url.searchParams.set('viewMode', 'story')
  url.searchParams.set('benchmarkApiBaseUrl', request.apiBaseUrl)
  url.searchParams.set('benchmarkScenarioId', request.scenarioId)
  url.searchParams.set('benchmarkGraphSlug', request.graphSlug)
  url.searchParams.set('benchmarkAction', request.action)
  url.searchParams.set('benchmarkOperationId', request.operationKey)
  url.searchParams.set('benchmarkRunId', request.runId)
  url.searchParams.set('benchmarkSampleId', request.sampleId)
  if (request.searchQuery != null) {
    url.searchParams.set('benchmarkSearchQuery', request.searchQuery)
  }
  return url.toString()
}

async function emitTerminal(request, terminal) {
  await writeFrame({
    eventKind: 'terminal',
    runId: request.runId,
    sampleId: request.sampleId,
    terminal,
  })
}

async function probe(request) {
  let browser
  try {
    browser = await chromium.launch({ headless: true })
    const browserEnvironment = environment(browser.version())
    await emitTerminal(request, {
      ...failureTerminal(request, 'unused', 'unused'),
      status: 'succeeded',
      failure: null,
      environment: browserEnvironment,
    })
  } catch (error) {
    await emitTerminal(request, failureTerminal(
      request,
      'browser-probe-failed',
      'The Playwright Chromium environment probe failed.',
      error?.constructor?.name,
    ))
  } finally {
    await browser?.close()
  }
}

async function journey(request) {
  let browser
  const phases = []
  const consoleErrors = []
  const pageErrors = []
  let browserEnvironment = null
  let terminalResult = null
  let finalizing = false
  let phaseWriteChain = Promise.resolve()
  let terminalResolve
  let fatalResolve
  const terminalSignal = new Promise((resolve) => { terminalResolve = resolve })
  const fatalSignal = new Promise((resolve) => { fatalResolve = resolve })

  try {
    browser = await chromium.launch({ headless: true })
    browserEnvironment = environment(browser.version())
    const context = await browser.newContext()
    await context.setExtraHTTPHeaders({
      'X-Insights-Run-Id': request.runId,
      'X-Insights-Sample-Id': request.sampleId,
    })
    const page = await context.newPage()

    page.on('console', (message) => {
      if (message.type() !== 'error') return
      const text = message.text()
      consoleErrors.push(text)
      finalizing = true
      fatalResolve({ code: 'browser-console-error', message: text })
    })
    page.on('pageerror', (error) => {
      const text = error?.message ?? String(error)
      pageErrors.push(text)
      finalizing = true
      fatalResolve({
        code: 'browser-page-error',
        message: text,
        exceptionType: error?.constructor?.name,
      })
    })

    await page.exposeBinding('__logiclikelyEmitBenchmarkPhase', async (_, phase) => {
      if (finalizing || terminalResult != null || phase == null || typeof phase !== 'object') return
      phaseWriteChain = phaseWriteChain.then(async () => {
        phases.push(phase)
        await writeFrame({
          eventKind: 'phase',
          runId: request.runId,
          sampleId: request.sampleId,
          phase,
        })
      })
      await phaseWriteChain
    })
    await page.exposeBinding('__logiclikelyEmitBenchmarkTerminal', (_, terminal) => {
      if (finalizing || terminalResult != null) return
      terminalResult = terminal
      terminalResolve(terminal)
    })
    await page.addInitScript((input) => {
      globalThis.__logiclikelyInsightsBenchmarkInput = input
      let phaseBindingChain = Promise.resolve()
      globalThis.addEventListener('logiclikely:insights-benchmark-phase', (event) => {
        phaseBindingChain = phaseBindingChain.then(() =>
          globalThis.__logiclikelyEmitBenchmarkPhase(event.detail))
      })
      globalThis.addEventListener('logiclikely:insights-benchmark-complete', (event) => {
        void phaseBindingChain.then(() =>
          globalThis.__logiclikelyEmitBenchmarkTerminal(event.detail))
      })
    }, {
      runId: request.runId,
      sampleId: request.sampleId,
      scenarioId: request.scenarioId,
      operationId: request.operationKey,
      resultPayload: request.resultPayload,
    })

    const actionStarted = performance.now()
    await page.goto(harnessUrl(request), {
      waitUntil: 'domcontentloaded',
      timeout: request.timeoutMilliseconds,
    })
    let timeoutHandle
    const internalTimeout = new Promise((resolve) => {
      const delay = Math.max(1, request.timeoutMilliseconds - 100)
      timeoutHandle = setTimeout(() => resolve({ code: 'browser-harness-timeout', message: 'The browser harness did not signal completion.' }), delay)
      timeoutHandle.unref()
    })
    const completed = await Promise.race([
      terminalSignal.then((terminal) => ({ terminal })),
      fatalSignal.then((failure) => ({ failure })),
      internalTimeout.then((failure) => ({ failure, timedOut: true })),
    ])
    finalizing = true
    clearTimeout(timeoutHandle)

    if (completed.terminal != null) {
      await phaseWriteChain
      const actionCompleted = performance.now()
      const searchStatus = completed.terminal?.evidence?.searchStatus ?? null
      const endToEndPhase = {
        layer: 'end-to-end',
        phase: 'action-to-stable-result-and-view',
        durationMilliseconds: actionCompleted - actionStarted,
        timingBoundaryProvenance: 'externally-observed',
        source: 'playwright-completion-event-observation',
        evidence: {
          startMilliseconds: actionStarted,
          endMilliseconds: actionCompleted,
          completionEvent: 'logiclikely:insights-benchmark-complete',
          terminalState: completed.terminal.status,
          stableSelector: completed.terminal?.evidence?.stableSelector ?? null,
          searchStatus,
        },
      }
      phases.push(endToEndPhase)
      await writeFrame({
        eventKind: 'phase',
        runId: request.runId,
        sampleId: request.sampleId,
        phase: endToEndPhase,
      })
      const terminal = {
        ...completed.terminal,
        version: completed.terminal.version ?? 'phase-4-browser-v1',
        scenarioId: completed.terminal.scenarioId ?? request.scenarioId,
        runId: completed.terminal.runId ?? request.runId,
        sampleId: completed.terminal.sampleId ?? request.sampleId,
        driverPayload: null,
        unexpectedConsoleErrors: consoleErrors,
        pageErrors,
        exactSuppressions: [],
        environment: browserEnvironment,
      }
      if (consoleErrors.length > 0 || pageErrors.length > 0) {
        terminal.status = 'failed'
        terminal.failure = {
          code: 'browser-page-error',
          message: 'The journey observed an unexpected page or console error.',
        }
      }
      await emitTerminal(request, terminal)
      return
    }

    await phaseWriteChain
    const terminal = failureTerminal(
      request,
      completed.failure.code,
      completed.failure.message,
      completed.failure.exceptionType,
      completed.timedOut ? 'timed-out' : 'failed',
    )
    terminal.unexpectedConsoleErrors = consoleErrors
    terminal.pageErrors = pageErrors
    terminal.environment = browserEnvironment
    await emitTerminal(request, terminal)
  } catch (error) {
    finalizing = true
    await phaseWriteChain
    const terminal = failureTerminal(
      request,
      'browser-driver-failed',
      'The Playwright journey failed before the harness reached a stable terminal view.',
      error?.constructor?.name,
    )
    terminal.unexpectedConsoleErrors = consoleErrors
    terminal.pageErrors = pageErrors
    terminal.environment = browserEnvironment
    await emitTerminal(request, terminal)
  } finally {
    await browser?.close()
  }
}

const input = readline.createInterface({ input: process.stdin, crlfDelay: Infinity })
const iterator = input[Symbol.asyncIterator]()
const first = await iterator.next()
if (first.done) {
  process.stderr.write('Browser driver requires one JSON request line.\n')
  process.exitCode = 2
} else {
  let request
  try {
    request = JSON.parse(first.value)
    if (request.mode === 'probe') {
      await probe(request)
    } else if (request.mode === 'journey') {
      await journey(request)
    } else {
      throw new Error(`Unknown browser driver mode '${request.mode}'.`)
    }
  } catch (error) {
    if (request?.runId != null && request?.sampleId != null) {
      await emitTerminal(request, failureTerminal(
        request,
        'browser-driver-input-invalid',
        'The browser driver request was invalid.',
        error?.constructor?.name,
      ))
    } else {
      process.stderr.write('Browser driver request was invalid.\n')
      process.exitCode = 2
    }
  } finally {
    input.close()
  }
}
