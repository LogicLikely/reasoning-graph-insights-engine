import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { BenchmarkSet, PerformanceRunRecord } from '../../services/performanceRuns'
import { InsightsLabTrends } from './InsightsLabTrends'

const benchmarkSets: BenchmarkSet[] = [
  {
    id: 'legacy',
    name: 'LL-699 legacy baseline',
    createdAtUtc: '2026-08-17T12:00:00Z',
  },
  {
    id: 'bayesian',
    name: 'Bayesian rework',
    createdAtUtc: '2026-08-18T12:00:00Z',
  },
]

interface RunOptions {
  runNumber: number
  benchmarkSetId?: string | null
  algorithmName?: string
  implementation?: string
  slug?: string
  shape?: string
  nodeCount?: number
  computeMilliseconds?: number
  operationMilliseconds?: number
  cpuMilliseconds?: number
  allocatedBytes?: number | null
  subsetEvaluations?: number | null
  status?: string
}

function performanceRun({
  runNumber,
  benchmarkSetId = 'legacy',
  algorithmName = 'minimal-counter-set',
  implementation = 'greedy',
  slug = 'stress-balanced-1k',
  shape = 'balanced',
  nodeCount = 1_000,
  computeMilliseconds = 10,
  operationMilliseconds = computeMilliseconds + 100,
  cpuMilliseconds = computeMilliseconds / 2,
  allocatedBytes = computeMilliseconds * 100,
  subsetEvaluations,
  status = 'completed',
}: RunOptions): PerformanceRunRecord {
  return {
    runNumber,
    benchmarkSetId,
    algorithm: {
      name: algorithmName,
      implementation,
      calculationModel: 'graph-likelihood-calculator',
    },
    graph: { slug, type: shape, nodeCount },
    timing: {
      computeElapsedMilliseconds: computeMilliseconds,
      operationElapsedMilliseconds: operationMilliseconds,
    },
    resources: {
      cpuTimeMilliseconds: cpuMilliseconds,
      allocatedBytes,
    },
    outcome: { status },
    details: subsetEvaluations === undefined
      ? {}
      : { subsetEvaluations },
  }
}

const representativeRuns: PerformanceRunRecord[] = [
  performanceRun({ runNumber: 1, computeMilliseconds: 10 }),
  performanceRun({ runNumber: 2, computeMilliseconds: 20 }),
  performanceRun({
    runNumber: 3,
    slug: 'stress-balanced-10k',
    nodeCount: 10_000,
    computeMilliseconds: 40,
    status: 'notProven',
  }),
  performanceRun({
    runNumber: 4,
    slug: 'stress-wide-1k',
    shape: 'wide',
    computeMilliseconds: 8,
  }),
  performanceRun({
    runNumber: 5,
    benchmarkSetId: 'bayesian',
    computeMilliseconds: 12,
  }),
  performanceRun({
    runNumber: 6,
    benchmarkSetId: 'bayesian',
    slug: 'stress-balanced-10k',
    nodeCount: 10_000,
    computeMilliseconds: 28,
  }),
  performanceRun({
    runNumber: 7,
    implementation: 'time-bounded-exhaustive',
    computeMilliseconds: 100,
  }),
  performanceRun({ runNumber: 8, benchmarkSetId: null, computeMilliseconds: 1 }),
  performanceRun({
    runNumber: 9,
    slug: 'sample-medium',
    shape: 'sample',
    nodeCount: 27,
    computeMilliseconds: 1,
  }),
  performanceRun({ runNumber: 10, status: 'failed', computeMilliseconds: 1 }),
  performanceRun({ runNumber: 11, status: 'cancelled', computeMilliseconds: 1 }),
]

async function selectMinimalCounterSet() {
  const withinMode = screen.getByRole('radio', { name: 'Scale within benchmark set' })
  if (!withinMode.matches(':checked')) fireEvent.click(withinMode)
  const algorithm = await screen.findByRole('combobox', { name: 'Algorithm' })
  fireEvent.change(algorithm, {
    target: { value: screen.getByRole('option', { name: 'Minimal counter set' }).getAttribute('value') },
  })
  await waitFor(() => expect(algorithm).toHaveDisplayValue('Minimal counter set'))
}

async function selectBoundedMinimalCounterSet() {
  const withinMode = screen.getByRole('radio', { name: 'Scale within benchmark set' })
  if (!withinMode.matches(':checked')) fireEvent.click(withinMode)
  const algorithm = await screen.findByRole('combobox', { name: 'Algorithm' })
  fireEvent.change(algorithm, {
    target: {
      value: screen.getByRole('option', { name: 'Time-bounded exhaustive search' })
        .getAttribute('value'),
    },
  })
  await waitFor(() => expect(algorithm).toHaveDisplayValue('Time-bounded exhaustive search'))
}

async function selectMetric(label: string) {
  const metric = await screen.findByRole('combobox', { name: 'Metric' })
  fireEvent.change(metric, {
    target: { value: within(metric).getByRole('option', { name: label }).getAttribute('value') },
  })
  await waitFor(() => expect(metric).toHaveDisplayValue(label))
}

describe('InsightsLabTrends', () => {
  it('compares greedy and exhaustive raw runs and renders a timeout as a right-censored lower bound', () => {
    const greedyOneThousand = performanceRun({
      runNumber: 21,
      implementation: 'greedy',
      computeMilliseconds: 12,
    })
    const exhaustiveOneThousand = performanceRun({
      runNumber: 22,
      implementation: 'time-bounded-exhaustive',
      computeMilliseconds: 1_240,
    })
    exhaustiveOneThousand.outcome = { status: 'completed', proofStatus: 'proven' }
    exhaustiveOneThousand.details = {
      totalCandidateCount: 20,
      subsetEvaluations: 1_048_576,
      largestCardinalityFullyExhausted: 20,
      totalPossibleSubsets: '1048576',
      proofStatus: 'proven',
      thresholdReached: false,
    }
    const greedyTenThousand = performanceRun({
      runNumber: 23,
      implementation: 'greedy',
      slug: 'stress-balanced-10k',
      nodeCount: 10_000,
      computeMilliseconds: 18,
    })
    const stoppedExhaustive = performanceRun({
      runNumber: 24,
      implementation: 'time-bounded-exhaustive',
      slug: 'stress-balanced-10k',
      nodeCount: 10_000,
      computeMilliseconds: 120_080,
      status: 'timedOut',
    })
    stoppedExhaustive.outcome = { status: 'timedOut', proofStatus: 'notProven' }
    stoppedExhaustive.invocation = { parameters: { timeBudgetMilliseconds: 120_000 } }
    stoppedExhaustive.details = {
      totalCandidateCount: 33,
      subsetEvaluations: 1_234_567,
      largestCardinalityFullyExhausted: 8,
      activeCardinality: 9,
      subsetEvaluationsAtActiveCardinality: 234_567,
      totalSubsetsAtActiveCardinality: '1307504',
      totalPossibleSubsets: '8589934592',
      stopReason: 'timeBudget',
      proofStatus: 'notProven',
    }

    render(
      <InsightsLabTrends
        benchmarkSets={benchmarkSets}
        runs={[
          greedyOneThousand,
          exhaustiveOneThousand,
          greedyTenThousand,
          stoppedExhaustive,
        ]}
      />,
    )

    expect(screen.getByRole('radio', { name: 'Compare counter-set solvers' })).toBeChecked()
    expect(screen.getByRole('radio', { name: 'Logarithmic' })).toBeChecked()
    expect(screen.queryByRole('combobox', { name: 'Algorithm' })).not.toBeInTheDocument()
    expect(screen.getByText('Compute time', { selector: '.insights-lab-trends__static-value strong' })).toBeInTheDocument()

    const chart = screen.getByRole('img', { name: /Greedy vs time-bounded exhaustive search/ })
    expect(within(screen.getByRole('list', { name: 'Chart series' })).getByText('Greedy')).toBeInTheDocument()
    expect(within(screen.getByRole('list', { name: 'Chart series' })).getByText('Time-bounded exhaustive')).toBeInTheDocument()
    expect(within(chart).getByText(/Time-bounded exhaustive, 10,000 nodes: 2:00 · stopped; minimum not proven, run #24/, { selector: 'title' })).toBeInTheDocument()
    expect(chart.querySelectorAll('.insights-lab-trends__budget-line')).toHaveLength(1)
    expect(chart.querySelectorAll('.insights-lab-trends__censored-marker')).toHaveLength(1)
    expect(chart.querySelectorAll('.insights-lab-trends__series-line')).toHaveLength(1)
    expect(within(chart).getByText('2:00 · stopped', { selector: 'text' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'View data' }))
    const table = screen.getByRole('table', {
      name: 'Raw greedy and exhaustive observations plotted above',
    })
    expect(screen.getByRole('region', { name: 'Trend data' })).toHaveAttribute('tabindex', '0')
    const stoppedRow = within(table).getByRole('row', {
      name: /Time-bounded exhaustive 10,000 2:00 · stopped Not established \(stopped\)/,
    })
    expect(within(table).getByRole('row', {
      name: /Time-bounded exhaustive 1,000 .*No qualifying set exists \(proven\)/,
    })).toBeInTheDocument()
    expect(stoppedRow).toHaveTextContent('33')
    expect(stoppedRow).toHaveTextContent('1,234,567')
    expect(stoppedRow).toHaveTextContent('fully exhausted through size 8; size 9: 234,567 of 1,307,504')
    expect(stoppedRow).toHaveTextContent(/0\.014\d*%/)
    expect(stoppedRow).toHaveTextContent('#24')

    fireEvent.click(screen.getByRole('button', { name: 'How to read' }))
    const guide = screen.getByRole('region', { name: 'How to read this chart' })
    expect(guide).toHaveTextContent(/Greedy quickly finds a usable counter set.*does not prove/i)
    expect(guide).toHaveTextContent(/current reference implementation.*not a claim/i)
    expect(guide).toHaveTextContent(/right-censored/i)
    expect(guide).toHaveTextContent(/% of maximum subset space/i)
    expect(guide).toHaveTextContent(/not a completion estimate/i)
  })

  it('does not relabel candidate-capped legacy runs as the exhaustive reference', () => {
    render(
      <InsightsLabTrends
        benchmarkSets={benchmarkSets}
        runs={[
          performanceRun({ runNumber: 1, implementation: 'greedy' }),
          performanceRun({ runNumber: 2, implementation: 'bounded-brute-force' }),
        ]}
      />,
    )

    expect(screen.getByRole('radio', { name: 'Compare counter-set solvers' })).toBeDisabled()
    const algorithm = screen.getByRole('combobox', { name: 'Algorithm' })
    expect(within(algorithm).getByRole('option', {
      name: 'Legacy bounded minimal counter set',
    })).toBeInTheDocument()
  })

  it('surfaces duplicate solver observations without averaging them', () => {
    render(
      <InsightsLabTrends
        benchmarkSets={benchmarkSets}
        runs={[
          performanceRun({ runNumber: 41, implementation: 'greedy', computeMilliseconds: 10 }),
          performanceRun({ runNumber: 42, implementation: 'greedy', computeMilliseconds: 20 }),
          performanceRun({
            runNumber: 43,
            implementation: 'time-bounded-exhaustive',
            computeMilliseconds: 100,
          }),
        ]}
      />,
    )

    expect(screen.getByRole('status')).toHaveTextContent(/1 solver\/size selection has duplicate runs/i)
    const chart = screen.getByRole('img')
    expect(within(chart).getByText(/Greedy, 1,000 nodes: 10 ms, raw run #41/, { selector: 'title' })).toBeInTheDocument()
    expect(within(chart).getByText(/Greedy, 1,000 nodes: 20 ms, raw run #42/, { selector: 'title' })).toBeInTheDocument()
    expect(chart.textContent).not.toContain('median')

    fireEvent.click(screen.getByRole('button', { name: 'View data' }))
    const table = screen.getByRole('table', {
      name: 'Raw greedy and exhaustive observations plotted above',
    })
    expect(within(table).getAllByRole('row')).toHaveLength(4)
    expect(table).toHaveTextContent('#41')
    expect(table).toHaveTextContent('#42')
    expect(table).toHaveTextContent('#43')
  })

  it('keeps greedy and bounded algorithms separate and aggregates repeated runs with a median', async () => {
    render(<InsightsLabTrends benchmarkSets={benchmarkSets} runs={representativeRuns} />)

    await selectMinimalCounterSet()
    const algorithm = screen.getByRole('combobox', { name: 'Algorithm' })
    expect(within(algorithm).getByRole('option', { name: 'Minimal counter set' })).toBeInTheDocument()
    expect(within(algorithm).getByRole('option', { name: 'Time-bounded exhaustive search' })).toBeInTheDocument()

    expect(await screen.findByRole('img', { name: /Minimal counter set by graph shape/ })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'View data' }))
    const table = screen.getByRole('table', { name: 'Compute-time values plotted above' })
    const rows = within(table).getAllByRole('row')
    expect(rows.some((row) => /15 ms median/.test(row.textContent ?? '') && /#1, #2/.test(row.textContent ?? ''))).toBe(true)
    expect(rows.some((row) => /40 ms/.test(row.textContent ?? '') && /#3/.test(row.textContent ?? ''))).toBe(true)
    expect(table).toHaveTextContent('#4')
    expect(table).not.toHaveTextContent('#7')
    expect(table).not.toHaveTextContent('#8')
    expect(table).not.toHaveTextContent('#9')
    expect(table).not.toHaveTextContent('#10')
    expect(table).not.toHaveTextContent('#11')
  })

  it('offers subset evaluations only for bounded runs and supports logarithmic counts', async () => {
    render(
      <InsightsLabTrends
        benchmarkSets={benchmarkSets}
        runs={[
          performanceRun({
            runNumber: 1,
            implementation: 'time-bounded-exhaustive',
            subsetEvaluations: 1_048_576,
          }),
          performanceRun({
            runNumber: 2,
            implementation: 'time-bounded-exhaustive',
            subsetEvaluations: 4_194_304,
          }),
          performanceRun({
            runNumber: 3,
            implementation: 'greedy',
            subsetEvaluations: 7,
          }),
        ]}
      />,
    )

    await selectBoundedMinimalCounterSet()
    const metric = screen.getByRole('combobox', { name: 'Metric' })
    expect(within(metric).getByRole('option', { name: 'Subset evaluations' })).toBeInTheDocument()

    await selectMetric('Subset evaluations')
    expect(screen.getByText(/Subset evaluations \(count\).*logarithmic/, { selector: 'text' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('radio', { name: 'Logarithmic' }))
    expect(screen.getByRole('img')).toHaveAccessibleName(/subset evaluations.*logarithmic y-axis/i)
    expect(screen.getByRole('img').outerHTML).not.toMatch(/NaN|Infinity/)

    fireEvent.click(screen.getByRole('button', { name: 'View data' }))
    const table = screen.getByRole('table', {
      name: /Subset evaluations values plotted above/,
    })
    expect(table).toHaveTextContent('2,621,440 evaluations median')
    expect(table).toHaveTextContent('#1, #2')
    expect(table).not.toHaveTextContent('#3')

    await selectMinimalCounterSet()
    expect(screen.getByRole('combobox', { name: 'Metric' })).toHaveDisplayValue('Compute time')
    expect(screen.queryByRole('option', { name: 'Subset evaluations' })).not.toBeInTheDocument()
  })

  it('shows stopped-run work metrics as partial observed values rather than time limits', async () => {
    const stopped = performanceRun({
      runNumber: 31,
      implementation: 'time-bounded-exhaustive',
      computeMilliseconds: 120_050,
      operationMilliseconds: 120_200,
      cpuMilliseconds: 118_400,
      allocatedBytes: 1_048_576,
      subsetEvaluations: 2_000_000,
      status: 'timedOut',
    })
    stopped.invocation = { parameters: { timeBudgetMilliseconds: 120_000 } }
    stopped.details = { subsetEvaluations: 2_000_000, stopReason: 'timeBudget' }

    render(
      <InsightsLabTrends
        benchmarkSets={benchmarkSets}
        runs={[
          performanceRun({ runNumber: 30, implementation: 'greedy' }),
          stopped,
        ]}
      />,
    )

    await selectBoundedMinimalCounterSet()
    await selectMetric('Managed allocations')

    let chart = screen.getByRole('img')
    expect(chart.querySelector('.insights-lab-trends__budget-line')).not.toBeInTheDocument()
    expect(chart.querySelector('.insights-lab-trends__censored-marker')).not.toBeInTheDocument()
    expect(chart.querySelector('.insights-lab-trends__stopped-marker')).toBeInTheDocument()
    expect(within(chart).getByText(/1 MiB · at stop/, { selector: 'title' })).toBeInTheDocument()
    expect(screen.getByText('Partial value from a stopped run')).toBeInTheDocument()

    await selectMetric('Subset evaluations')
    chart = screen.getByRole('img')
    expect(within(chart).getByText(/2,000,000 evaluations · at stop/, { selector: 'title' })).toBeInTheDocument()
    expect(chart.textContent).not.toContain('> 2:00')
  })

  it('switches among recorded metrics and aggregates repeated runs using the selected metric', async () => {
    render(
      <InsightsLabTrends
        benchmarkSets={benchmarkSets}
        runs={[
          performanceRun({
            runNumber: 1,
            computeMilliseconds: 10,
            operationMilliseconds: 100,
            cpuMilliseconds: 6,
            allocatedBytes: 1_048_576,
          }),
          performanceRun({
            runNumber: 2,
            computeMilliseconds: 20,
            operationMilliseconds: 300,
            cpuMilliseconds: 14,
            allocatedBytes: 3_145_728,
          }),
        ]}
      />,
    )

    const metric = await screen.findByRole('combobox', { name: 'Metric' })
    expect(metric).toHaveDisplayValue('Compute time')
    expect(within(metric).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Compute time',
      'Total operation time',
      'CPU time',
      'Managed allocations',
    ])

    fireEvent.click(screen.getByRole('button', { name: 'View data' }))
    expect(screen.getByRole('columnheader', { name: 'Compute time' })).toBeInTheDocument()
    expect(screen.getByRole('table')).toHaveTextContent('15 ms median')

    await selectMetric('Total operation time')
    expect(screen.getByRole('img')).toHaveAccessibleName(/total operation time/i)
    expect(screen.getByText(/Total operation time \(ms\).*logarithmic/, { selector: 'text' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Total operation time' })).toBeInTheDocument()
    expect(screen.getByRole('table')).toHaveTextContent('200 ms median')

    await selectMetric('CPU time')
    expect(screen.getByRole('img')).toHaveAccessibleName(/cpu time/i)
    expect(screen.getByText(/CPU time \(ms\).*logarithmic/, { selector: 'text' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'CPU time' })).toBeInTheDocument()
    expect(screen.getByRole('table')).toHaveTextContent('10 ms median')

    await selectMetric('Managed allocations')
    expect(screen.getByRole('img')).toHaveAccessibleName(/managed allocations/i)
    expect(screen.getByRole('columnheader', { name: 'Managed allocations' })).toBeInTheDocument()
    expect(screen.getByRole('table')).toHaveTextContent(/2(?:\.00)? MiB median/)
  })

  it('offers linear and logarithmic y-axis scales and exposes the selected scale to chart readers', async () => {
    render(<InsightsLabTrends benchmarkSets={benchmarkSets} runs={representativeRuns} />)
    await selectMinimalCounterSet()

    const scale = screen.getByRole('radiogroup', { name: 'Y-axis scale' })
    const linear = within(scale).getByRole('radio', { name: 'Linear' })
    const logarithmic = within(scale).getByRole('radio', { name: 'Logarithmic' })
    expect(logarithmic).toBeChecked()
    expect(linear).not.toBeChecked()
    expect(screen.getByRole('img')).toHaveAccessibleName(/logarithmic y-axis/i)
    expect(screen.getByText(/Compute time \(ms\).*logarithmic/i, { selector: 'text' })).toBeInTheDocument()

    fireEvent.click(linear)

    expect(linear).toBeChecked()
    expect(logarithmic).not.toBeChecked()
    expect(screen.getByRole('img')).toHaveAccessibleName(/linear y-axis/i)
    expect(screen.getByText('Compute time (ms)', { selector: 'text' })).toBeInTheDocument()
  })

  it('keeps zero, sub-millisecond, and positive points finite on a logarithmic y-axis', () => {
    render(
      <InsightsLabTrends
        benchmarkSets={benchmarkSets}
        runs={[
          performanceRun({ runNumber: 1, computeMilliseconds: 0 }),
          performanceRun({
            runNumber: 2,
            slug: 'stress-balanced-10k',
            nodeCount: 10_000,
            computeMilliseconds: 0.25,
          }),
          performanceRun({
            runNumber: 3,
            slug: 'stress-balanced-100k',
            nodeCount: 100_000,
            computeMilliseconds: 10,
          }),
        ]}
      />,
    )

    fireEvent.click(screen.getByRole('radio', { name: 'Logarithmic' }))

    const chart = screen.getByRole('img')
    expect(chart.outerHTML).not.toMatch(/NaN|Infinity/)
    expect(within(chart).getByText('Balanced tree, 1,000 nodes: 0 ms, raw run #1', { selector: 'title' })).toBeInTheDocument()
    expect(within(chart).getByText('Balanced tree, 10,000 nodes: 0.25 ms, raw run #2', { selector: 'title' })).toBeInTheDocument()
    expect(within(chart).getByText('Balanced tree, 100,000 nodes: 10 ms, raw run #3', { selector: 'title' })).toBeInTheDocument()
    expect(chart.querySelectorAll('circle')).toHaveLength(3)
  })

  it('keeps an all-zero logarithmic view on an explicit zero baseline', () => {
    render(
      <InsightsLabTrends
        benchmarkSets={benchmarkSets}
        runs={[
          performanceRun({ runNumber: 1, computeMilliseconds: 0 }),
          performanceRun({
            runNumber: 2,
            slug: 'stress-balanced-10k',
            nodeCount: 10_000,
            computeMilliseconds: 0,
          }),
        ]}
      />,
    )

    fireEvent.click(screen.getByRole('radio', { name: 'Logarithmic' }))

    const chart = screen.getByRole('img')
    const yTickLabels = [...chart.querySelectorAll('.insights-lab-trends__grid-line')]
      .map((line) => line.parentElement?.querySelector('text')?.textContent)
    expect(yTickLabels).toEqual(['0'])
    expect(chart.outerHTML).not.toMatch(/NaN|Infinity/)
    expect(screen.getByText(/All selected values are zero; logarithmic spacing is not applicable/)).toBeInTheDocument()
  })

  it('excludes only runs missing the selected allocation metric', async () => {
    render(
      <InsightsLabTrends
        benchmarkSets={benchmarkSets}
        runs={[
          performanceRun({ runNumber: 1, allocatedBytes: 1_024 }),
          performanceRun({ runNumber: 2, allocatedBytes: null }),
          performanceRun({ runNumber: 3, allocatedBytes: 3_072 }),
        ]}
      />,
    )

    await selectMetric('Managed allocations')
    fireEvent.click(screen.getByRole('button', { name: 'View data' }))

    const table = screen.getByRole('table', { name: 'Managed allocations values plotted above' })
    expect(table).toHaveTextContent('2 KiB median')
    expect(table).toHaveTextContent('#1, #3')
    expect(table).not.toHaveTextContent('#2')
    expect(within(table).getByRole('cell', { name: '2' })).toBeInTheDocument()
  })

  it('charts early timing aliases for compute and total operation time', async () => {
    const aliasRun = performanceRun({ runNumber: 1 })
    aliasRun.timing = {
      computeElapsedMs: 0.75,
      operationElapsedMs: 12.5,
    }

    render(<InsightsLabTrends benchmarkSets={benchmarkSets} runs={[aliasRun]} />)

    expect(screen.getByRole('img')).toHaveAccessibleName(/compute time/i)
    fireEvent.click(screen.getByRole('button', { name: 'View data' }))
    expect(screen.getByRole('table')).toHaveTextContent('0.75 ms')

    await selectMetric('Total operation time')
    expect(screen.getByRole('img')).toHaveAccessibleName(/total operation time/i)
    expect(screen.getByRole('table')).toHaveTextContent('12.5 ms')
  })

  it('opens an inline chart-reading guide with metric-specific context and closes it', async () => {
    render(<InsightsLabTrends benchmarkSets={benchmarkSets} runs={representativeRuns} />)
    await selectMinimalCounterSet()
    await selectMetric('Managed allocations')

    const howToRead = screen.getByRole('button', { name: 'How to read' })
    expect(howToRead).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('region', { name: 'How to read this chart' })).not.toBeInTheDocument()

    fireEvent.click(howToRead)

    expect(howToRead).toHaveAttribute('aria-expanded', 'true')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    const guide = screen.getByRole('region', { name: 'How to read this chart' })
    expect(guide).toHaveTextContent(/graph size.*logarithmic/i)
    expect(guide).toHaveTextContent(/Y-axis.*selected metric/i)
    expect(guide).toHaveTextContent(/one (?:completed )?run.*raw|raw run.*one/i)
    expect(guide).toHaveTextContent(/repeated.*runs.*median|median.*repeated.*runs/i)
    expect(guide).toHaveTextContent(/managed allocations/i)
    expect(guide).toHaveTextContent(/not retained or peak memory/i)

    fireEvent.keyDown(guide, { key: 'Escape' })
    expect(screen.queryByRole('region', { name: 'How to read this chart' })).not.toBeInTheDocument()
    expect(howToRead).toHaveAttribute('aria-expanded', 'false')
    expect(howToRead).toHaveFocus()

    fireEvent.click(howToRead)
    const reopenedGuide = screen.getByRole('region', { name: 'How to read this chart' })
    fireEvent.click(within(reopenedGuide).getByRole('button', { name: 'Close how to read' }))
    expect(screen.queryByRole('region', { name: 'How to read this chart' })).not.toBeInTheDocument()
    expect(howToRead).toHaveFocus()
  })

  it('compares benchmark sets for one shape and permits graph-size filtering without an empty selection', async () => {
    render(<InsightsLabTrends benchmarkSets={benchmarkSets} runs={representativeRuns} />)
    await selectMinimalCounterSet()

    fireEvent.click(screen.getByRole('radio', { name: 'Compare benchmark sets' }))
    const chart = await screen.findByRole('img', { name: /Minimal counter set across benchmark sets/ })
    expect(chart).toBeInTheDocument()

    const seriesLegend = screen.getByRole('list', { name: 'Chart series' })
    expect(within(seriesLegend).getByText('LL-699 legacy baseline')).toBeInTheDocument()
    expect(within(seriesLegend).getByText('Bayesian rework')).toBeInTheDocument()

    const tenThousand = screen.getByRole('checkbox', { name: '10,000 nodes' })
    fireEvent.click(tenThousand)
    expect(tenThousand).not.toBeChecked()
    const oneThousand = screen.getByRole('checkbox', { name: '1,000 nodes' })
    expect(oneThousand).toBeDisabled()
    expect(screen.getByText(
      /Balanced tree · 1,000 nodes · median compute time/,
      { selector: 'figcaption span' },
    )).toBeInTheDocument()

    const bayesian = screen.getByRole('checkbox', { name: 'Bayesian rework' })
    fireEvent.click(bayesian)
    expect(within(seriesLegend).queryByText('Bayesian rework')).not.toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'LL-699 legacy baseline' })).toBeDisabled()
  })

  it('explains how to create comparable data when no eligible runs exist', () => {
    render(
      <InsightsLabTrends
        benchmarkSets={benchmarkSets}
        runs={[
          performanceRun({ runNumber: 1, benchmarkSetId: null }),
          performanceRun({ runNumber: 2, status: 'failed' }),
          performanceRun({ runNumber: 3, slug: 'sample-medium', shape: 'sample', nodeCount: 27 }),
        ]}
      />,
    )

    expect(screen.getByRole('heading', { name: 'Historical trends' })).toBeInTheDocument()
    expect(screen.getByText(/recorded stress-graph runs assigned to a benchmark set/i)).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('disambiguates benchmark sets that share a display name', () => {
    const duplicateSets: BenchmarkSet[] = [
      { id: '111111aaaaaa', name: 'Bayesian rework', createdAtUtc: '2026-08-17T12:00:00Z' },
      { id: '222222bbbbbb', name: 'Bayesian rework', createdAtUtc: '2026-08-18T12:00:00Z' },
    ]
    render(
      <InsightsLabTrends
        benchmarkSets={duplicateSets}
        runs={[
          performanceRun({ runNumber: 1, benchmarkSetId: duplicateSets[0].id }),
          performanceRun({ runNumber: 2, benchmarkSetId: duplicateSets[1].id }),
        ]}
      />,
    )

    fireEvent.click(screen.getByRole('radio', { name: 'Compare benchmark sets' }))
    const seriesLegend = screen.getByRole('list', { name: 'Chart series' })
    expect(within(seriesLegend).getByText('Bayesian rework · aaaaaa')).toBeInTheDocument()
    expect(within(seriesLegend).getByText('Bayesian rework · bbbbbb')).toBeInTheDocument()
  })
})
