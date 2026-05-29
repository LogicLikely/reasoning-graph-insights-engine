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
      <div className="graph-details-panel graph-details-panel--empty" data-testid="graph-details-panel">
        <h3>Node Details</h3>
        <p className="empty-hint">Select a node in the graph to view its details and available actions.</p>
      </div>
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
      <div className="graph-details-panel" data-testid="graph-details-panel">
        <header className="node-header">
          {isEdit ? (
            <>
              <span className="eyebrow">Editing node {node.id}</span>
              <h2>Modify Node</h2>
            </>
          ) : (
            <>
              <span className="eyebrow">Adding support to {node.id}</span>
              <h2>New Node Details</h2>
            </>
          )}
        </header>

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

        <div className="actions-button-group">
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
    <div className="graph-details-panel" data-testid="graph-details-panel">
      <header className="node-header">
        <div className="node-kind-row">
          <span className={`node-kind-tag node-kind-tag--${node.kind}`}>{node.kind}</span>
          <span className="node-id">ID: {node.id}</span>
        </div>
        <h2>{node.title}</h2>
      </header>

      <section className="node-section">
        <h4>Description</h4>
        <p className="node-body-text">{node.bodyText}</p>
      </section>

      <section className="node-section node-metadata">
        <h4>Metadata</h4>
        <div className="metadata-grid">
          {node.confidence !== undefined && (
            <div className="metadata-item">
              <span className="label">Confidence</span>
              <span className="value">{(node.confidence * 100).toFixed(0)}%</span>
            </div>
          )}
          {node.importance !== undefined && (
            <div className="metadata-item">
              <span className="label">Importance</span>
              <span className="value">{node.importance}</span>
            </div>
          )}
        </div>
        {node.tags && node.tags.length > 0 && (
          <div className="node-tags">
            {node.tags.map(tag => <span key={tag} className="tag-pill">{tag}</span>)}
          </div>
        )}
      </section>

      <section className="node-section node-actions">
        <h4>Node Actions</h4>
        <div className="actions-button-group">
          <button
            className="btn btn--primary"
            onClick={enterAddMode}
          >
            Add Supporting Premise
          </button>
          <button
            className="btn btn--secondary"
            onClick={enterEditMode}
          >
            Edit Node
          </button>
          <button
            className="btn btn--danger"
            onClick={() => onDelete?.(node.id)}
          >
            Delete This Node
          </button>
        </div>
        <p className="action-hint">Shortcut: Press 'A' to add or 'D' to delete while a node is selected.</p>
      </section>
    </div>
  )
}