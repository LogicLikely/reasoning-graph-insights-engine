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
    timing: { computeElapsedMilliseconds: computeMilliseconds },
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
