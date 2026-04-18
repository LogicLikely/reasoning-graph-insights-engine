import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { sampleGraph } from '../../fixtures/sampleGraph'
import { GraphDetailsPanel } from './GraphDetailsPanel'

describe('GraphDetailsPanel', () => {
  it('shows an empty state when no node is selected', () => {
    render(<GraphDetailsPanel />)

    expect(screen.getByTestId('graph-details-panel')).toBeInTheDocument()
    expect(screen.getByText(/select a node/i)).toBeInTheDocument()
  })

  it('renders rich details for the selected node', () => {
    render(<GraphDetailsPanel node={sampleGraph.nodes.find((node) => node.id === 'E1')} />)

    expect(screen.getByText('Photographs from beaches')).toBeInTheDocument()
    expect(screen.getAllByText(/observational/i).length).toBeGreaterThan(0)
    expect(screen.getByText(/score:/i)).toBeInTheDocument()
  })
})
