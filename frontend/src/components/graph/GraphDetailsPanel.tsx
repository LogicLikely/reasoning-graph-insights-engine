import { useEffect, useState } from 'react'
import type { GraphFixtureNode } from '../../fixtures/sampleGraph'
import './GraphDetailsPanel.css'

interface GraphDetailsPanelProps {
  node?: GraphFixtureNode
  onDelete?: (id: string) => void
  onAddSupporting?: (parentId: string, data: Partial<GraphFixtureNode>) => void
  onUpdate?: (id: string, data: Partial<GraphFixtureNode>) => void
}

type PanelMode = 'view' | 'add' | 'edit'

function formatMetric(value?: number) {
  if (value === null || value === undefined) {
    return undefined; // Or an empty string, or a default value like '0.00'
  }
  return value.toFixed(2);
}
export function GraphDetailsPanel({ node, onDelete, onAddSupporting, onUpdate }: GraphDetailsPanelProps) {
  const [mode, setMode] = useState<PanelMode>('view')
  const [formData, setFormData] = useState({
    title: '',
    bodyText: '',
    kind: 'premise' as GraphFixtureNode['kind'],
    confidence: 0.5
  })

  useEffect(() => {
    setMode('view')
  }, [node?.id])

  if (!node) {
    return (
      <aside className="graph-details-panel" data-testid="graph-details-panel">
        <span className="eyebrow">Node Details</span>
        <div className="graph-details-panel__empty">
          <h3>Select a node to view details</h3>
          <p>
            Click any node in the graph to inspect its role, supporting text, and
            any attached metadata in this panel.
          </p>
        </div>
      </aside>
    )
  }

  const isFormValid =
    (formData.title?.trim() ?? '').length > 0 &&
    (formData.bodyText?.trim() ?? '').length > 0

  const handleSave = () => {
    if (!isFormValid) return

    const submissionData = {
      ...formData,
      title: (formData.title ?? '').trim(),
      bodyText: (formData.bodyText ?? '').trim()
    }

    if (mode === 'add') {
      onAddSupporting?.(node.id, {
        ...submissionData,
        tags: ['dynamic']
      })
    } else if (mode === 'edit') {
      onUpdate?.(node.id, submissionData)
    }
    setMode('view')
    setFormData({ title: '', bodyText: '', kind: 'premise', confidence: 0.5 })
  }

  const enterAddMode = () => {
    setFormData({ title: '', bodyText: '', kind: 'premise', confidence: 0.5 })
    setMode('add')
  }

  const enterEditMode = () => {
    setFormData({
      title: node.title ?? '',
      bodyText: node.bodyText ?? '',
      kind: node.kind,
      confidence: node.confidence ?? 0.5
    })
    setMode('edit')
  }

  if (mode !== 'view') {
    const isEdit = mode === 'edit'

    return (
      <div className="graph-details-panel graph-details-panel--form" data-testid="graph-details-panel">
        <header className="node-header">
          {isEdit ? (
            <>
              <span className="eyebrow">Editing node {node.id}</span>
              <h3>Modify Node</h3>
            </>
          ) : (
            <>
              <span className="eyebrow">Adding support to {node.id}</span>
              <h3>New Node Details</h3>
            </>
          )}
        </header>

        <div className="node-form">
          <div className="form-group">
            <label htmlFor="node-title">Title</label>
            <input
              id="node-title"
              className="form-input"
              value={formData.title}
              onChange={(e) => setFormData({ ...formData, title: e.target.value })}
              placeholder="Enter title..."
            />
          </div>

          <div className="form-group">
            <label htmlFor="node-kind">Type</label>
            <select
              id="node-kind"
              className="form-input"
              value={formData.kind}
              onChange={(e) => setFormData({ ...formData, kind: e.target.value as any })}
            >
              <option value="premise">Premise</option>
              <option value="evidence">Evidence</option>
              <option value="counter">Counter</option>
            </select>
          </div>

          <div className="form-group">
            <label htmlFor="node-body">Description</label>
            <textarea
              id="node-body"
              className="form-input form-input--textarea"
              value={formData.bodyText}
              onChange={(e) => setFormData({ ...formData, bodyText: e.target.value })}
            />
          </div>
        </div>

        <div className="actions-button-group form-actions">
          <button
            className="btn btn--primary"
            onClick={handleSave}
            disabled={!isFormValid}
          >
            {isEdit ? 'Save Changes' : 'Create Node'}
          </button>
          <button className="btn btn--secondary" onClick={() => setMode('view')}>Cancel</button>
        </div>
      </div>
    )
  }

  return (
    <aside className="graph-details-panel" data-testid="graph-details-panel">
      <span className="eyebrow">Node Details</span>

      <div className="graph-details-panel__header">
        <h3>{node.title}</h3>
        <div className="graph-details-panel__meta">
          <span>{node.kind}</span>
          <span>{node.id}</span>
        </div>
      </div>

      <p className="graph-details-panel__body">{node.bodyText}</p>

      <dl className="graph-details-list">
        {node.category ? (
          <>
            <dt>Category</dt>
            <dd>{node.category}</dd>
          </>
        ) : null}
        {node.tags?.length ? (
          <>
            <dt>Tags</dt>
            <dd>
              <div className="graph-tag-list">
                {node.tags.map((tag) => (
                  <span key={tag} className="graph-tag">
                    {tag}
                  </span>
                ))}
              </div>
            </dd>
          </>
        ) : null}
        {formatMetric(node.prior) ? (
          <>
            <dt>Prior</dt>
            <dd>{formatMetric(node.prior)}</dd>
          </>
        ) : null}
        {formatMetric(node.confidence) ? (
          <>
            <dt>Confidence</dt>
            <dd>{formatMetric(node.confidence)}</dd>
          </>
        ) : null}
        {formatMetric(node.weight) ? (
          <>
            <dt>Weight</dt>
            <dd>{formatMetric(node.weight)}</dd>
          </>
        ) : null}
        {formatMetric(node.importance) ? (
          <>
            <dt>Importance</dt>
            <dd>{formatMetric(node.importance)}</dd>
          </>
        ) : null}
        {node.evidence ? (
          <>
            <dt>Evidence</dt>
            <dd className="graph-evidence-block">
              <strong>{node.evidence.type}</strong>
              {node.evidence.score !== undefined ? (
                <span>Score: {node.evidence.score.toFixed(2)}</span>
              ) : null}
              {node.evidence.rationale ? <p>{node.evidence.rationale}</p> : null}
            </dd>
          </>
        ) : null}
      </dl>

      <section className="node-section node-actions">
        <h4>Node Actions</h4>
        <div className="actions-button-group">
          <button
            className="btn btn--primary"
            aria-label="Add a child node connected to this selected node"
            data-tooltip="Add a child node connected to this selected node."
            onClick={enterAddMode}
          >
            Add
          </button>
          <button
            className="btn btn--secondary"
            aria-label="Edit this node's title, type, and description"
            data-tooltip="Edit this node's title, type, and description."
            onClick={enterEditMode}
          >
            Edit
          </button>
          <button
            className="btn btn--danger"
            aria-label="Delete this node from the graph"
            data-tooltip="Delete this node from the graph."
            onClick={() => onDelete?.(node.id)}
          >
            Delete
          </button>
        </div>
        <p className="action-hint">Shortcut: Press 'A' to add or 'D' to delete while a node is selected.</p>
      </section>
    </aside>
  )
}
