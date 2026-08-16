import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import {
  BoundedAnalysisResult,
  MAX_RENDERED_PATH_NODE_IDS,
  MAX_RENDERED_PATHS,
  MAX_RENDERED_RESULT_ITEMS,
  MAX_RENDERED_VALUE_CHARACTERS,
} from './BoundedAnalysisResult'

describe('BoundedAnalysisResult', () => {
  it('keeps total identity while bounding rows, paths, and very long textual paths', () => {
    render(
      <BoundedAnalysisResult
        payload={{
          operationId: 'strongest-path-v1',
          status: 'succeeded',
          totalResultCardinality: 250,
          resultDigest: 'sha256:complete-result',
          summary: { resultCount: 250 },
          items: Array.from({ length: 125 }, (_, index) => ({
            nodeId: `n-${index}`,
            rank: index + 1,
          })),
          orderedPaths: Array.from({ length: 25 }, (_, index) => ({
            pathId: `path-${index}`,
            nodeIds: Array.from(
              { length: MAX_RENDERED_PATH_NODE_IDS + 2 },
              (__, nodeIndex) => `n-${nodeIndex}`,
            ),
          })),
        }}
      />,
    )

    expect(screen.getByTestId('result-total-cardinality')).toHaveTextContent('250')
    expect(screen.getByText('sha256:complete-result')).toBeVisible()
    expect(screen.getByTestId('bounded-result-item-count')).toHaveTextContent(
      `Rendering ${MAX_RENDERED_RESULT_ITEMS} of 250 result items.`,
    )
    expect(screen.getByTestId('bounded-result-path-count')).toHaveTextContent(
      `Rendering ${MAX_RENDERED_PATHS} of 25 ordered paths.`,
    )
    expect(screen.getAllByText(/2 more nodes/)).toHaveLength(MAX_RENDERED_PATHS)
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(
      MAX_RENDERED_RESULT_ITEMS + 1,
    )
  })

  it('bounds structured result cells and fallback distributions with visible truncation', () => {
    const nodeIds = Array.from({ length: 10_000 }, (_, index) => `deep-node-${index}`)
    render(
      <BoundedAnalysisResult
        payload={{
          operationId: 'robustness-v0',
          status: 'succeeded',
          totalResultCardinality: 1,
          resultDigest: 'sha256:complete-deep-result',
          distribution: { buckets: nodeIds },
          items: [{ nodeId: 'deep-node-0', pathNodeIds: nodeIds }],
        }}
      />,
    )

    const structuredCell = screen.getByRole('cell', { name: /9992 more items/ })
    const distribution = screen
      .getAllByText(/9992 more items/)
      .find((element) => element.tagName === 'PRE')
    expect(structuredCell.textContent?.length).toBeLessThanOrEqual(
      MAX_RENDERED_VALUE_CHARACTERS,
    )
    expect(distribution?.textContent?.length).toBeLessThanOrEqual(
      MAX_RENDERED_VALUE_CHARACTERS,
    )
    expect(screen.queryByText(/deep-node-9999/)).not.toBeInTheDocument()
    expect(screen.getByText('sha256:complete-deep-result')).toBeVisible()
  })

  it.each(['failed', 'timed-out', 'cancelled', 'crashed', 'skipped'])(
    'renders the %s terminal state without requiring graph canvas output',
    (status) => {
      render(
        <BoundedAnalysisResult
          payload={{
            operationId: 'critical-counter-v1',
            status,
            totalResultCardinality: 0,
            failure: { kind: status, message: `${status} fixture` },
          }}
        />,
      )

      expect(screen.getByTestId('bounded-analysis-result')).toHaveAttribute(
        'data-result-status',
        status,
      )
      expect(screen.getByText(`${status} fixture`)).toBeVisible()
    },
  )
})
