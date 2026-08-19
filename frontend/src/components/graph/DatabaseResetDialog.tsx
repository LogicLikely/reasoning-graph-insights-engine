import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import {
  STRESS_GRAPH_OPTIONS,
  type StressGraphId,
  type StressGraphScale,
} from '../../services/stressGraphs'
import './DatabaseResetDialog.css'

interface DatabaseResetDialogProps {
  isOpen: boolean
  initialSelectedStressGraphIds: readonly StressGraphId[]
  isSubmitting?: boolean
  error?: string | null
  onCancel: () => void
  onConfirm: (stressGraphIds: StressGraphId[]) => void
}

const STRESS_GRAPH_SCALES: readonly StressGraphScale[] = [
  ...new Set(STRESS_GRAPH_OPTIONS.map(({ scale }) => scale)),
]
const FOCUSABLE_SELECTOR = [
  'button:not(:disabled)',
  'input:not(:disabled)',
  'select:not(:disabled)',
  'textarea:not(:disabled)',
  'a[href]',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

type OpenDatabaseResetDialogProps = Omit<DatabaseResetDialogProps, 'isOpen'>

export function DatabaseResetDialog({ isOpen, ...dialogProps }: DatabaseResetDialogProps) {
  return isOpen ? <OpenDatabaseResetDialog {...dialogProps} /> : null
}

function OpenDatabaseResetDialog({
  initialSelectedStressGraphIds,
  isSubmitting = false,
  error = null,
  onCancel,
  onConfirm,
}: OpenDatabaseResetDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null)
  const cancelButtonRef = useRef<HTMLButtonElement>(null)
  const [selectedIds, setSelectedIds] = useState<Set<StressGraphId>>(
    () => new Set(initialSelectedStressGraphIds),
  )

  useEffect(() => {
    const previouslyFocusedElement = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null

    cancelButtonRef.current?.focus()

    return () => {
      previouslyFocusedElement?.focus()
    }
  }, [])

  const updateGroup = (scale: StressGraphScale, shouldSelect: boolean) => {
    setSelectedIds((currentIds) => {
      const nextIds = new Set(currentIds)

      STRESS_GRAPH_OPTIONS
        .filter((option) => option.scale === scale)
        .forEach((option) => {
          if (shouldSelect) {
            nextIds.add(option.id)
          } else {
            nextIds.delete(option.id)
          }
        })

      return nextIds
    })
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault()
      event.stopPropagation()

      if (!isSubmitting) {
        onCancel()
      }
      return
    }

    if (event.key !== 'Tab') {
      return
    }

    const focusableElements = Array.from(
      dialogRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR) ?? [],
    )

    if (focusableElements.length === 0) {
      event.preventDefault()
      return
    }

    const firstElement = focusableElements[0]
    const lastElement = focusableElements[focusableElements.length - 1]

    if (event.shiftKey && document.activeElement === firstElement) {
      event.preventDefault()
      lastElement.focus()
    } else if (!event.shiftKey && document.activeElement === lastElement) {
      event.preventDefault()
      firstElement.focus()
    }
  }

  const selectedStressGraphIds = STRESS_GRAPH_OPTIONS
    .filter(({ id }) => selectedIds.has(id))
    .map(({ id }) => id)

  return (
    <div
      className="database-reset-dialog__backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !isSubmitting) {
          onCancel()
        }
      }}
    >
      <div
        aria-describedby="database-reset-description database-reset-warning"
        aria-labelledby="database-reset-title"
        aria-modal="true"
        className="database-reset-dialog"
        onKeyDown={handleKeyDown}
        ref={dialogRef}
        role="dialog"
      >
        <header className="database-reset-dialog__header">
          <span className="eyebrow">Database maintenance</span>
          <h2 id="database-reset-title">Reset database</h2>
          <p id="database-reset-description">
            Choose which optional stress graphs to install. The standard example graphs are always installed.
          </p>
        </header>

        <p className="database-reset-dialog__warning" id="database-reset-warning">
          This deletes all current graph data and rebuilds the database. This action cannot be undone.
        </p>

        <div className="database-reset-dialog__bulk-actions" aria-label="All stress graphs">
          <span>{selectedIds.size} of {STRESS_GRAPH_OPTIONS.length} optional graphs selected</span>
          <div>
            <button
              disabled={isSubmitting || selectedIds.size === STRESS_GRAPH_OPTIONS.length}
              onClick={() => setSelectedIds(new Set(STRESS_GRAPH_OPTIONS.map(({ id }) => id)))}
              type="button"
            >
              Select all
            </button>
            <button
              disabled={isSubmitting || selectedIds.size === 0}
              onClick={() => setSelectedIds(new Set())}
              type="button"
            >
              Clear all
            </button>
          </div>
        </div>

        <form
          className="database-reset-dialog__form"
          onSubmit={(event) => {
            event.preventDefault()
            onConfirm(selectedStressGraphIds)
          }}
        >
          <div className="database-reset-dialog__groups">
            {STRESS_GRAPH_SCALES.map((scale) => {
              const options = STRESS_GRAPH_OPTIONS.filter((option) => option.scale === scale)
              const selectedGroupCount = options.filter(({ id }) => selectedIds.has(id)).length
              const scaleLabel = scale === '100' ? '100-node' : scale

              return (
                <fieldset disabled={isSubmitting} key={scale}>
                  <legend>{scaleLabel} stress graphs</legend>
                  <div className="database-reset-dialog__group-actions">
                    <button
                      disabled={selectedGroupCount === options.length}
                      onClick={() => updateGroup(scale, true)}
                      type="button"
                    >
                      Select {scaleLabel}
                    </button>
                    <button
                      disabled={selectedGroupCount === 0}
                      onClick={() => updateGroup(scale, false)}
                      type="button"
                    >
                      Clear {scaleLabel}
                    </button>
                  </div>
                  <div className="database-reset-dialog__options">
                    {options.map((option) => (
                      <label className="database-reset-dialog__option" key={option.id}>
                        <input
                          checked={selectedIds.has(option.id)}
                          onChange={(event) => {
                            const shouldSelect = event.target.checked
                            setSelectedIds((currentIds) => {
                              const nextIds = new Set(currentIds)

                              if (shouldSelect) {
                                nextIds.add(option.id)
                              } else {
                                nextIds.delete(option.id)
                              }

                              return nextIds
                            })
                          }}
                          type="checkbox"
                        />
                        <span>
                          <strong>{option.label}</strong>
                          <code>{option.id}</code>
                        </span>
                      </label>
                    ))}
                  </div>
                </fieldset>
              )
            })}
          </div>

          {error ? (
            <p className="database-reset-dialog__error" role="alert">{error}</p>
          ) : null}

          <div className="database-reset-dialog__footer">
            <button
              className="database-reset-dialog__cancel"
              disabled={isSubmitting}
              onClick={onCancel}
              ref={cancelButtonRef}
              type="button"
            >
              Cancel
            </button>
            <button
              className="database-reset-dialog__confirm"
              disabled={isSubmitting}
              type="submit"
            >
              {isSubmitting ? 'Resetting database…' : 'Reset and rebuild database'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
