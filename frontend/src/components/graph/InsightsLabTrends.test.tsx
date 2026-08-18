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
    implementation: 'bounded-brute-force',
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
  const algorithm = await screen.findByRole('combobox', { name: 'Algorithm' })
  fireEvent.change(algorithm, {
    target: { value: screen.getByRole('option', { name: 'Minimal counter set' }).getAttribute('value') },
  })
  await waitFor(() => expect(algorithm).toHaveDisplayValue('Minimal counter set'))
}

async function selectMetric(label: string) {
  const metric = await screen.findByRole('combobox', { name: 'Metric' })
  fireEvent.change(metric, {
    target: { value: within(metric).getByRole('option', { name: label }).getAttribute('value') },
  })
  await waitFor(() => expect(metric).toHaveDisplayValue(label))
}

describe('InsightsLabTrends', () => {
  it('keeps greedy and bounded algorithms separate and aggregates repeated runs with a median', async () => {
    render(<InsightsLabTrends benchmarkSets={benchmarkSets} runs={representativeRuns} />)

    const algorithm = await screen.findByRole('combobox', { name: 'Algorithm' })
    expect(within(algorithm).getByRole('option', { name: 'Minimal counter set' })).toBeInTheDocument()
    expect(within(algorithm).getByRole('option', { name: 'Bounded minimal counter set' })).toBeInTheDocument()

    await selectMinimalCounterSet()
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
    expect(screen.getByText('Total operation time (ms)', { selector: 'text' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Total operation time' })).toBeInTheDocument()
    expect(screen.getByRole('table')).toHaveTextContent('200 ms median')

    await selectMetric('CPU time')
    expect(screen.getByRole('img')).toHaveAccessibleName(/cpu time/i)
    expect(screen.getByText('CPU time (ms)', { selector: 'text' })).toBeInTheDocument()
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
    expect(linear).toBeChecked()
    expect(logarithmic).not.toBeChecked()
    expect(screen.getByRole('img')).toHaveAccessibleName(/linear y-axis/i)
    expect(screen.getByText('Compute time (ms)', { selector: 'text' })).toBeInTheDocument()

    fireEvent.click(logarithmic)

    expect(logarithmic).toBeChecked()
    expect(linear).not.toBeChecked()
    expect(screen.getByRole('img')).toHaveAccessibleName(/logarithmic y-axis/i)
    expect(screen.getByText(/Compute time \(ms\).*logarithmic/i, { selector: 'text' })).toBeInTheDocument()
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
    expect(within(chart).getByText('Balanced tree, 1,000 nodes: 0 ms, n=1', { selector: 'title' })).toBeInTheDocument()
    expect(within(chart).getByText('Balanced tree, 10,000 nodes: 0.25 ms, n=1', { selector: 'title' })).toBeInTheDocument()
    expect(within(chart).getByText('Balanced tree, 100,000 nodes: 10 ms, n=1', { selector: 'title' })).toBeInTheDocument()
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
    expect(guide).toHaveTextContent(/one run.*raw|raw run.*one/i)
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
    expect(screen.getByText(/completed stress-graph runs assigned to a benchmark set/i)).toBeInTheDocument()
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
