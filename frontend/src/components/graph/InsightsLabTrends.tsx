import { useId, useMemo, useRef, useState } from 'react'
import type { BenchmarkSet, PerformanceRunRecord } from '../../services/performanceRuns'
import { isStressGraphId } from '../../services/stressGraphs'
import './InsightsLabTrends.css'

export interface InsightsLabTrendsProps {
  runs: readonly PerformanceRunRecord[]
  benchmarkSets: readonly BenchmarkSet[]
}

type TrendMode = 'solvers' | 'within' | 'across'
type TrendMetricId = 'compute' | 'operation' | 'cpu' | 'allocations' | 'subsets'
type YAxisScale = 'linear' | 'logarithmic'
type CounterSetSolverKind = 'greedy' | 'exhaustive'

type TrendMetricDefinition = {
  id: TrendMetricId
  label: string
  axisLabel: string
  unitKind: 'milliseconds' | 'bytes' | 'count'
  boundedOnly?: boolean
  explanation: string
  significance: string
  readValue: (run: PerformanceRunRecord) => number | null | undefined
}

type TrendRun = {
  runNumber: number
  algorithmId: string
  algorithmLabel: string
  bounded: boolean
  benchmarkSetId: string
  benchmarkSetName: string
  shapeId: string
  shapeLabel: string
  nodeCount: number
  metricValues: Partial<Record<TrendMetricId, number>>
  solverKind?: CounterSetSolverKind
  timedOut: boolean
  source: PerformanceRunRecord
}

type SelectedMetricTrendRun = TrendRun & {
  metricValue: number
}

type TrendPoint = {
  id: string
  benchmarkSetId: string
  benchmarkSetName: string
  shapeLabel: string
  nodeCount: number
  metricValue: number
  sampleCount: number
  runNumbers: number[]
  timedOut: boolean
  timeBudgetMilliseconds?: number
  sourceRuns: TrendRun[]
}

type TrendSeries = {
  id: string
  label: string
  points: TrendPoint[]
}

type MetricPresentation = {
  axisLabel: string
  divisor: number
  formatValue: (value: number) => string
  formatAxisValue: (value: number) => string
}

const MAX_WITHIN_SHAPES = 4
const TIME_BOUNDED_EXHAUSTIVE_IMPLEMENTATION = 'time-bounded-exhaustive'
const LEGACY_BOUNDED_IMPLEMENTATIONS = new Set(['bounded-brute-force', 'exact-bounded'])
const CHART_WIDTH = 920
const CHART_HEIGHT = 360
const PLOT_LEFT = 82
const PLOT_RIGHT = 28
const PLOT_TOP = 22
const PLOT_BOTTOM = 58

const TREND_METRICS: readonly TrendMetricDefinition[] = [
  {
    id: 'compute',
    label: 'Compute time',
    axisLabel: 'Compute time',
    unitKind: 'milliseconds',
    explanation: 'Wall-clock time spent running the algorithm after its graph is loaded.',
    significance: 'Lower values mean the selected algorithm completed its compute work faster.',
    readValue: (run) => (
      run.timing?.computeElapsedMilliseconds ?? run.timing?.computeElapsedMs
    ),
  },
  {
    id: 'operation',
    label: 'Total operation time',
    axisLabel: 'Total operation time',
    unitKind: 'milliseconds',
    explanation: 'Wall-clock backend time for graph loading, computation, and any operation persistence.',
    significance: 'Lower values mean the complete backend operation finished faster.',
    readValue: (run) => (
      run.timing?.operationElapsedMilliseconds ?? run.timing?.operationElapsedMs
    ),
  },
  {
    id: 'cpu',
    label: 'CPU time',
    axisLabel: 'CPU time',
    unitKind: 'milliseconds',
    explanation: 'Process-wide CPU time consumed while the algorithm compute scope was measured.',
    significance: 'Lower values generally mean less processor work, but other backend activity can affect this measurement and multicore CPU time can exceed wall-clock time.',
    readValue: (run) => run.resources?.cpuTimeMilliseconds,
  },
  {
    id: 'allocations',
    label: 'Managed allocations',
    axisLabel: 'Managed allocations',
    unitKind: 'bytes',
    explanation: 'Managed bytes allocated on the compute thread while the algorithm ran.',
    significance: 'Lower values mean less allocation pressure. This is allocation traffic, not retained or peak memory.',
    readValue: (run) => run.resources?.allocatedBytes,
  },
  {
    id: 'subsets',
    label: 'Subset evaluations',
    axisLabel: 'Subset evaluations',
    unitKind: 'count',
    boundedOnly: true,
    explanation: 'The number of candidate subsets the time-bounded exhaustive reference evaluated during its search.',
    significance: 'Lower counts mean less combinatorial search work. A count of one usually means the empty set was evaluated before any candidate nodes were added.',
    readValue: (run) => {
      const value = run.details?.subsetEvaluations
      return typeof value === 'number' ? value : undefined
    },
  },
] as const

const SERIES_COLORS = [
  'var(--insights-trend-series-1)',
  'var(--insights-trend-series-2)',
  'var(--insights-trend-series-3)',
  'var(--insights-trend-series-4)',
  'var(--insights-trend-series-5)',
  'var(--insights-trend-series-6)',
] as const

const SERIES_DASHES = [undefined, '9 5', '2 5', '11 4 2 4', '5 4', '12 4 3 4 3 4'] as const

const SHAPE_LABELS: Record<string, string> = {
  balanced: 'Balanced tree',
  wide: 'Wide star',
  deep: 'Deep chain',
  'shared-diamond': 'Shared-diamond DAG',
}

const SHAPE_ORDER = ['balanced', 'wide', 'deep', 'shared-diamond'] as const

function sentenceCase(value: string): string {
  const spaced = value.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[-_]/g, ' ')
  return `${spaced.charAt(0).toUpperCase()}${spaced.slice(1)}`
}

function getAlgorithmId(run: PerformanceRunRecord): string | undefined {
  const name = run.algorithm?.name?.trim()
  if (!name) return undefined
  return JSON.stringify([name, run.algorithm?.implementation?.trim() ?? ''])
}

function getAlgorithmLabel(run: PerformanceRunRecord): string {
  const name = run.algorithm?.name ?? 'unknown'
  const implementation = run.algorithm?.implementation

  if (name === 'minimal-counter-set') {
    if (implementation === TIME_BOUNDED_EXHAUSTIVE_IMPLEMENTATION) {
      return 'Time-bounded exhaustive search'
    }
    if (implementation && LEGACY_BOUNDED_IMPLEMENTATIONS.has(implementation)) {
      return 'Legacy bounded minimal counter set'
    }
    return 'Minimal counter set'
  }

  const knownLabel = ({
    'evidence-impact-ranking': 'Evidence impact ranking',
    'least-robust-node': 'Least robust node',
    'robustness-ranking': 'Robustness ranking',
    'leaf-update': 'Leaf update',
  } as Record<string, string>)[name]

  if (knownLabel) return knownLabel
  const baseLabel = sentenceCase(name)
  return implementation && implementation !== 'current'
    ? `${baseLabel} (${sentenceCase(implementation)})`
    : baseLabel
}

function isBoundedMinimalCounterSet(run: PerformanceRunRecord): boolean {
  return run.algorithm?.name === 'minimal-counter-set'
    && run.algorithm?.implementation === TIME_BOUNDED_EXHAUSTIVE_IMPLEMENTATION
}

function getCounterSetSolverKind(run: PerformanceRunRecord): CounterSetSolverKind | undefined {
  if (run.algorithm?.name !== 'minimal-counter-set') return undefined
  if (isBoundedMinimalCounterSet(run)) return 'exhaustive'
  return run.algorithm?.implementation === 'greedy' ? 'greedy' : undefined
}

function getShapeId(run: PerformanceRunRecord): string | undefined {
  const explicitType = run.graph?.type?.trim()
  if (explicitType) return explicitType

  const slug = run.graph?.slug
  if (!slug) return undefined
  return slug.replace(/^stress-/, '').replace(/-(?:100|1k|10k|100k)$/i, '')
}

function getShapeLabel(shapeId: string): string {
  return SHAPE_LABELS[shapeId] ?? sentenceCase(shapeId)
}

function getBenchmarkSetLabel(
  benchmarkSet: BenchmarkSet,
  benchmarkSets: readonly BenchmarkSet[],
): string {
  const normalizedName = benchmarkSet.name.trim().toLowerCase()
  const hasDuplicateName = benchmarkSets.some((candidate) => (
    candidate.id !== benchmarkSet.id
    && candidate.name.trim().toLowerCase() === normalizedName
  ))
  return hasDuplicateName
    ? `${benchmarkSet.name} · ${benchmarkSet.id.slice(-6)}`
    : benchmarkSet.name
}

function isIncludedOutcome(run: PerformanceRunRecord): boolean {
  const status = run.outcome?.status?.replace(/[-_\s]/g, '').toLowerCase()
  return status === 'completed' || status === 'notproven' || status === 'timedout'
}

function isTimedOutOutcome(run: PerformanceRunRecord): boolean {
  const status = run.outcome?.status?.replace(/[-_\s]/g, '').toLowerCase()
  const stopReason = String(run.details?.stopReason ?? '').replace(/[-_\s]/g, '').toLowerCase()
  return status === 'timedout' || stopReason === 'timebudget' || stopReason === 'timelimit'
}

function readNumber(source: Record<string, unknown> | undefined, ...keys: string[]): number | undefined {
  for (const key of keys) {
    const value = source?.[key]
    if (typeof value === 'number' && Number.isFinite(value)) return value
    if (typeof value === 'string' && value.trim() !== '') {
      const parsed = Number(value)
      if (Number.isFinite(parsed)) return parsed
    }
  }
  return undefined
}

function readString(source: Record<string, unknown> | undefined, ...keys: string[]): string | undefined {
  for (const key of keys) {
    const value = source?.[key]
    if (typeof value === 'string' && value.trim() !== '') return value
    if (typeof value === 'number' && Number.isFinite(value)) return String(value)
  }
  return undefined
}

function getTimeBudgetMilliseconds(run: PerformanceRunRecord): number | undefined {
  return readNumber(
    run.invocation?.parameters,
    'timeBudgetMilliseconds',
    'timeBudgetMs',
    'timeoutMilliseconds',
    'timeoutMs',
  ) ?? readNumber(
    run.details,
    'timeBudgetMilliseconds',
    'timeBudgetMs',
    'timeoutMilliseconds',
    'timeoutMs',
  )
}

function compareShapes(left: string, right: string): number {
  const leftIndex = SHAPE_ORDER.indexOf(left as (typeof SHAPE_ORDER)[number])
  const rightIndex = SHAPE_ORDER.indexOf(right as (typeof SHAPE_ORDER)[number])
  if (leftIndex >= 0 && rightIndex >= 0) return leftIndex - rightIndex
  if (leftIndex >= 0) return -1
  if (rightIndex >= 0) return 1
  return getShapeLabel(left).localeCompare(getShapeLabel(right))
}

function median(values: readonly number[]): number {
  const ordered = [...values].sort((left, right) => left - right)
  const midpoint = Math.floor(ordered.length / 2)
  return ordered.length % 2 === 0
    ? (ordered[midpoint - 1] + ordered[midpoint]) / 2
    : ordered[midpoint]
}

function formatNodeCount(value: number): string {
  return value.toLocaleString()
}

function formatDurationLimit(milliseconds: number): string {
  const totalSeconds = Math.max(0, Math.round(milliseconds / 1000))
  if (totalSeconds < 60) return `${totalSeconds}s`
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  return `${minutes}:${String(seconds).padStart(2, '0')}`
}

function formatStoppedMetricValue(
  point: TrendPoint,
  metric: TrendMetricDefinition,
  presentation: MetricPresentation,
): string {
  if (metric.id === 'compute') {
    return `${formatDurationLimit(point.metricValue)} · stopped`
  }
  return `${presentation.formatValue(point.metricValue)} · at stop`
}

function formatAxisNumber(value: number): string {
  const absoluteValue = Math.abs(value)
  return value.toLocaleString(undefined, {
    maximumFractionDigits: absoluteValue > 0 && absoluteValue < 0.01
      ? 4
      : absoluteValue < 1
        ? 3
        : absoluteValue < 10
          ? 2
          : 0,
  })
}

function niceMaximum(value: number): number {
  if (!Number.isFinite(value) || value <= 0) return 1
  const exponent = 10 ** Math.floor(Math.log10(value))
  const normalized = value / exponent
  const niceNormalized = normalized <= 1
    ? 1
    : normalized <= 2
      ? 2
      : normalized <= 5
        ? 5
        : 10
  return niceNormalized * exponent
}

function validMetricValue(value: number | null | undefined): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0
}

function metricDefinition(metricId: TrendMetricId): TrendMetricDefinition {
  return TREND_METRICS.find(({ id }) => id === metricId) ?? TREND_METRICS[0]
}

function metricPresentation(
  metric: TrendMetricDefinition,
  series: readonly TrendSeries[],
): MetricPresentation {
  const rawMaximum = Math.max(
    ...series.flatMap(({ points }) => points.map(({ metricValue }) => metricValue)),
    0,
  )

  if (metric.unitKind === 'milliseconds') {
    return {
      axisLabel: `${metric.axisLabel} (ms)`,
      divisor: 1,
      formatValue: (value) => `${value.toLocaleString(undefined, { maximumFractionDigits: 2 })} ms`,
      formatAxisValue: formatAxisNumber,
    }
  }

  if (metric.unitKind === 'count') {
    return {
      axisLabel: `${metric.axisLabel} (count)`,
      divisor: 1,
      formatValue: (value) => (
        `${value.toLocaleString(undefined, { maximumFractionDigits: 2 })} ${value === 1 ? 'evaluation' : 'evaluations'}`
      ),
      formatAxisValue: formatAxisNumber,
    }
  }

  const byteUnits = [
    { divisor: 1024 ** 3, label: 'GiB' },
    { divisor: 1024 ** 2, label: 'MiB' },
    { divisor: 1024, label: 'KiB' },
  ] as const
  const unit = byteUnits.find(({ divisor }) => rawMaximum >= divisor)
    ?? { divisor: 1, label: 'bytes' }
  const formatBytes = (value: number) => (
    `${formatAxisNumber(value / unit.divisor)} ${unit.label}`
  )

  return {
    axisLabel: `${metric.axisLabel} (${unit.label})`,
    divisor: unit.divisor,
    formatValue: formatBytes,
    formatAxisValue: formatAxisNumber,
  }
}

function downsampleTicks(values: readonly number[], maximumCount = 5): number[] {
  if (values.length <= maximumCount) return [...values]
  const lastIndex = values.length - 1
  return Array.from({ length: maximumCount }, (_, index) => (
    values[Math.round((lastIndex * index) / (maximumCount - 1))]
  )).filter((value, index, selected) => selected.indexOf(value) === index)
}

function markerShape(index: number): 'circle' | 'square' | 'diamond' | 'triangle' {
  return (['circle', 'square', 'diamond', 'triangle'] as const)[index % 4]
}

interface SeriesMarkerProps {
  index: number
  x: number
  y: number
  size?: number
}

function SeriesMarker({ index, x, y, size = 5 }: SeriesMarkerProps) {
  const color = SERIES_COLORS[index % SERIES_COLORS.length]
  const common = {
    fill: 'var(--insights-trend-point-fill)',
    stroke: color,
    strokeWidth: 2.5,
    vectorEffect: 'non-scaling-stroke' as const,
  }

  switch (markerShape(index)) {
    case 'square':
      return <rect {...common} height={size * 2} width={size * 2} x={x - size} y={y - size} />
    case 'diamond':
      return (
        <polygon
          {...common}
          points={`${x},${y - size - 1} ${x + size + 1},${y} ${x},${y + size + 1} ${x - size - 1},${y}`}
        />
      )
    case 'triangle':
      return (
        <polygon
          {...common}
          points={`${x},${y - size - 1} ${x + size + 1},${y + size} ${x - size - 1},${y + size}`}
        />
      )
    default:
      return <circle {...common} cx={x} cy={y} r={size} />
  }
}

function CensoredMarker({ index, x, y, size = 5 }: SeriesMarkerProps) {
  const color = SERIES_COLORS[index % SERIES_COLORS.length]
  const arrowTop = y - size - 10
  return (
    <g className="insights-lab-trends__censored-marker">
      <circle
        cx={x}
        cy={y}
        fill="var(--insights-trend-point-fill)"
        r={size + 1}
        stroke={color}
        strokeDasharray="2 2"
        strokeWidth="2.5"
        vectorEffect="non-scaling-stroke"
      />
      <line
        stroke={color}
        strokeWidth="2.5"
        vectorEffect="non-scaling-stroke"
        x1={x}
        x2={x}
        y1={y - size}
        y2={arrowTop}
      />
      <polyline
        fill="none"
        points={`${x - 4},${arrowTop + 4} ${x},${arrowTop} ${x + 4},${arrowTop + 4}`}
        stroke={color}
        strokeLinecap="round"
        strokeLinejoin="round"
        strokeWidth="2.5"
        vectorEffect="non-scaling-stroke"
      />
    </g>
  )
}

function StoppedMarker({ index, x, y, size = 5 }: SeriesMarkerProps) {
  const color = SERIES_COLORS[index % SERIES_COLORS.length]
  return (
    <g className="insights-lab-trends__stopped-marker">
      <circle
        cx={x}
        cy={y}
        fill="var(--insights-trend-point-fill)"
        r={size + 1}
        stroke={color}
        strokeWidth="2.5"
        vectorEffect="non-scaling-stroke"
      />
      <line stroke={color} strokeWidth="2" x1={x - 3} x2={x + 3} y1={y - 3} y2={y + 3} />
      <line stroke={color} strokeWidth="2" x1={x + 3} x2={x - 3} y1={y - 3} y2={y + 3} />
    </g>
  )
}

function rawPoint(run: SelectedMetricTrendRun): TrendPoint {
  return {
    id: `run-${run.runNumber}`,
    benchmarkSetId: run.benchmarkSetId,
    benchmarkSetName: run.benchmarkSetName,
    shapeLabel: run.shapeLabel,
    nodeCount: run.nodeCount,
    metricValue: run.metricValue,
    sampleCount: 1,
    runNumbers: [run.runNumber],
    timedOut: run.timedOut,
    timeBudgetMilliseconds: getTimeBudgetMilliseconds(run.source),
    sourceRuns: [run],
  }
}

function aggregateSeries(
  runs: readonly SelectedMetricTrendRun[],
  seriesDefinitions: readonly { id: string; label: string }[],
  seriesIdForRun: (run: SelectedMetricTrendRun) => string,
): TrendSeries[] {
  return seriesDefinitions.flatMap((definition) => {
    const grouped = new Map<number, SelectedMetricTrendRun[]>()
    for (const run of runs) {
      if (seriesIdForRun(run) !== definition.id) continue
      const current = grouped.get(run.nodeCount) ?? []
      current.push(run)
      grouped.set(run.nodeCount, current)
    }

    const points = [...grouped.entries()]
      .sort(([left], [right]) => left - right)
      .flatMap(([nodeCount, matches]) => {
        const completedMatches = matches.filter((match) => !match.timedOut)
        const timedOutMatches = matches.filter((match) => match.timedOut)
        const completedPoint: TrendPoint[] = completedMatches.length === 0
          ? []
          : [{
              id: `${definition.id}-${nodeCount}-completed`,
              benchmarkSetId: completedMatches[0].benchmarkSetId,
              benchmarkSetName: completedMatches[0].benchmarkSetName,
              shapeLabel: completedMatches[0].shapeLabel,
              nodeCount,
              metricValue: median(completedMatches.map(({ metricValue }) => metricValue)),
              sampleCount: completedMatches.length,
              runNumbers: completedMatches
                .map(({ runNumber }) => runNumber)
                .sort((left, right) => left - right),
              timedOut: false,
              sourceRuns: completedMatches,
            }]

        // A stopped observation is a lower bound, not another completed sample.
        // Keep each one raw so it can never disappear into a median.
        return [...completedPoint, ...timedOutMatches.map(rawPoint)]
      })

    return points.length > 0 ? [{ ...definition, points }] : []
  })
}

function rawSolverSeries(runs: readonly SelectedMetricTrendRun[]): TrendSeries[] {
  const definitions: readonly { id: CounterSetSolverKind; label: string }[] = [
    { id: 'greedy', label: 'Greedy' },
    { id: 'exhaustive', label: 'Time-bounded exhaustive' },
  ]

  return definitions.flatMap((definition) => {
    const points = runs
      .filter((run) => run.solverKind === definition.id)
      .sort((left, right) => left.nodeCount - right.nodeCount || left.runNumber - right.runNumber)
      .map(rawPoint)
    return points.length > 0 ? [{ ...definition, points }] : []
  })
}

function parseWholeNumber(value: unknown): bigint | undefined {
  if (typeof value === 'bigint' && value >= 0n) return value
  if (typeof value === 'number' && Number.isSafeInteger(value) && value >= 0) {
    return BigInt(value)
  }
  if (typeof value !== 'string') return undefined
  const normalized = value.trim().replaceAll(',', '')
  return /^\d+$/.test(normalized) ? BigInt(normalized) : undefined
}

function wholeSubsetSpace(run: TrendRun): bigint | undefined {
  const explicit = parseWholeNumber(
    readString(run.source.details, 'totalPossibleSubsets', 'maximumSubsetCount', 'subsetSpaceSize'),
  )
  if (explicit !== undefined) return explicit

  const candidateCount = readNumber(
    run.source.details,
    'totalCandidateCount',
    'candidateCountTotal',
    'candidateCount',
  )
  return candidateCount !== undefined
    && Number.isSafeInteger(candidateCount)
    && candidateCount >= 0
    && candidateCount <= 1_000_000
    ? 1n << BigInt(candidateCount)
    : undefined
}

function approximateLog10(value: bigint): number {
  if (value <= 0n) return Number.NEGATIVE_INFINITY
  const digits = value.toString()
  const leadingDigits = digits.slice(0, Math.min(15, digits.length))
  return Math.log10(Number(leadingDigits)) + digits.length - leadingDigits.length
}

function formatMaximumSubsetSpacePercent(run: TrendRun): string {
  const evaluated = parseWholeNumber(
    readString(run.source.details, 'subsetEvaluations', 'subsetsEvaluated'),
  )
  const maximum = wholeSubsetSpace(run)
  if (evaluated === undefined || maximum === undefined || maximum === 0n) return '—'
  if (evaluated === 0n) return '0%'
  if (evaluated >= maximum) return '100%'

  const millionthsOfPercent = (evaluated * 100_000_000n) / maximum
  if (millionthsOfPercent > 0n) {
    const percent = Number(millionthsOfPercent) / 1_000_000
    return `${percent.toLocaleString(undefined, { maximumFractionDigits: 6 })}%`
  }

  const exponent = Math.floor(approximateLog10(evaluated) - approximateLog10(maximum) + 2)
  const mantissa = 10 ** (
    approximateLog10(evaluated) - approximateLog10(maximum) + 2 - exponent
  )
  return `${mantissa.toFixed(2)}e${exponent}%`
}

function formatIntegerDetail(value: number | undefined): string {
  return value === undefined ? '—' : Math.trunc(value).toLocaleString()
}

function formatWholeNumberDetail(value: string): string {
  const parsed = parseWholeNumber(value)
  if (parsed === undefined) return value
  const digits = parsed.toString()
  if (digits.length <= 18) return parsed.toLocaleString()
  return `${digits[0]}.${digits.slice(1, 6)}e+${digits.length - 1}`
}

function solverProofLabel(run: TrendRun): string {
  if (run.solverKind === 'greedy') {
    if (run.source.details?.thresholdReached === true) {
      return 'Usable set found; minimum not proven'
    }
    if (run.source.details?.thresholdReached === false) {
      return 'No qualifying set found'
    }
    return 'No minimum proof attempted'
  }
  if (run.timedOut) return 'Not established (stopped)'
  const proofStatus = String(
    run.source.outcome?.proofStatus
      ?? run.source.details?.proofStatus
      ?? '',
  ).replace(/[-_]/g, ' ').trim()
  if (proofStatus.toLowerCase() === 'proven') {
    return run.source.details?.thresholdReached === false
      ? 'No qualifying set exists (proven)'
      : 'Minimum size proven'
  }
  return proofStatus ? sentenceCase(proofStatus) : 'See run details'
}

function solverFrontierLabel(run: TrendRun): string {
  if (run.solverKind !== 'exhaustive') return '—'
  const details = run.source.details
  const largestFinished = readNumber(
    details,
    'largestCardinalityFullyExhausted',
    'largestFullyExhaustedCardinality',
  )
  const active = readNumber(details, 'activeCardinality', 'currentCardinality')
  const evaluatedAtActive = readNumber(
    details,
    'subsetEvaluationsAtActiveCardinality',
    'subsetsEvaluatedAtActiveCardinality',
  )
  const activeTotal = readString(
    details,
    'totalSubsetsAtActiveCardinality',
    'subsetsAtActiveCardinality',
  )
  const parts: string[] = []
  if (largestFinished !== undefined) {
    parts.push(`fully exhausted through size ${Math.trunc(largestFinished).toLocaleString()}`)
  }
  if (active !== undefined) {
    const activeProgress = evaluatedAtActive === undefined
      ? `working on size ${Math.trunc(active).toLocaleString()}`
      : `size ${Math.trunc(active).toLocaleString()}: ${formatIntegerDetail(evaluatedAtActive)}${activeTotal ? ` of ${formatWholeNumberDetail(activeTotal)}` : ''}`
    parts.push(activeProgress)
  }
  if (parts.length === 0 && run.timedOut) {
    const stage = readString(details, 'timeoutStage', 'stoppedStage')
    return stage ? `stopped during ${sentenceCase(stage).toLowerCase()}` : 'stopped before a frontier was recorded'
  }
  return parts.length > 0 ? parts.join('; ') : '—'
}

interface TrendChartProps {
  idPrefix: string
  title: string
  description: string
  series: readonly TrendSeries[]
  metric: TrendMetricDefinition
  presentation: MetricPresentation
  yAxisScale: YAxisScale
}

function TrendChart({
  idPrefix,
  title,
  description,
  series,
  metric,
  presentation,
  yAxisScale,
}: TrendChartProps) {
  const points = series.flatMap(({ points: seriesPoints }) => seriesPoints)
  const nodeCounts = [...new Set(points.map(({ nodeCount }) => nodeCount))].sort((left, right) => left - right)
  const plotWidth = CHART_WIDTH - PLOT_LEFT - PLOT_RIGHT
  const plotHeight = CHART_HEIGHT - PLOT_TOP - PLOT_BOTTOM
  const minimumLog = Math.log10(Math.max(nodeCounts[0] ?? 1, 1))
  const maximumLog = Math.log10(Math.max(nodeCounts[nodeCounts.length - 1] ?? 1, 1))
  const timeBudgetValues = metric.id === 'compute'
    ? [...new Set(points.flatMap((point) => (
        point.timedOut && validMetricValue(point.timeBudgetMilliseconds)
          ? [point.timeBudgetMilliseconds]
          : []
      )))]
    : []
  const displayedValues = [
    ...points.map(({ metricValue }) => metricValue / presentation.divisor),
    ...timeBudgetValues.map((value) => value / presentation.divisor),
  ]
  const rawMaximum = Math.max(...displayedValues, 0)
  const linearMaximum = niceMaximum(rawMaximum * 1.08)
  const linearTicks = Array.from({ length: 5 }, (_, index) => (linearMaximum * index) / 4)
  const positiveValues = displayedValues.filter((value) => value > 0)
  const hasZeroValue = displayedValues.some((value) => value === 0)
  const allValuesAreZero = displayedValues.length > 0 && positiveValues.length === 0
  const minimumPositive = Math.min(...positiveValues)
  const maximumPositive = Math.max(...positiveValues)
  const positiveLogMinimum = Number.isFinite(minimumPositive) ? Math.log10(minimumPositive) : 0
  const positiveLogMaximum = Number.isFinite(maximumPositive) ? Math.log10(maximumPositive) : 0
  const positiveLogSpan = positiveLogMaximum - positiveLogMinimum
  const logarithmicMinimum = allValuesAreZero
    ? 0
    : hasZeroValue
      ? positiveLogMinimum - 1
    : positiveLogSpan === 0
      ? positiveLogMinimum - 0.5
      : positiveLogMinimum - positiveLogSpan * 0.05
  const logarithmicMaximum = allValuesAreZero
    ? 1
    : positiveLogSpan === 0
      ? positiveLogMaximum + 0.5
      : positiveLogMaximum + positiveLogSpan * 0.05
  const logarithmicPowerTicks = !allValuesAreZero && Number.isFinite(minimumPositive)
    ? Array.from(
        {
          length: Math.max(
            0,
            Math.floor(logarithmicMaximum) - Math.ceil(logarithmicMinimum) + 1,
          ),
        },
        (_, index) => 10 ** (Math.ceil(logarithmicMinimum) + index),
      )
    : []
  const sampledPowerTicks = downsampleTicks(
    logarithmicPowerTicks,
    hasZeroValue ? 4 : 5,
  )
  const positiveLogarithmicTicks = sampledPowerTicks.length >= 2
    ? sampledPowerTicks
    : [...new Set([
        minimumPositive,
        ...sampledPowerTicks,
        maximumPositive,
      ])].filter(Number.isFinite).sort((left, right) => left - right)
  const logarithmicTicks = [
    ...(hasZeroValue ? [{ value: 0, transformedValue: logarithmicMinimum }] : []),
    ...positiveLogarithmicTicks
      .map((value) => ({ value, transformedValue: Math.log10(value) })),
  ]
  const yTicks = yAxisScale === 'linear'
    ? linearTicks.map((value) => ({ value, transformedValue: value }))
    : logarithmicTicks

  const xForNodeCount = (nodeCount: number) => {
    if (minimumLog === maximumLog) return PLOT_LEFT + plotWidth / 2
    return PLOT_LEFT + ((Math.log10(Math.max(nodeCount, 1)) - minimumLog) / (maximumLog - minimumLog)) * plotWidth
  }
  const yForMetricValue = (rawValue: number) => {
    const value = rawValue / presentation.divisor
    if (yAxisScale === 'linear') {
      return PLOT_TOP + plotHeight - (value / linearMaximum) * plotHeight
    }
    const transformedValue = value === 0 ? logarithmicMinimum : Math.log10(value)
    const logarithmicSpan = logarithmicMaximum - logarithmicMinimum
    if (logarithmicSpan === 0) return PLOT_TOP + plotHeight / 2
    const normalizedValue = Math.min(
      1,
      Math.max(0, (transformedValue - logarithmicMinimum) / logarithmicSpan),
    )
    return PLOT_TOP + plotHeight - normalizedValue * plotHeight
  }
  const yForTick = (transformedValue: number) => {
    if (yAxisScale === 'linear') {
      return PLOT_TOP + plotHeight - (transformedValue / linearMaximum) * plotHeight
    }
    const logarithmicSpan = logarithmicMaximum - logarithmicMinimum
    if (logarithmicSpan === 0) return PLOT_TOP + plotHeight / 2
    const normalizedValue = Math.min(
      1,
      Math.max(0, (transformedValue - logarithmicMinimum) / logarithmicSpan),
    )
    return PLOT_TOP + plotHeight - normalizedValue * plotHeight
  }
  const yAxisScaleLabel = yAxisScale === 'linear' ? 'linear' : 'logarithmic'
  const firstStoppedSeriesIndex = Math.max(
    0,
    series.findIndex(({ points: seriesPoints }) => (
      seriesPoints.some(({ timedOut }) => timedOut)
    )),
  )

  return (
    <figure className="insights-lab-trends__figure">
      <figcaption>
        <strong>{title}</strong>
        <span>{description}</span>
        {yAxisScale === 'logarithmic' && allValuesAreZero ? (
          <span>All selected values are zero; logarithmic spacing is not applicable.</span>
        ) : null}
      </figcaption>
      <ul aria-label="Chart series" className="insights-lab-trends__legend">
        {series.map((item, index) => (
          <li key={item.id}>
            <svg aria-hidden="true" height="18" viewBox="0 0 34 18" width="34">
              <line
                stroke={SERIES_COLORS[index % SERIES_COLORS.length]}
                strokeDasharray={SERIES_DASHES[index % SERIES_DASHES.length]}
                strokeWidth="2.5"
                vectorEffect="non-scaling-stroke"
                x1="1"
                x2="33"
                y1="9"
                y2="9"
              />
              <SeriesMarker index={index} size={4} x={17} y={9} />
            </svg>
            <span>{item.label}</span>
          </li>
        ))}
        {points.some(({ timedOut }) => timedOut) ? (
          <li>
            <svg aria-hidden="true" height="22" viewBox="0 0 34 22" width="34">
              {metric.id === 'compute'
                ? <CensoredMarker index={firstStoppedSeriesIndex} size={4} x={17} y={15} />
                : <StoppedMarker index={firstStoppedSeriesIndex} size={4} x={17} y={11} />}
            </svg>
            <span>
              {metric.id === 'compute'
                ? 'Stopped before minimum was proven'
                : 'Partial value from a stopped run'}
            </span>
          </li>
        ) : null}
      </ul>
      <div
        aria-label="Trend chart"
        className="insights-lab-trends__chart-wrap"
        role="region"
        tabIndex={0}
      >
        <svg
          aria-labelledby={`${idPrefix}-chart-title ${idPrefix}-chart-description`}
          className="insights-lab-trends__chart"
          preserveAspectRatio="xMidYMid meet"
          role="img"
          viewBox={`0 0 ${CHART_WIDTH} ${CHART_HEIGHT}`}
        >
          <title id={`${idPrefix}-chart-title`}>{title}</title>
          <desc id={`${idPrefix}-chart-description`}>
            {description}. Points show {metric.label.toLowerCase()} by graph size on a {yAxisScaleLabel} Y-axis.
            {points.some(({ timedOut }) => timedOut)
              ? metric.id === 'compute'
                ? ' Arrow markers are stopped, right-censored observations and show only a lower bound.'
                : ' Crossed markers are partial measurements captured when a run stopped.'
              : ''}
            {yAxisScale === 'logarithmic' && allValuesAreZero
              ? ' All selected values are zero, so logarithmic spacing is not applicable.'
              : ''}
          </desc>

          {yTicks.map((tick) => {
            const y = yForTick(tick.transformedValue)
            return (
              <g key={`${tick.value}-${tick.transformedValue}`}>
                <line
                  className="insights-lab-trends__grid-line"
                  x1={PLOT_LEFT}
                  x2={CHART_WIDTH - PLOT_RIGHT}
                  y1={y}
                  y2={y}
                />
                <text className="insights-lab-trends__axis-tick" textAnchor="end" x={PLOT_LEFT - 12} y={y + 4}>
                  {presentation.formatAxisValue(tick.value)}
                </text>
              </g>
            )
          })}

          {nodeCounts.map((nodeCount) => {
            const x = xForNodeCount(nodeCount)
            return (
              <g key={nodeCount}>
                <line
                  className="insights-lab-trends__x-guide"
                  x1={x}
                  x2={x}
                  y1={PLOT_TOP}
                  y2={CHART_HEIGHT - PLOT_BOTTOM}
                />
                <text
                  className="insights-lab-trends__axis-tick"
                  textAnchor="middle"
                  x={x}
                  y={CHART_HEIGHT - PLOT_BOTTOM + 25}
                >
                  {formatNodeCount(nodeCount)}
                </text>
              </g>
            )
          })}


          {timeBudgetValues.map((budget) => {
            const y = yForMetricValue(budget)
            return (
              <g key={`budget-${budget}`}>
                <line
                  className="insights-lab-trends__budget-line"
                  x1={PLOT_LEFT}
                  x2={CHART_WIDTH - PLOT_RIGHT}
                  y1={y}
                  y2={y}
                />
                <text
                  className="insights-lab-trends__budget-label"
                  textAnchor="end"
                  x={CHART_WIDTH - PLOT_RIGHT - 5}
                  y={y - 6}
                >
                  {formatDurationLimit(budget)} time budget
                </text>
              </g>
            )
          })}

          <line
            className="insights-lab-trends__axis-line"
            x1={PLOT_LEFT}
            x2={PLOT_LEFT}
            y1={PLOT_TOP}
            y2={CHART_HEIGHT - PLOT_BOTTOM}
          />
          <line
            className="insights-lab-trends__axis-line"
            x1={PLOT_LEFT}
            x2={CHART_WIDTH - PLOT_RIGHT}
            y1={CHART_HEIGHT - PLOT_BOTTOM}
            y2={CHART_HEIGHT - PLOT_BOTTOM}
          />

          <text
            className="insights-lab-trends__axis-label"
            textAnchor="middle"
            transform={`rotate(-90 20 ${PLOT_TOP + plotHeight / 2})`}
            x="20"
            y={PLOT_TOP + plotHeight / 2}
          >
            {presentation.axisLabel}{yAxisScale === 'logarithmic' ? ' — logarithmic' : ''}
          </text>
          <text
            className="insights-lab-trends__axis-label"
            textAnchor="middle"
            x={PLOT_LEFT + plotWidth / 2}
            y={CHART_HEIGHT - 9}
          >
            Graph size (nodes, logarithmic)
          </text>

          {series.map((item, seriesIndex) => {
            const chartPoints = item.points.map((point) => ({
              ...point,
              x: xForNodeCount(point.nodeCount),
              y: yForMetricValue(point.metricValue),
            }))
            const color = SERIES_COLORS[seriesIndex % SERIES_COLORS.length]
            const duplicateNodeCounts = new Set(
              chartPoints
                .filter((point, index, allPoints) => (
                  allPoints.some((candidate, candidateIndex) => (
                    candidateIndex !== index && candidate.nodeCount === point.nodeCount
                  ))
                ))
                .map(({ nodeCount }) => nodeCount),
            )
            return (
              <g key={item.id}>
                {chartPoints.slice(1).map((point, pointIndex) => {
                  const previous = chartPoints[pointIndex]
                  if (
                    previous.timedOut
                    || point.timedOut
                    || duplicateNodeCounts.has(previous.nodeCount)
                    || duplicateNodeCounts.has(point.nodeCount)
                  ) return null
                  return (
                    <line
                      className="insights-lab-trends__series-line"
                      key={`${previous.id}-${point.id}`}
                      stroke={color}
                      strokeDasharray={SERIES_DASHES[seriesIndex % SERIES_DASHES.length]}
                      strokeWidth="2.5"
                      vectorEffect="non-scaling-stroke"
                      x1={previous.x}
                      x2={point.x}
                      y1={previous.y}
                      y2={point.y}
                    />
                  )
                })}
                {chartPoints.map((point) => (
                  <g key={point.id}>
                    <title>
                      {point.timedOut
                        ? `${item.label}, ${formatNodeCount(point.nodeCount)} nodes: ${formatStoppedMetricValue(point, metric, presentation)}; minimum not proven, run #${point.runNumbers[0]}`
                        : `${item.label}, ${formatNodeCount(point.nodeCount)} nodes: ${presentation.formatValue(point.metricValue)}${point.sampleCount > 1 ? ` median, n=${point.sampleCount}` : `, raw run #${point.runNumbers[0]}`}`}
                    </title>
                    {point.timedOut ? (
                      <>
                        {metric.id === 'compute'
                          ? <CensoredMarker index={seriesIndex} x={point.x} y={point.y} />
                          : <StoppedMarker index={seriesIndex} x={point.x} y={point.y} />}
                        <text
                          className="insights-lab-trends__stopped-label"
                          textAnchor={point.x > CHART_WIDTH - PLOT_RIGHT - 90 ? 'end' : 'start'}
                          x={point.x + (point.x > CHART_WIDTH - PLOT_RIGHT - 90 ? -9 : 9)}
                          y={point.y < PLOT_TOP + 32 ? point.y + 22 : point.y - 18}
                        >
                          {formatStoppedMetricValue(point, metric, presentation)}
                        </text>
                      </>
                    ) : (
                      <SeriesMarker index={seriesIndex} x={point.x} y={point.y} />
                    )}
                  </g>
                ))}
              </g>
            )
          })}
        </svg>
      </div>
    </figure>
  )
}

export function InsightsLabTrends({ runs, benchmarkSets }: InsightsLabTrendsProps) {
  const idPrefix = useId().replace(/:/g, '')
  const howToReadButtonRef = useRef<HTMLButtonElement>(null)
  const [mode, setMode] = useState<TrendMode>('solvers')
  const [requestedMetricId, setRequestedMetricId] = useState<TrendMetricId>('compute')
  const [yAxisScale, setYAxisScale] = useState<YAxisScale>('logarithmic')
  const [requestedAlgorithmId, setRequestedAlgorithmId] = useState('')
  const [requestedWithinSetId, setRequestedWithinSetId] = useState('')
  const [requestedWithinShapeIds, setRequestedWithinShapeIds] = useState<string[]>([])
  const [requestedAcrossShapeId, setRequestedAcrossShapeId] = useState('')
  const [requestedAcrossSetIds, setRequestedAcrossSetIds] = useState<string[]>([])
  const [requestedNodeCounts, setRequestedNodeCounts] = useState<number[]>([])
  const [showData, setShowData] = useState(false)
  const [showHowToRead, setShowHowToRead] = useState(false)

  const benchmarkSetsById = useMemo(
    () => new Map(benchmarkSets.map((benchmarkSet) => [benchmarkSet.id, benchmarkSet])),
    [benchmarkSets],
  )

  const trendRuns = useMemo<TrendRun[]>(() => runs.flatMap((run) => {
    const benchmarkSetId = run.benchmarkSetId
    const benchmarkSet = benchmarkSetId ? benchmarkSetsById.get(benchmarkSetId) : undefined
    const slug = run.graph?.slug
    const nodeCount = run.graph?.nodeCount
    const algorithmIdForRun = getAlgorithmId(run)
    const shapeId = getShapeId(run)
    const metricValues = TREND_METRICS.reduce<Partial<Record<TrendMetricId, number>>>(
      (values, metric) => {
        const value = metric.readValue(run)
        if (validMetricValue(value)) values[metric.id] = value
        return values
      },
      {},
    )

    if (
      !benchmarkSet
      || !slug
      || !isStressGraphId(slug)
      || !algorithmIdForRun
      || !shapeId
      || !isIncludedOutcome(run)
      || typeof nodeCount !== 'number'
      || !Number.isFinite(nodeCount)
      || nodeCount <= 0
      || Object.keys(metricValues).length === 0
    ) {
      return []
    }

    return [{
      runNumber: run.runNumber,
      algorithmId: algorithmIdForRun,
      algorithmLabel: getAlgorithmLabel(run),
      bounded: isBoundedMinimalCounterSet(run),
      benchmarkSetId: benchmarkSet.id,
      benchmarkSetName: getBenchmarkSetLabel(benchmarkSet, benchmarkSets),
      shapeId,
      shapeLabel: getShapeLabel(shapeId),
      nodeCount,
      metricValues,
      solverKind: getCounterSetSolverKind(run),
      timedOut: isTimedOutOutcome(run),
      source: run,
    }]
  }), [benchmarkSets, benchmarkSetsById, runs])

  const solverComparisonSetOptions = useMemo(() => benchmarkSets.filter((benchmarkSet) => {
    const matchingRuns = trendRuns.filter((run) => run.benchmarkSetId === benchmarkSet.id)
    const shapeIds = new Set(matchingRuns.map(({ shapeId }) => shapeId))
    return [...shapeIds].some((shapeId) => (
      matchingRuns.some((run) => run.shapeId === shapeId && run.solverKind === 'greedy')
      && matchingRuns.some((run) => run.shapeId === shapeId && run.solverKind === 'exhaustive')
    ))
  }), [benchmarkSets, trendRuns])

  const hasSolverComparison = solverComparisonSetOptions.length > 0
  const activeMode: TrendMode = mode === 'solvers' && !hasSolverComparison ? 'within' : mode

  const algorithmOptions = useMemo(() => {
    const options = new Map<string, { label: string; bounded: boolean }>()
    for (const run of trendRuns) {
      options.set(run.algorithmId, {
        label: run.algorithmLabel,
        bounded: run.bounded,
      })
    }
    return [...options.entries()]
      .map(([id, option]) => ({ id, ...option }))
      .sort((left, right) => left.label.localeCompare(right.label))
  }, [trendRuns])

  const algorithmId = algorithmOptions.some(({ id }) => id === requestedAlgorithmId)
    ? requestedAlgorithmId
    : algorithmOptions[0]?.id ?? ''

  const selectedAlgorithmOption = algorithmOptions.find(({ id }) => id === algorithmId)
  const selectedAlgorithmIsBounded = selectedAlgorithmOption?.bounded ?? false

  const selectedAlgorithmRuns = useMemo(
    () => trendRuns.filter((run) => run.algorithmId === algorithmId),
    [algorithmId, trendRuns],
  )

  const availableMetricIds = useMemo(() => new Set(
    TREND_METRICS
      .filter((candidateMetric) => (
        (!candidateMetric.boundedOnly || selectedAlgorithmIsBounded)
        && selectedAlgorithmRuns.some((run) => (
          validMetricValue(run.metricValues[candidateMetric.id])
        ))
      ))
      .map(({ id }) => id),
  ), [selectedAlgorithmIsBounded, selectedAlgorithmRuns])
  const availableMetrics = useMemo(() => TREND_METRICS.filter((candidateMetric) => (
    !candidateMetric.boundedOnly || selectedAlgorithmIsBounded
  )), [selectedAlgorithmIsBounded])
  const metricId = useMemo(() => (
    activeMode === 'solvers'
      ? 'compute'
      : availableMetricIds.has(requestedMetricId)
      ? requestedMetricId
      : availableMetrics.find(({ id }) => availableMetricIds.has(id))?.id ?? 'compute'
  ), [activeMode, availableMetricIds, availableMetrics, requestedMetricId])
  const metric = metricDefinition(metricId)

  const algorithmRuns = useMemo<SelectedMetricTrendRun[]>(() => (
    selectedAlgorithmRuns.flatMap((run) => {
      const metricValue = run.metricValues[metricId]
      return validMetricValue(metricValue) ? [{ ...run, metricValue }] : []
    })
  ), [metricId, selectedAlgorithmRuns])

  const solverComparisonSetId = solverComparisonSetOptions.some(({ id }) => id === requestedWithinSetId)
    ? requestedWithinSetId
    : solverComparisonSetOptions[0]?.id ?? ''

  const solverShapeOptions = useMemo(() => [...new Set(
    trendRuns
      .filter((run) => run.benchmarkSetId === solverComparisonSetId && run.solverKind)
      .map(({ shapeId }) => shapeId)
      .filter((shapeId) => {
        const matches = trendRuns.filter((run) => (
          run.benchmarkSetId === solverComparisonSetId && run.shapeId === shapeId
        ))
        return matches.some((run) => run.solverKind === 'greedy')
          && matches.some((run) => run.solverKind === 'exhaustive')
      }),
  )].sort(compareShapes), [solverComparisonSetId, trendRuns])

  const solverShapeId = solverShapeOptions.includes(requestedAcrossShapeId)
    ? requestedAcrossShapeId
    : solverShapeOptions[0] ?? ''

  const withinSetOptions = useMemo(() => benchmarkSets.filter((benchmarkSet) => (
    algorithmRuns.some((run) => run.benchmarkSetId === benchmarkSet.id)
  )), [algorithmRuns, benchmarkSets])

  const withinSetId = withinSetOptions.some(({ id }) => id === requestedWithinSetId)
    ? requestedWithinSetId
    : withinSetOptions[0]?.id ?? ''

  const withinShapeOptions = useMemo(() => [...new Set(
    algorithmRuns
      .filter((run) => run.benchmarkSetId === withinSetId)
      .map(({ shapeId }) => shapeId),
  )].sort(compareShapes), [algorithmRuns, withinSetId])

  const validRequestedWithinShapeIds = requestedWithinShapeIds
    .filter((shapeId) => withinShapeOptions.includes(shapeId))
    .slice(0, MAX_WITHIN_SHAPES)
  const withinShapeIds = validRequestedWithinShapeIds.length > 0
    ? validRequestedWithinShapeIds
    : withinShapeOptions.slice(0, MAX_WITHIN_SHAPES)

  const acrossShapeOptions = useMemo(() => [...new Set(
    algorithmRuns.map(({ shapeId }) => shapeId),
  )].sort(compareShapes), [algorithmRuns])

  const acrossShapeId = acrossShapeOptions.includes(requestedAcrossShapeId)
    ? requestedAcrossShapeId
    : acrossShapeOptions[0] ?? ''

  const acrossSetOptions = useMemo(() => benchmarkSets.filter((benchmarkSet) => (
    algorithmRuns.some((run) => (
      run.benchmarkSetId === benchmarkSet.id && run.shapeId === acrossShapeId
    ))
  )), [acrossShapeId, algorithmRuns, benchmarkSets])

  const validRequestedAcrossSetIds = requestedAcrossSetIds
    .filter((setId) => acrossSetOptions.some(({ id }) => id === setId))
  const acrossSetIds = validRequestedAcrossSetIds.length > 0
    ? validRequestedAcrossSetIds
    : acrossSetOptions.map(({ id }) => id)

  const nodeSizeOptions = useMemo(() => {
    const availableRuns = activeMode === 'solvers'
      ? trendRuns.filter((run) => (
          run.benchmarkSetId === solverComparisonSetId
          && run.shapeId === solverShapeId
          && run.solverKind
          && validMetricValue(run.metricValues.compute)
        ))
      : activeMode === 'within'
      ? algorithmRuns.filter((run) => (
          run.benchmarkSetId === withinSetId && withinShapeIds.includes(run.shapeId)
        ))
      : algorithmRuns.filter((run) => (
          run.shapeId === acrossShapeId && acrossSetIds.includes(run.benchmarkSetId)
        ))
    return [...new Set(availableRuns.map(({ nodeCount }) => nodeCount))]
      .sort((left, right) => left - right)
  }, [
    acrossSetIds,
    acrossShapeId,
    algorithmRuns,
    activeMode,
    solverComparisonSetId,
    solverShapeId,
    trendRuns,
    withinSetId,
    withinShapeIds,
  ])

  const validRequestedNodeCounts = requestedNodeCounts
    .filter((nodeCount) => nodeSizeOptions.includes(nodeCount))
  const selectedNodeCounts = validRequestedNodeCounts.length > 0
    ? validRequestedNodeCounts
    : nodeSizeOptions

  const selectedAlgorithmLabel = selectedAlgorithmOption?.label ?? 'Algorithm'
  const selectedWithinSet = withinSetOptions.find(({ id }) => id === withinSetId)
  const selectedAcrossShapeLabel = acrossShapeId ? getShapeLabel(acrossShapeId) : 'Graph shape'

  const series = useMemo<TrendSeries[]>(() => {
    if (activeMode === 'solvers') {
      const selectedRuns = trendRuns.flatMap<SelectedMetricTrendRun>((run) => {
        const metricValue = run.metricValues.compute
        return run.benchmarkSetId === solverComparisonSetId
          && run.shapeId === solverShapeId
          && run.solverKind
          && selectedNodeCounts.includes(run.nodeCount)
          && validMetricValue(metricValue)
          ? [{ ...run, metricValue }]
          : []
      })
      return rawSolverSeries(selectedRuns)
    }

    if (activeMode === 'within') {
      const selectedRuns = algorithmRuns.filter((run) => (
        run.benchmarkSetId === withinSetId
        && withinShapeIds.includes(run.shapeId)
        && selectedNodeCounts.includes(run.nodeCount)
      ))
      return aggregateSeries(
        selectedRuns,
        withinShapeIds.map((shapeId) => ({ id: shapeId, label: getShapeLabel(shapeId) })),
        (run) => run.shapeId,
      )
    }

    const selectedRuns = algorithmRuns.filter((run) => (
      run.shapeId === acrossShapeId
      && acrossSetIds.includes(run.benchmarkSetId)
      && selectedNodeCounts.includes(run.nodeCount)
    ))
    return aggregateSeries(
      selectedRuns,
      acrossSetOptions
        .filter(({ id }) => acrossSetIds.includes(id))
        .map((benchmarkSet) => ({
          id: benchmarkSet.id,
          label: getBenchmarkSetLabel(benchmarkSet, benchmarkSets),
        })),
      (run) => run.benchmarkSetId,
    )
  }, [
    acrossSetIds,
    acrossSetOptions,
    acrossShapeId,
    algorithmRuns,
    benchmarkSets,
    activeMode,
    selectedNodeCounts,
    withinSetId,
    withinShapeIds,
    solverComparisonSetId,
    solverShapeId,
    trendRuns,
  ])

  const tableRows = useMemo(() => series
    .flatMap((item, seriesIndex) => item.points.map((point) => ({
      ...point,
      seriesIndex,
    })))
    .sort((left, right) => (
      left.nodeCount - right.nodeCount || left.seriesIndex - right.seriesIndex
    )), [series])
  const presentation = useMemo(
    () => metricPresentation(metric, series),
    [metric, series],
  )
  const selectedSolverRuns = useMemo(() => (
    activeMode === 'solvers'
      ? series.flatMap(({ points }) => points.flatMap(({ sourceRuns }) => sourceRuns))
      : []
  ), [activeMode, series])
  const solverDuplicateGroupCount = useMemo(() => {
    const counts = new Map<string, number>()
    for (const run of selectedSolverRuns) {
      const key = `${run.solverKind ?? 'unknown'}-${run.nodeCount}`
      counts.set(key, (counts.get(key) ?? 0) + 1)
    }
    return [...counts.values()].filter((count) => count > 1).length
  }, [selectedSolverRuns])

  if (trendRuns.length === 0) {
    return (
      <section aria-labelledby={`${idPrefix}-heading`} className="insights-lab-trends insights-lab-trends--empty">
        <h3 id={`${idPrefix}-heading`}>Historical trends</h3>
        <p>
          Trends need recorded stress-graph runs assigned to a benchmark set. Completed runs and time-bounded attempts can both contribute. Select a benchmark set on the Run tab, then record a stress-graph run.
        </p>
      </section>
    )
  }

  const selectedSolverSet = solverComparisonSetOptions.find(({ id }) => id === solverComparisonSetId)
  const chartTitle = activeMode === 'solvers'
    ? 'Greedy vs time-bounded exhaustive search'
    : activeMode === 'within'
      ? `${selectedAlgorithmLabel} by graph shape`
      : `${selectedAlgorithmLabel} across benchmark sets`
  const graphSizeDescription = selectedNodeCounts.length === nodeSizeOptions.length
    ? 'all available graph sizes'
    : `${selectedNodeCounts.map(formatNodeCount).join(', ')} nodes`
  const chartDescription = activeMode === 'solvers'
    ? `${selectedSolverSet ? getBenchmarkSetLabel(selectedSolverSet, benchmarkSets) : 'Selected benchmark set'} · ${getShapeLabel(solverShapeId)} · ${graphSizeDescription} · one raw run per point`
    : activeMode === 'within'
      ? `${selectedWithinSet ? getBenchmarkSetLabel(selectedWithinSet, benchmarkSets) : 'Selected benchmark set'} · ${graphSizeDescription} · median ${metric.label.toLowerCase()} for repeated runs`
      : `${selectedAcrossShapeLabel} · ${graphSizeDescription} · median ${metric.label.toLowerCase()} for repeated runs`

  return (
    <section
      aria-labelledby={`${idPrefix}-heading`}
      className="insights-lab-trends"
      onKeyDown={(event) => {
        if (event.key !== 'Escape' || !showHowToRead) return
        event.preventDefault()
        event.stopPropagation()
        setShowHowToRead(false)
        howToReadButtonRef.current?.focus()
      }}
    >
      <div className="insights-lab-trends__intro">
        <div>
          <h3 id={`${idPrefix}-heading`}>Historical trends</h3>
          <p>Compare performance across the graph sizes, shapes, and benchmark sets you choose.</p>
        </div>
        <div className="insights-lab-trends__intro-actions">
          <fieldset className="insights-lab-trends__scale" role="radiogroup">
            <legend>Y-axis scale</legend>
            <div>
              <label>
                <input
                  checked={yAxisScale === 'linear'}
                  name={`${idPrefix}-y-axis-scale`}
                  onChange={() => setYAxisScale('linear')}
                  type="radio"
                />
                <span>Linear</span>
              </label>
              <label>
                <input
                  checked={yAxisScale === 'logarithmic'}
                  name={`${idPrefix}-y-axis-scale`}
                  onChange={() => setYAxisScale('logarithmic')}
                  type="radio"
                />
                <span>Logarithmic</span>
              </label>
            </div>
          </fieldset>
          <button
            aria-controls={`${idPrefix}-how-to-read`}
            aria-expanded={showHowToRead}
            onClick={() => setShowHowToRead((current) => !current)}
            ref={howToReadButtonRef}
            type="button"
          >
            How to read
          </button>
        </div>
      </div>

      <section
        aria-labelledby={`${idPrefix}-how-to-read-title`}
        className="insights-lab-trends__guide"
        hidden={!showHowToRead}
        id={`${idPrefix}-how-to-read`}
        role="region"
      >
        <header>
          <strong id={`${idPrefix}-how-to-read-title`}>How to read this chart</strong>
          <button
            onClick={() => {
              setShowHowToRead(false)
              howToReadButtonRef.current?.focus()
            }}
            type="button"
          >
            Close how to read
          </button>
        </header>
        <dl>
          <div>
            <dt>Axes</dt>
            <dd>
              Graph size runs left to right on a logarithmic scale, so each tenfold increase gets equal space. The Y-axis shows the selected metric; lower is generally better.
            </dd>
          </div>
          <div>
            <dt>Lines and points</dt>
            <dd>
              {activeMode === 'solvers'
                ? 'Each point is one recorded run. Lines connect completed measurements only to guide the eye; they do not measure the sizes between them, and no observations are averaged.'
                : 'A line represents a graph shape within one set, or a benchmark set in the across-set view. Lines connect completed measured sizes to guide the eye; they do not measure the sizes between them. One completed run is shown raw; repeated matching completed runs are represented by their median and sample count.'}
            </dd>
          </div>
          {activeMode === 'solvers' ? (
            <>
              <div>
                <dt>What the solvers establish</dt>
                <dd>
                  Greedy quickly searches for a usable counter set; when it finds one, it does not prove that it is the smallest. The time-bounded exhaustive reference searches by set size to try to prove a minimum. This describes the current reference implementation, not a claim that every possible exact method must perform the same search.
                </dd>
              </div>
              <div>
                <dt>Stopped observations</dt>
                <dd>
                  An open arrow marker is right-censored: the exhaustive search reached its fixed time budget before proving a minimum. Its plotted time is a lower bound, so it is not connected or combined with completed observations.
                </dd>
              </div>
              <div>
                <dt>Search frontier</dt>
                <dd>
                  The data table shows the largest set size fully exhausted and progress within the active size. “% of maximum subset space” compares evaluated subsets with every possible subset; it is workload context, not a completion estimate.
                </dd>
              </div>
            </>
          ) : null}
          <div>
            <dt>{metric.label}</dt>
            <dd>{metric.explanation} {metric.significance}</dd>
          </div>
          <div>
            <dt>Y-axis scale</dt>
            <dd>
              Linear spacing emphasizes absolute differences. Logarithmic spacing emphasizes ratios and keeps widely separated values readable; a zero value uses a separate baseline below the positive values.
            </dd>
          </div>
          <div>
            <dt>Fair comparisons</dt>
            <dd>
              Compare matching algorithms, graph shapes, sizes, and targets. Differences between benchmark sets can also reflect build configuration, hardware, and other backend activity.
            </dd>
          </div>
        </dl>
      </section>

      <div className={`insights-lab-trends__controls${activeMode === 'solvers' ? ' insights-lab-trends__controls--solvers' : ''}`}>
        <fieldset className="insights-lab-trends__mode">
          <legend>Comparison</legend>
          <label>
            <input
              checked={activeMode === 'solvers'}
              disabled={!hasSolverComparison}
              name={`${idPrefix}-mode`}
              onChange={() => {
                setMode('solvers')
                setRequestedNodeCounts([])
                setRequestedMetricId('compute')
                setYAxisScale('logarithmic')
              }}
              type="radio"
            />
            <span>Compare counter-set solvers</span>
          </label>
          <label>
            <input
              checked={activeMode === 'within'}
              name={`${idPrefix}-mode`}
              onChange={() => {
                setMode('within')
                setRequestedNodeCounts([])
              }}
              type="radio"
            />
            <span>Scale within benchmark set</span>
          </label>
          <label>
            <input
              checked={activeMode === 'across'}
              name={`${idPrefix}-mode`}
              onChange={() => {
                setMode('across')
                setRequestedNodeCounts([])
              }}
              type="radio"
            />
            <span>Compare benchmark sets</span>
          </label>
        </fieldset>

        {activeMode === 'solvers' ? (
          <>
            <label className="insights-lab-trends__select">
              <span>Benchmark set</span>
              <select
                onChange={(event) => {
                  setRequestedWithinSetId(event.target.value)
                  setRequestedAcrossShapeId('')
                  setRequestedNodeCounts([])
                }}
                value={solverComparisonSetId}
              >
                {solverComparisonSetOptions.map((benchmarkSet) => (
                  <option key={benchmarkSet.id} value={benchmarkSet.id}>
                    {getBenchmarkSetLabel(benchmarkSet, benchmarkSets)}
                  </option>
                ))}
              </select>
            </label>
            <label className="insights-lab-trends__select">
              <span>Graph shape</span>
              <select
                onChange={(event) => {
                  setRequestedAcrossShapeId(event.target.value)
                  setRequestedNodeCounts([])
                }}
                value={solverShapeId}
              >
                {solverShapeOptions.map((shapeId) => (
                  <option key={shapeId} value={shapeId}>{getShapeLabel(shapeId)}</option>
                ))}
              </select>
            </label>
            <div className="insights-lab-trends__static-value">
              <span>Metric</span>
              <strong>Compute time</strong>
            </div>
          </>
        ) : (
          <>
            <label className="insights-lab-trends__select">
              <span>Algorithm</span>
              <select
                onChange={(event) => {
                  setRequestedAlgorithmId(event.target.value)
                  setRequestedWithinSetId('')
                  setRequestedWithinShapeIds([])
                  setRequestedAcrossShapeId('')
                  setRequestedAcrossSetIds([])
                  setRequestedNodeCounts([])
                }}
                value={algorithmId}
              >
                {algorithmOptions.map((option) => (
                  <option key={option.id} value={option.id}>{option.label}</option>
                ))}
              </select>
            </label>

            <label className="insights-lab-trends__select">
              <span>Metric</span>
              <select
                onChange={(event) => {
                  setRequestedMetricId(event.target.value as TrendMetricId)
                  setRequestedNodeCounts([])
                }}
                value={metricId}
              >
                {availableMetrics.map((option) => (
                  <option
                    disabled={!availableMetricIds.has(option.id)}
                    key={option.id}
                    value={option.id}
                  >
                    {option.label}
                  </option>
                ))}
              </select>
            </label>

            {activeMode === 'within' ? (
              <>
                <label className="insights-lab-trends__select">
                  <span>Benchmark set</span>
                  <select
                    onChange={(event) => {
                      setRequestedWithinSetId(event.target.value)
                      setRequestedWithinShapeIds([])
                      setRequestedNodeCounts([])
                    }}
                    value={withinSetId}
                  >
                    {withinSetOptions.map((benchmarkSet) => (
                      <option key={benchmarkSet.id} value={benchmarkSet.id}>
                        {getBenchmarkSetLabel(benchmarkSet, benchmarkSets)}
                      </option>
                    ))}
                  </select>
                </label>
                <fieldset className="insights-lab-trends__checks">
                  <legend>Graph shapes <span>(up to {MAX_WITHIN_SHAPES})</span></legend>
                  <div>
                    {withinShapeOptions.map((shapeId) => {
                      const checked = withinShapeIds.includes(shapeId)
                      const atLimit = withinShapeIds.length >= MAX_WITHIN_SHAPES
                      return (
                        <label key={shapeId}>
                          <input
                            checked={checked}
                            disabled={(checked && withinShapeIds.length === 1) || (!checked && atLimit)}
                            onChange={(event) => {
                              setRequestedWithinShapeIds(
                                event.target.checked
                                  ? [...withinShapeIds, shapeId].slice(0, MAX_WITHIN_SHAPES)
                                  : withinShapeIds.filter((selected) => selected !== shapeId),
                              )
                            }}
                            type="checkbox"
                          />
                          <span>{getShapeLabel(shapeId)}</span>
                        </label>
                      )
                    })}
                  </div>
                </fieldset>
              </>
            ) : (
              <>
                <label className="insights-lab-trends__select">
                  <span>Graph shape</span>
                  <select
                    onChange={(event) => {
                      setRequestedAcrossShapeId(event.target.value)
                      setRequestedAcrossSetIds([])
                      setRequestedNodeCounts([])
                    }}
                    value={acrossShapeId}
                  >
                    {acrossShapeOptions.map((shapeId) => (
                      <option key={shapeId} value={shapeId}>{getShapeLabel(shapeId)}</option>
                    ))}
                  </select>
                </label>
                <fieldset className="insights-lab-trends__checks">
                  <legend>Benchmark sets</legend>
                  <div>
                    {acrossSetOptions.map((benchmarkSet) => {
                      const checked = acrossSetIds.includes(benchmarkSet.id)
                      return (
                        <label key={benchmarkSet.id}>
                          <input
                            checked={checked}
                            disabled={checked && acrossSetIds.length === 1}
                            onChange={(event) => setRequestedAcrossSetIds(
                              event.target.checked
                                ? [...acrossSetIds, benchmarkSet.id]
                                : acrossSetIds.filter((selected) => selected !== benchmarkSet.id),
                            )}
                            type="checkbox"
                          />
                          <span>{getBenchmarkSetLabel(benchmarkSet, benchmarkSets)}</span>
                        </label>
                      )
                    })}
                  </div>
                </fieldset>
              </>
            )}
          </>
        )}

        <fieldset className="insights-lab-trends__checks insights-lab-trends__sizes">
          <legend>Graph sizes</legend>
          <div>
            {nodeSizeOptions.map((nodeCount) => {
              const checked = selectedNodeCounts.includes(nodeCount)
              return (
                <label key={nodeCount}>
                  <input
                    checked={checked}
                    disabled={checked && selectedNodeCounts.length === 1}
                    onChange={(event) => setRequestedNodeCounts(
                      event.target.checked
                        ? [...selectedNodeCounts, nodeCount].sort((left, right) => left - right)
                        : selectedNodeCounts.filter((selected) => selected !== nodeCount),
                    )}
                    type="checkbox"
                  />
                  <span>{formatNodeCount(nodeCount)} nodes</span>
                </label>
              )
            })}
          </div>
        </fieldset>
      </div>

      {series.length > 0 ? (
        <>
          <TrendChart
            description={chartDescription}
            idPrefix={idPrefix}
            metric={metric}
            presentation={presentation}
            series={series}
            title={chartTitle}
            yAxisScale={yAxisScale}
          />
          <div className="insights-lab-trends__data-actions">
            <button
              aria-controls={`${idPrefix}-data-table`}
              aria-expanded={showData}
              onClick={() => setShowData((current) => !current)}
              type="button"
            >
              {showData ? 'Hide data' : 'View data'}
            </button>
            <span>
              {activeMode === 'solvers'
                ? 'Each point is one raw run; no averaging is applied.'
                : 'n=1 shows the raw run; completed repeats show the median.'}
            </span>
          </div>
          {activeMode === 'solvers' && solverDuplicateGroupCount > 0 ? (
            <p className="insights-lab-trends__duplicate-note" role="status">
              {solverDuplicateGroupCount} solver/size {solverDuplicateGroupCount === 1 ? 'selection has' : 'selections have'} duplicate runs. Every observation is shown separately; none are averaged.
            </p>
          ) : null}
          {showData && (
            <div
              aria-label="Trend data"
              className="insights-lab-trends__table-wrap"
              id={`${idPrefix}-data-table`}
              role="region"
              tabIndex={0}
            >
              {activeMode === 'solvers' ? (
                <table>
                  <caption>Raw greedy and exhaustive observations plotted above</caption>
                  <thead>
                    <tr>
                      <th scope="col">Solver</th>
                      <th scope="col">Nodes</th>
                      <th scope="col">Compute time</th>
                      <th scope="col">Minimum proof</th>
                      <th scope="col">Candidates</th>
                      <th scope="col">Subset evaluations</th>
                      <th scope="col">Search frontier</th>
                      <th scope="col">% of maximum subset space</th>
                      <th scope="col">Run</th>
                    </tr>
                  </thead>
                  <tbody>
                    {[...selectedSolverRuns]
                      .sort((left, right) => (
                        left.nodeCount - right.nodeCount
                        || (left.solverKind ?? '').localeCompare(right.solverKind ?? '')
                        || left.runNumber - right.runNumber
                      ))
                      .map((run) => {
                        const candidateCount = readNumber(
                          run.source.details,
                          'totalCandidateCount',
                          'candidateCountTotal',
                          'candidateCount',
                        )
                        const subsetEvaluations = readNumber(
                          run.source.details,
                          'subsetEvaluations',
                          'subsetsEvaluated',
                        )
                        return (
                          <tr key={`solver-run-${run.runNumber}`}>
                            <td>{run.solverKind === 'exhaustive' ? 'Time-bounded exhaustive' : 'Greedy'}</td>
                            <td>{formatNodeCount(run.nodeCount)}</td>
                            <td>
                              {run.timedOut
                                ? `${formatDurationLimit(run.metricValues.compute ?? 0)} · stopped`
                                : presentation.formatValue(run.metricValues.compute ?? 0)}
                            </td>
                            <td>{solverProofLabel(run)}</td>
                            <td>{formatIntegerDetail(candidateCount)}</td>
                            <td>
                              {run.solverKind === 'exhaustive'
                                ? formatIntegerDetail(subsetEvaluations)
                                : '—'}
                            </td>
                            <td className="insights-lab-trends__frontier-cell">{solverFrontierLabel(run)}</td>
                            <td>{run.solverKind === 'exhaustive' ? formatMaximumSubsetSpacePercent(run) : '—'}</td>
                            <td>#{run.runNumber}</td>
                          </tr>
                        )
                      })}
                  </tbody>
                </table>
              ) : (
                <table>
                  <caption>
                    {metric.id === 'compute'
                      ? 'Compute-time values plotted above'
                      : `${metric.label} values plotted above`}
                  </caption>
                  <thead>
                    <tr>
                      <th scope="col">Benchmark set</th>
                      <th scope="col">Graph shape</th>
                      <th scope="col">Nodes</th>
                      <th scope="col">{metric.label}</th>
                      <th scope="col">Samples</th>
                      <th scope="col">Runs</th>
                    </tr>
                  </thead>
                  <tbody>
                    {tableRows.map((row) => (
                      <tr key={row.id}>
                        <td>{row.benchmarkSetName}</td>
                        <td>{row.shapeLabel}</td>
                        <td>{formatNodeCount(row.nodeCount)}</td>
                        <td>
                          {row.timedOut
                            ? formatStoppedMetricValue(row, metric, presentation)
                            : presentation.formatValue(row.metricValue)}
                          {!row.timedOut && row.sampleCount > 1 && <span> median</span>}
                        </td>
                        <td>{row.sampleCount}</td>
                        <td>{row.runNumbers.map((runNumber) => `#${runNumber}`).join(', ')}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          )}
        </>
      ) : (
        <p aria-live="polite" className="insights-lab-trends__no-match">
          No comparable runs with {metric.label.toLowerCase()} data match these selections.
        </p>
      )}
    </section>
  )
}
