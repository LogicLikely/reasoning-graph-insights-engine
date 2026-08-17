import { useId, useMemo, useRef, useState } from 'react'
import type { BenchmarkSet, PerformanceRunRecord } from '../../services/performanceRuns'
import { isStressGraphId } from '../../services/stressGraphs'
import './InsightsLabTrends.css'

export interface InsightsLabTrendsProps {
  runs: readonly PerformanceRunRecord[]
  benchmarkSets: readonly BenchmarkSet[]
}

type TrendMode = 'within' | 'across'
type TrendMetricId = 'compute' | 'operation' | 'cpu' | 'allocations'
type YAxisScale = 'linear' | 'logarithmic'

type TrendMetricDefinition = {
  id: TrendMetricId
  label: string
  axisLabel: string
  unitKind: 'milliseconds' | 'bytes'
  explanation: string
  significance: string
  readValue: (run: PerformanceRunRecord) => number | null | undefined
}

type TrendRun = {
  runNumber: number
  algorithmId: string
  algorithmLabel: string
  benchmarkSetId: string
  benchmarkSetName: string
  shapeId: string
  shapeLabel: string
  nodeCount: number
  metricValues: Partial<Record<TrendMetricId, number>>
}

type SelectedMetricTrendRun = TrendRun & {
  metricValue: number
}

type TrendPoint = {
  benchmarkSetId: string
  benchmarkSetName: string
  shapeLabel: string
  nodeCount: number
  metricValue: number
  sampleCount: number
  runNumbers: number[]
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
    return implementation === 'bounded-brute-force'
      ? 'Bounded minimal counter set'
      : 'Minimal counter set'
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

function getShapeId(run: PerformanceRunRecord): string | undefined {
  const explicitType = run.graph?.type?.trim()
  if (explicitType) return explicitType

  const slug = run.graph?.slug
  if (!slug) return undefined
  return slug.replace(/^stress-/, '').replace(/-(?:1k|10k|100k)$/i, '')
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
  return status === 'completed' || status === 'notproven'
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
      .map(([nodeCount, matches]) => ({
        benchmarkSetId: matches[0].benchmarkSetId,
        benchmarkSetName: matches[0].benchmarkSetName,
        shapeLabel: matches[0].shapeLabel,
        nodeCount,
        metricValue: median(matches.map(({ metricValue }) => metricValue)),
        sampleCount: matches.length,
        runNumbers: matches.map(({ runNumber }) => runNumber).sort((left, right) => left - right),
      }))

    return points.length > 0 ? [{ ...definition, points }] : []
  })
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
  const displayedValues = points.map(({ metricValue }) => metricValue / presentation.divisor)
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
            const pointList = chartPoints.map(({ x, y }) => `${x},${y}`).join(' ')
            const color = SERIES_COLORS[seriesIndex % SERIES_COLORS.length]
            return (
              <g key={item.id}>
                {chartPoints.length > 1 && (
                  <polyline
                    className="insights-lab-trends__series-line"
                    fill="none"
                    points={pointList}
                    stroke={color}
                    strokeDasharray={SERIES_DASHES[seriesIndex % SERIES_DASHES.length]}
                    strokeWidth="2.5"
                    vectorEffect="non-scaling-stroke"
                  />
                )}
                {chartPoints.map((point) => (
                  <g key={point.nodeCount}>
                    <title>
                      {`${item.label}, ${formatNodeCount(point.nodeCount)} nodes: ${presentation.formatValue(point.metricValue)}${point.sampleCount > 1 ? ` median, n=${point.sampleCount}` : ', n=1'}`}
                    </title>
                    <SeriesMarker index={seriesIndex} x={point.x} y={point.y} />
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
  const [mode, setMode] = useState<TrendMode>('within')
  const [requestedMetricId, setRequestedMetricId] = useState<TrendMetricId>('compute')
  const [yAxisScale, setYAxisScale] = useState<YAxisScale>('linear')
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
      benchmarkSetId: benchmarkSet.id,
      benchmarkSetName: getBenchmarkSetLabel(benchmarkSet, benchmarkSets),
      shapeId,
      shapeLabel: getShapeLabel(shapeId),
      nodeCount,
      metricValues,
    }]
  }), [benchmarkSets, benchmarkSetsById, runs])

  const availableMetricIds = useMemo(() => new Set(
    TREND_METRICS
      .filter((metric) => trendRuns.some((run) => validMetricValue(run.metricValues[metric.id])))
      .map(({ id }) => id),
  ), [trendRuns])
  const metricId = availableMetricIds.has(requestedMetricId)
    ? requestedMetricId
    : TREND_METRICS.find(({ id }) => availableMetricIds.has(id))?.id ?? 'compute'
  const metric = metricDefinition(metricId)

  const selectedMetricRuns = useMemo<SelectedMetricTrendRun[]>(() => trendRuns.flatMap((run) => {
    const metricValue = run.metricValues[metricId]
    return validMetricValue(metricValue) ? [{ ...run, metricValue }] : []
  }), [metricId, trendRuns])

  const algorithmOptions = useMemo(() => {
    const options = new Map<string, string>()
    for (const run of trendRuns) options.set(run.algorithmId, run.algorithmLabel)
    return [...options.entries()]
      .map(([id, label]) => ({ id, label }))
      .sort((left, right) => left.label.localeCompare(right.label))
  }, [trendRuns])

  const algorithmId = algorithmOptions.some(({ id }) => id === requestedAlgorithmId)
    ? requestedAlgorithmId
    : algorithmOptions[0]?.id ?? ''

  const algorithmRuns = useMemo(
    () => selectedMetricRuns.filter((run) => run.algorithmId === algorithmId),
    [algorithmId, selectedMetricRuns],
  )

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
    const availableRuns = mode === 'within'
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
    mode,
    withinSetId,
    withinShapeIds,
  ])

  const validRequestedNodeCounts = requestedNodeCounts
    .filter((nodeCount) => nodeSizeOptions.includes(nodeCount))
  const selectedNodeCounts = validRequestedNodeCounts.length > 0
    ? validRequestedNodeCounts
    : nodeSizeOptions

  const selectedAlgorithmLabel = algorithmOptions.find(({ id }) => id === algorithmId)?.label ?? 'Algorithm'
  const selectedWithinSet = withinSetOptions.find(({ id }) => id === withinSetId)
  const selectedAcrossShapeLabel = acrossShapeId ? getShapeLabel(acrossShapeId) : 'Graph shape'

  const series = useMemo<TrendSeries[]>(() => {
    if (mode === 'within') {
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
    mode,
    selectedNodeCounts,
    withinSetId,
    withinShapeIds,
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

  if (trendRuns.length === 0) {
    return (
      <section aria-labelledby={`${idPrefix}-heading`} className="insights-lab-trends insights-lab-trends--empty">
        <h3 id={`${idPrefix}-heading`}>Historical trends</h3>
        <p>
          Trends need completed stress-graph runs assigned to a benchmark set. Select a benchmark set on the Run tab, then record a stress-graph run.
        </p>
      </section>
    )
  }

  const chartTitle = mode === 'within'
    ? `${selectedAlgorithmLabel} by graph shape`
    : `${selectedAlgorithmLabel} across benchmark sets`
  const graphSizeDescription = selectedNodeCounts.length === nodeSizeOptions.length
    ? 'all available graph sizes'
    : `${selectedNodeCounts.map(formatNodeCount).join(', ')} nodes`
  const chartDescription = mode === 'within'
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
              A line represents a graph shape within one set, or a benchmark set in the across-set view. Lines connect measured sizes to guide the eye; they do not measure the sizes between them. One run is shown raw; repeated matching runs are represented by their median and sample count.
            </dd>
          </div>
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

      <div className="insights-lab-trends__controls">
        <fieldset className="insights-lab-trends__mode">
          <legend>Comparison</legend>
          <label>
            <input
              checked={mode === 'within'}
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
              checked={mode === 'across'}
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
            {TREND_METRICS.map((option) => (
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

        {mode === 'within' ? (
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
            <span>n=1 shows the raw run; repeats show the median.</span>
          </div>
          {showData && (
            <div className="insights-lab-trends__table-wrap" id={`${idPrefix}-data-table`}>
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
                    <tr key={`${row.benchmarkSetId}-${row.shapeLabel}-${row.nodeCount}`}>
                      <td>{row.benchmarkSetName}</td>
                      <td>{row.shapeLabel}</td>
                      <td>{formatNodeCount(row.nodeCount)}</td>
                      <td>
                        {presentation.formatValue(row.metricValue)}
                        {row.sampleCount > 1 && <span> median</span>}
                      </td>
                      <td>{row.sampleCount}</td>
                      <td>{row.runNumbers.map((runNumber) => `#${runNumber}`).join(', ')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
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
