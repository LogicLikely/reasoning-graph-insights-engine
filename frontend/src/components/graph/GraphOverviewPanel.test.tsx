import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { GraphOverviewPanel } from './GraphOverviewPanel'

describe('GraphOverviewPanel', () => {
  it('renders graph stats in a separate overview panel', () => {
    render(
      <GraphOverviewPanel
        title="Sample Reasoning Graph"
        description="A local fixture description."
        nodeCount={10}
        edgeCount={9}
        fixtureName="sample-medium"
      />,
    )

    expect(screen.getByTestId('graph-overview-panel')).toBeInTheDocument()
    expect(screen.getByText('Graph Overview')).toBeInTheDocument()
    expect(screen.getByText('Sample Reasoning Graph')).toBeInTheDocument()
    expect(screen.getByText('A local fixture description.')).toBeInTheDocument()
    expect(screen.getByText('10')).toBeInTheDocument()
    expect(screen.getByText('9')).toBeInTheDocument()
    expect(screen.getByText('sample-medium')).toBeInTheDocument()
  })
})
