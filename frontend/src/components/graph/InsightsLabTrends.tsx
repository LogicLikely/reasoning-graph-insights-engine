import { useId, useMemo, useState } from 'react'
import type { BenchmarkSet, PerformanceRunRecord } from '../../services/performanceRuns'
import { isStressGraphId } from '../../services/stressGraphs'
import './InsightsLabTrends.css'

export interface InsightsLabTrendsProps {
  runs: readonly PerformanceRunRecord[]
  benchmarkSets: readonly BenchmarkSet[]
}

type TrendMode = 'within' | 'across'

type TrendRun = {
  runNumber: number
  algorithmId: string
  algorithmLabel: string
  benchmarkSetId: string
  benchmarkSetName: string
  shapeId: string
  shapeLabel: string
  nodeCount: number
  computeMilliseconds: number
}

type TrendPoint = {
  benchmarkSetId: string
  benchmarkSetName: string
  shapeLabel: string
  nodeCount: number
  computeMilliseconds: number
  sampleCount: number
  runNumbers: number[]
}

type TrendSeries = {
  id: string
  label: string
  points: TrendPoint[]
}

const MAX_WITHIN_SHAPES = 4
const CHART_WIDTH = 920
const CHART_HEIGHT = 360
const PLOT_LEFT = 82
const PLOT_RIGHT = 28
const PLOT_TOP = 22
const PLOT_BOTTOM = 58

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

function formatMilliseconds(value: number): string {
  return `${value.toLocaleString(undefined, { maximumFractionDigits: 2 })} ms`
}

function formatNodeCount(value: number): string {
  return value.toLocaleString()
}

function formatAxisNumber(value: number): string {
  return value.toLocaleString(undefined, { maximumFractionDigits: value < 10 ? 1 : 0 })
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
  runs: readonly TrendRun[],
  seriesDefinitions: readonly { id: string; label: string }[],
  seriesIdForRun: (run: TrendRun) => string,
): TrendSeries[] {
  return seriesDefinitions.flatMap((definition) => {
    const grouped = new Map<number, TrendRun[]>()
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
        computeMilliseconds: median(matches.map(({ computeMilliseconds }) => computeMilliseconds)),
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
}

function TrendChart({ idPrefix, title, description, series }: TrendChartProps) {
  const points = series.flatMap(({ points: seriesPoints }) => seriesPoints)
  const nodeCounts = [...new Set(points.map(({ nodeCount }) => nodeCount))].sort((left, right) => left - right)
  const plotWidth = CHART_WIDTH - PLOT_LEFT - PLOT_RIGHT
  const plotHeight = CHART_HEIGHT - PLOT_TOP - PLOT_BOTTOM
  const minimumLog = Math.log10(Math.max(nodeCounts[0] ?? 1, 1))
  const maximumLog = Math.log10(Math.max(nodeCounts[nodeCounts.length - 1] ?? 1, 1))
  const rawMaximum = Math.max(...points.map(({ computeMilliseconds }) => computeMilliseconds), 0)
  const yMaximum = niceMaximum(rawMaximum * 1.08)
  const yTicks = Array.from({ length: 5 }, (_, index) => (yMaximum * index) / 4)

  const xForNodeCount = (nodeCount: number) => {
    if (minimumLog === maximumLog) return PLOT_LEFT + plotWidth / 2
    return PLOT_LEFT + ((Math.log10(Math.max(nodeCount, 1)) - minimumLog) / (maximumLog - minimumLog)) * plotWidth
  }
  const yForMilliseconds = (milliseconds: number) => (
    PLOT_TOP + plotHeight - (milliseconds / yMaximum) * plotHeight
  )

  return (
    <figure className="insights-lab-trends__figure">
      <figcaption>
        <strong>{title}</strong>
        <span>{description}</span>
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
          <desc id={`${idPrefix}-chart-description`}>{description}. Points show compute time by graph size.</desc>

          {yTicks.map((tick) => {
            const y = yForMilliseconds(tick)
            return (
              <g key={tick}>
                <line
                  className="insights-lab-trends__grid-line"
                  x1={PLOT_LEFT}
                  x2={CHART_WIDTH - PLOT_RIGHT}
                  y1={y}
                  y2={y}
                />
                <text className="insights-lab-trends__axis-tick" textAnchor="end" x={PLOT_LEFT - 12} y={y + 4}>
                  {formatAxisNumber(tick)}
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
            Compute time (ms)
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
              y: yForMilliseconds(point.computeMilliseconds),
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
                      {`${item.label}, ${formatNodeCount(point.nodeCount)} nodes: ${formatMilliseconds(point.computeMilliseconds)}${point.sampleCount > 1 ? ` median, n=${point.sampleCount}` : ', n=1'}`}
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
  const [mode, setMode] = useState<TrendMode>('within')
  const [requestedAlgorithmId, setRequestedAlgorithmId] = useState('')
  const [requestedWithinSetId, setRequestedWithinSetId] = useState('')
  const [requestedWithinShapeIds, setRequestedWithinShapeIds] = useState<string[]>([])
  const [requestedAcrossShapeId, setRequestedAcrossShapeId] = useState('')
  const [requestedAcrossSetIds, setRequestedAcrossSetIds] = useState<string[]>([])
  const [requestedNodeCounts, setRequestedNodeCounts] = useState<number[]>([])
  const [showData, setShowData] = useState(false)

  const benchmarkSetsById = useMemo(
    () => new Map(benchmarkSets.map((benchmarkSet) => [benchmarkSet.id, benchmarkSet])),
    [benchmarkSets],
  )

  const trendRuns = useMemo<TrendRun[]>(() => runs.flatMap((run) => {
    const benchmarkSetId = run.benchmarkSetId
    const benchmarkSet = benchmarkSetId ? benchmarkSetsById.get(benchmarkSetId) : undefined
    const slug = run.graph?.slug
    const nodeCount = run.graph?.nodeCount
    const computeMilliseconds = run.timing?.computeElapsedMilliseconds
    const algorithmIdForRun = getAlgorithmId(run)
    const shapeId = getShapeId(run)

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
      || typeof computeMilliseconds !== 'number'
      || !Number.isFinite(computeMilliseconds)
      || computeMilliseconds < 0
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
      computeMilliseconds,
    }]
  }), [benchmarkSets, benchmarkSetsById, runs])

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
    () => trendRuns.filter((run) => run.algorithmId === algorithmId),
    [algorithmId, trendRuns],
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
    ? `${selectedWithinSet ? getBenchmarkSetLabel(selectedWithinSet, benchmarkSets) : 'Selected benchmark set'} · ${graphSizeDescription} · median compute time for repeated runs`
    : `${selectedAcrossShapeLabel} · ${graphSizeDescription} · median compute time for repeated runs`

  return (
    <section aria-labelledby={`${idPrefix}-heading`} className="insights-lab-trends">
      <div className="insights-lab-trends__intro">
        <div>
          <h3 id={`${idPrefix}-heading`}>Historical trends</h3>
          <p>Compare compute time across the graph sizes and shapes you choose.</p>
        </div>
      </div>

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
            series={series}
            title={chartTitle}
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
                <caption>Compute-time values plotted above</caption>
                <thead>
                  <tr>
                    <th scope="col">Benchmark set</th>
                    <th scope="col">Graph shape</th>
                    <th scope="col">Nodes</th>
                    <th scope="col">Compute time</th>
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
                        {formatMilliseconds(row.computeMilliseconds)}
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
          No comparable runs match these selections.
        </p>
      )}
    </section>
  )
}
