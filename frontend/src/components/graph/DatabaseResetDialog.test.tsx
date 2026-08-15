import { fireEvent, render, screen, within } from '@testing-library/react'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { STRESS_GRAPH_OPTIONS, type StressGraphId } from '../../services/stressGraphs'
import { DatabaseResetDialog } from './DatabaseResetDialog'

function ResetDialogHarness({
  initialSelectedStressGraphIds = [],
}: {
  initialSelectedStressGraphIds?: StressGraphId[]
}) {
  const [isOpen, setIsOpen] = useState(false)

  return (
    <>
      <button onClick={() => setIsOpen(true)} type="button">Open reset</button>
      <DatabaseResetDialog
        initialSelectedStressGraphIds={initialSelectedStressGraphIds}
        isOpen={isOpen}
        onCancel={() => setIsOpen(false)}
        onConfirm={vi.fn()}
      />
    </>
  )
}

describe('DatabaseResetDialog', () => {
  it('groups all eight optional graphs by scale and marks installed graphs', () => {
    render(
      <DatabaseResetDialog
        initialSelectedStressGraphIds={['stress-wide-1k', 'stress-deep-10k']}
        isOpen
        onCancel={vi.fn()}
        onConfirm={vi.fn()}
      />,
    )

    const dialog = screen.getByRole('dialog', { name: 'Reset database' })
    const oneThousandGroup = within(dialog).getByRole('group', { name: '1K stress graphs' })
    const tenThousandGroup = within(dialog).getByRole('group', { name: '10K stress graphs' })

    expect(within(oneThousandGroup).getAllByRole('checkbox')).toHaveLength(4)
    expect(within(tenThousandGroup).getAllByRole('checkbox')).toHaveLength(4)
    expect(within(dialog).getByRole('checkbox', { name: /Wide star \(1,000 nodes\)/ })).toBeChecked()
    expect(within(dialog).getByRole('checkbox', { name: /Deep chain \(10,000 nodes\)/ })).toBeChecked()
    expect(dialog).toHaveTextContent('standard example graphs are always installed')

    STRESS_GRAPH_OPTIONS.forEach(({ id }) => {
      expect(within(dialog).getByText(id)).toBeInTheDocument()
    })
  })

  it('supports all-graph and scale actions and submits IDs in canonical order', () => {
    const onConfirm = vi.fn()
    render(
      <DatabaseResetDialog
        initialSelectedStressGraphIds={[]}
        isOpen
        onCancel={vi.fn()}
        onConfirm={onConfirm}
      />,
    )

    const dialog = screen.getByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Select all' }))
    expect(within(dialog).getAllByRole('checkbox').every((checkbox) => (
      (checkbox as HTMLInputElement).checked
    ))).toBe(true)

    fireEvent.click(within(dialog).getByRole('button', { name: 'Clear 10K' }))
    expect(within(dialog).getByText('4 of 8 optional graphs selected')).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Select 10K' }))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reset and rebuild database' }))

    expect(onConfirm).toHaveBeenCalledWith(STRESS_GRAPH_OPTIONS.map(({ id }) => id))
  })

  it('discards cancelled draft changes, closes with Escape, and restores trigger focus', () => {
    render(<ResetDialogHarness initialSelectedStressGraphIds={['stress-balanced-1k']} />)

    const trigger = screen.getByRole('button', { name: 'Open reset' })
    trigger.focus()
    fireEvent.click(trigger)

    let dialog = screen.getByRole('dialog')
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus()
    fireEvent.click(within(dialog).getByRole('checkbox', { name: /Balanced tree \(1,000 nodes\)/ }))
    fireEvent.click(within(dialog).getByRole('checkbox', { name: /Wide star \(1,000 nodes\)/ }))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(trigger).toHaveFocus()
    fireEvent.click(trigger)
    dialog = screen.getByRole('dialog')
    expect(within(dialog).getByRole('checkbox', { name: /Balanced tree \(1,000 nodes\)/ })).toBeChecked()
    expect(within(dialog).getByRole('checkbox', { name: /Wide star \(1,000 nodes\)/ })).not.toBeChecked()

    fireEvent.keyDown(dialog, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(trigger).toHaveFocus()
  })

  it('keeps every control disabled and announces errors while submitting', () => {
    render(
      <DatabaseResetDialog
        error="The reset request failed."
        initialSelectedStressGraphIds={[]}
        isOpen
        isSubmitting
        onCancel={vi.fn()}
        onConfirm={vi.fn()}
      />,
    )

    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByRole('alert')).toHaveTextContent('The reset request failed.')
    within(dialog).getAllByRole('checkbox').forEach((checkbox) => {
      expect(checkbox).toBeDisabled()
    })
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toBeDisabled()
    expect(within(dialog).getByRole('button', { name: 'Resetting database…' })).toBeDisabled()
  })
})
