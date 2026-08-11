import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { sampleGraph } from '../../fixtures/sampleGraph'
import { GraphDetailsPanel } from './GraphDetailsPanel'

describe('GraphDetailsPanel', () => {
  it('shows an empty state when no node is selected', () => {
    render(<GraphDetailsPanel />)

    expect(screen.getByTestId('graph-details-panel')).toBeInTheDocument()
    expect(screen.getByText(/select a node/i)).toBeInTheDocument()
  })

  it('renders rich details for the selected node', () => {
    render(
      <GraphDetailsPanel
        node={sampleGraph.nodes.find((node) => node.id === 'E1')}
        nodes={sampleGraph.nodes}
        edges={sampleGraph.edges}
      />,
    )

    expect(screen.getByText('Photographs from beaches')).toBeInTheDocument()
    expect(screen.getAllByText(/observational/i).length).toBeGreaterThan(0)
    expect(screen.getAllByText(/52\.00/)).toHaveLength(2)
    expect(screen.getByText(/This node supports/i)).toBeInTheDocument()
    expect(screen.getByText('The horizon looks flat')).toBeInTheDocument()
    expect(screen.getByText('Importance to that claim: 5/10')).toBeInTheDocument()
  })

  it('edits likelihood as a percent and parent edge importance', () => {
    const onUpdate = vi.fn()
    const onUpdateEdge = vi.fn()
    const onAddParentEdge = vi.fn()
    const node = sampleGraph.nodes.find((graphNode) => graphNode.id === 'E1')

    render(
      <GraphDetailsPanel
        node={node}
        nodes={sampleGraph.nodes}
        edges={sampleGraph.edges}
        onUpdate={onUpdate}
        onUpdateEdge={onUpdateEdge}
        onAddParentEdge={onAddParentEdge}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: /edit this node's title, type, likelihood, and description/i }))

    expect(screen.getByRole('textbox', { name: 'Type' })).toHaveTextContent('evidence')
    expect(screen.queryByRole('combobox', { name: 'Type' })).not.toBeInTheDocument()

    const likelihoodInput = screen.getByLabelText('Likelihood')
    expect(likelihoodInput).toHaveValue(52)

    fireEvent.change(likelihoodInput, { target: { value: '24.5' } })
    fireEvent.change(screen.getByLabelText('Importance to that claim', { selector: '#edge-importance-E-C1-E1' }), { target: { value: '7' } })
    expect(screen.queryByLabelText('Relation')).not.toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Additional Parent Support'), { target: { value: 'C2' } })
    fireEvent.change(screen.getByLabelText('Importance to that claim', { selector: '#new-parent-importance' }), { target: { value: '3' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    const submittedData = onUpdate.mock.calls[0][1]
    expect(submittedData.kind).toBeUndefined()
    expect(submittedData.priorOdds).toBeCloseTo(Math.log(0.245 / 0.755), 5)
    expect(onUpdateEdge).toHaveBeenCalledWith('E-C1-E1', { importanceToParent: 7 })
    expect(onAddParentEdge).toHaveBeenCalledWith({
      from: 'E1',
      to: 'C2',
      kind: 'support',
      importanceToParent: 3,
    })
  })

  it('adds supporting nodes with likelihood converted to log odds', () => {
    const onAddSupporting = vi.fn()
    const node = sampleGraph.nodes.find((graphNode) => graphNode.id === 'E1')

    render(<GraphDetailsPanel node={node} onAddSupporting={onAddSupporting} />)

    fireEvent.click(screen.getByRole('button', { name: 'Add a child node connected to this selected node' }))
    expect(screen.getByRole('combobox', { name: 'Type' })).toHaveValue('claim')
    fireEvent.change(screen.getByRole('combobox', { name: 'Type' }), { target: { value: 'objection' } })
    expect(screen.queryByLabelText('Relation')).not.toBeInTheDocument()
    expect(screen.getByText('Relation to selected node: Counter')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Importance to that claim'), { target: { value: '4' } })
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'New support' } })
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'A new supporting node.' } })
    fireEvent.change(screen.getByLabelText('Likelihood'), { target: { value: '65' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create Node' }))

    const submittedData = onAddSupporting.mock.calls[0][1]
    expect(onAddSupporting.mock.calls[0][0]).toBe('E1')
    expect(submittedData.kind).toBe('objection')
    expect(submittedData.priorOdds).toBeCloseTo(Math.log(0.65 / 0.35), 5)
    expect(onAddSupporting.mock.calls[0][2]).toEqual({
      kind: 'rebut',
      importanceToParent: 4,
    })
  })
})
