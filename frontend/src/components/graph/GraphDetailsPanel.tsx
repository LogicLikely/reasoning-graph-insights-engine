import { useState } from 'react'
import type { GraphFixtureEdge, GraphFixtureNode } from '../../fixtures/sampleGraph'
import './GraphDetailsPanel.css'

interface GraphDetailsPanelProps {
  node?: GraphFixtureNode
  nodes?: GraphFixtureNode[]
  edges?: GraphFixtureEdge[]
  onDelete?: (id: string) => void
  onAddSupporting?: (
    parentId: string,
    data: Partial<GraphFixtureNode>,
    edge: Pick<GraphFixtureEdge, 'kind' | 'importanceToParent'>,
  ) => void
  onUpdate?: (id: string, data: Partial<GraphFixtureNode>) => void
  onUpdateEdge?: (edgeId: string, data: Partial<Pick<GraphFixtureEdge, 'importanceToParent'>>) => void
  onAddParentEdge?: (edge: Omit<GraphFixtureEdge, 'id'>) => void
}

type PanelMode = 'view' | 'add' | 'edit'
type PanelModeState = {
  nodeId?: string
  mode: PanelMode
}

function logOddsToProbability(logOdds: number): number {
  return 1 / (1 + Math.exp(-logOdds))
}

function probabilityToLogOdds(probability: number): number {
  return Math.log(probability / (1 - probability))
}

function formatMetric(value?: number) {
  if (value === null || value === undefined) {
    return undefined; // Or an empty string, or a default value like '0.00'
  }
  return value.toFixed(2);
}

function formatLogOddsAsPercent(value?: number) {
  if (value === null || value === undefined) {
    return undefined; // Or an empty string, or a default value like '0.00'
  }
  return (logOddsToProbability(value) * 100).toFixed(2);
}

const emptyFormData = {
  title: '',
  bodyText: '',
  kind: 'claim' as GraphFixtureNode['kind'],
  likelihoodPercent: formatLogOddsAsPercent(0) ?? '',
  parentImportance: '1'
}

const editableNodeKinds = ['claim', 'evidence', 'objection'] satisfies GraphFixtureNode['kind'][]

function formatEdgeVerb(kind: GraphFixtureEdge['kind']) {
  return kind === 'rebut' ? 'counters' : 'supports'
}

function getEdgeKindForNodeKind(kind: GraphFixtureNode['kind']): GraphFixtureEdge['kind'] {
  return kind === 'objection' ? 'rebut' : 'support'
}

function formatEdgeKindLabel(kind: GraphFixtureEdge['kind']) {
  return kind === 'rebut' ? 'Counter' : 'Support'
}

export function GraphDetailsPanel({
  node,
  nodes = [],
  edges = [],
  onDelete,
  onAddSupporting,
  onUpdate,
  onUpdateEdge,
  onAddParentEdge,
}: GraphDetailsPanelProps) {
  const [modeState, setModeState] = useState<PanelModeState>({ mode: 'view' })
  const [formData, setFormData] = useState(emptyFormData)
  const [edgeImportanceData, setEdgeImportanceData] = useState<Record<string, string>>({})
  const [newParentEdge, setNewParentEdge] = useState({
    parentId: '',
    importanceToParent: '1'
  })
  const mode = node && modeState.nodeId === node.id ? modeState.mode : 'view'

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

  const parentRelations = edges
    .filter((edge) => edge.from === node.id)
    .map((edge) => ({
      edge,
      parent: nodes.find((candidate) => candidate.id === edge.to),
    }))
    .filter((relation): relation is { edge: GraphFixtureEdge, parent: GraphFixtureNode } => Boolean(relation.parent))
  const existingParentIds = new Set(parentRelations.map((relation) => relation.parent.id))
  const availableParentNodes = nodes.filter((candidate) => candidate.id !== node.id && !existingParentIds.has(candidate.id))
  const likelihoodPercentValue = Number(formData.likelihoodPercent)
  const parentImportanceValue = Number(formData.parentImportance)
  const derivedEdgeKind = getEdgeKindForNodeKind(formData.kind)
  const derivedEdgeKindLabel = formatEdgeKindLabel(derivedEdgeKind)
  const edgeImportanceValues = parentRelations.map((relation) => Number(edgeImportanceData[relation.edge.id] ?? relation.edge.importanceToParent))
  const newParentImportanceValue = Number(newParentEdge.importanceToParent)
  const isLikelihoodValid =
    formData.likelihoodPercent.trim().length > 0 &&
    Number.isFinite(likelihoodPercentValue) &&
    likelihoodPercentValue > 0 &&
    likelihoodPercentValue < 100
  const isImportanceValid = (value: number) => Number.isFinite(value) && value > 0 && value <= 10
  const isParentImportanceValid = isImportanceValid(parentImportanceValue)
  const areEdgeImportancesValid = edgeImportanceValues.every(isImportanceValid)
  const isNewParentEdgeValid =
    newParentEdge.parentId.length === 0 ||
    (isImportanceValid(newParentImportanceValue) && availableParentNodes.some((parent) => parent.id === newParentEdge.parentId))
  const isFormValid =
    (formData.title?.trim() ?? '').length > 0 &&
    (formData.bodyText?.trim() ?? '').length > 0 &&
    isLikelihoodValid &&
    (mode === 'add' ? isParentImportanceValid : areEdgeImportancesValid && isNewParentEdgeValid)

  const handleSave = () => {
    if (!isFormValid) return

    const probability = Number(formData.likelihoodPercent) / 100
    const submissionData: Partial<GraphFixtureNode> = {
      title: (formData.title ?? '').trim(),
      bodyText: (formData.bodyText ?? '').trim(),
      priorOdds: probabilityToLogOdds(probability)
    }

    if (mode === 'add') {
      onAddSupporting?.(node.id, {
        ...submissionData,
        kind: formData.kind,
        tags: ['dynamic']
      }, {
        kind: derivedEdgeKind,
        importanceToParent: parentImportanceValue
      })
    } else if (mode === 'edit') {
      onUpdate?.(node.id, submissionData)
      parentRelations.forEach((relation) => {
        const nextImportance = Number(edgeImportanceData[relation.edge.id] ?? relation.edge.importanceToParent)
        if (nextImportance !== relation.edge.importanceToParent) {
          onUpdateEdge?.(relation.edge.id, { importanceToParent: nextImportance })
        }
      })
      if (newParentEdge.parentId.length > 0) {
        onAddParentEdge?.({
          from: node.id,
          to: newParentEdge.parentId,
          kind: derivedEdgeKind,
          importanceToParent: newParentImportanceValue,
        })
      }
    }
    setModeState({ nodeId: node.id, mode: 'view' })
    setFormData(emptyFormData)
    setEdgeImportanceData({})
    setNewParentEdge({ parentId: '', importanceToParent: '1' })
  }

  const enterAddMode = () => {
    setFormData(emptyFormData)
    setEdgeImportanceData({})
    setNewParentEdge({ parentId: '', importanceToParent: '1' })
    setModeState({ nodeId: node.id, mode: 'add' })
  }

  const enterEditMode = () => {
    const parentImportanceEntries = Object.fromEntries(
      parentRelations.map((relation) => [relation.edge.id, String(relation.edge.importanceToParent)]),
    )
    setFormData({
      title: node.title ?? '',
      bodyText: node.bodyText ?? '',
      kind: node.kind,
      likelihoodPercent: formatLogOddsAsPercent(node.priorOdds) ?? '',
      parentImportance: '1'
    })
    setEdgeImportanceData(parentImportanceEntries)
    setNewParentEdge({
      parentId: '',
      importanceToParent: '1'
    })
    setModeState({ nodeId: node.id, mode: 'edit' })
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
            {isEdit ? (
              <>
                <span id="node-kind-label" className="form-group__label">Type</span>
                <div
                  aria-labelledby="node-kind-label"
                  className="form-input form-input--readonly"
                  role="textbox"
                >
                  {formData.kind}
                </div>
              </>
            ) : (
              <>
                <label htmlFor="node-kind">Type</label>
                <select
                  id="node-kind"
                  className="form-input"
                  value={formData.kind}
                  onChange={(e) => setFormData({ ...formData, kind: e.target.value as GraphFixtureNode['kind'] })}
                >
                  {editableNodeKinds.map((kind) => (
                    <option key={kind} value={kind}>
                      {kind}
                    </option>
                  ))}
                </select>
              </>
            )}
          </div>

          <div className="form-group">
            <label htmlFor="node-likelihood">Likelihood</label>
            <input
              id="node-likelihood"
              className="form-input"
              inputMode="decimal"
              max="99.99"
              min="0.01"
              onChange={(e) => setFormData({ ...formData, likelihoodPercent: e.target.value })}
              placeholder="50.00"
              step="0.01"
              type="number"
              value={formData.likelihoodPercent}
            />
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

          {isEdit ? (
            <section className="node-section node-edge-form">
              <h4>Parent Relations</h4>
              {parentRelations.length ? (
                <div className="node-edge-form__list">
                  {parentRelations.map((relation) => (
                    <div className="node-edge-form__row" key={relation.edge.id}>
                      <p>
                        This node {formatEdgeVerb(relation.edge.kind)} <strong>{relation.parent.title}</strong>
                      </p>
                      <label htmlFor={`edge-importance-${relation.edge.id}`}>Importance to that claim</label>
                      <input
                        id={`edge-importance-${relation.edge.id}`}
                        className="form-input"
                        max="10"
                        min="0.001"
                        onChange={(event) => setEdgeImportanceData({
                          ...edgeImportanceData,
                          [relation.edge.id]: event.target.value,
                        })}
                        step="0.001"
                        type="number"
                        value={edgeImportanceData[relation.edge.id] ?? String(relation.edge.importanceToParent)}
                      />
                    </div>
                  ))}
                </div>
              ) : (
                <p className="node-edge-form__empty">No parent relations.</p>
              )}

              {availableParentNodes.length ? (
                <div className="node-edge-form__add">
                  <div className="form-group">
                    <label htmlFor="new-parent-node">Additional Parent {derivedEdgeKindLabel}</label>
                    <select
                      id="new-parent-node"
                      className="form-input"
                      onChange={(event) => setNewParentEdge({ ...newParentEdge, parentId: event.target.value })}
                      value={newParentEdge.parentId}
                    >
                      <option value="">None</option>
                      {availableParentNodes.map((parent) => (
                        <option key={parent.id} value={parent.id}>
                          {parent.title}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div className="form-group">
                    <label htmlFor="new-parent-importance">Importance to that claim</label>
                    <input
                      id="new-parent-importance"
                      className="form-input"
                      max="10"
                      min="0.001"
                      onChange={(event) => setNewParentEdge({
                        ...newParentEdge,
                        importanceToParent: event.target.value,
                      })}
                      step="0.001"
                      type="number"
                      value={newParentEdge.importanceToParent}
                    />
                  </div>
                </div>
              ) : null}
            </section>
          ) : (
            <section className="node-section node-edge-form">
              <h4>Relation to selected node: {derivedEdgeKindLabel}</h4>
              <div className="form-group">
                <label htmlFor="parent-edge-importance">Importance to that claim</label>
                <input
                  id="parent-edge-importance"
                  className="form-input"
                  max="10"
                  min="0.001"
                  onChange={(event) => setFormData({ ...formData, parentImportance: event.target.value })}
                  step="0.001"
                  type="number"
                  value={formData.parentImportance}
                />
              </div>
            </section>
          )}
        </div>

        <div className="actions-button-group form-actions">
          <button
            className="btn btn--primary"
            onClick={handleSave}
            disabled={!isFormValid}
          >
            {isEdit ? 'Save Changes' : 'Create Node'}
          </button>
          <button className="btn btn--secondary" onClick={() => setModeState({ nodeId: node.id, mode: 'view' })}>Cancel</button>
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
        {formatMetric(node.priorOdds) ? (
          <>
            <dt>Prior likelihood</dt>
            <dd>{formatLogOddsAsPercent(node.priorOdds)}%</dd>
          </>
        ) : null}
        {formatMetric(node.posteriorOdds) ? (
          <>
            <dt>Posterior likelihood</dt>
            <dd>{formatLogOddsAsPercent(node.posteriorOdds)}%</dd>
          </>
        ) : null}
        {node.kind == 'evidence' && node.evidence ? (
          <>
            <dt>Evidence</dt>
            <dd className="graph-evidence-block">
              <strong>{node.evidence.type}</strong>
              {node.evidence.rationale ? <p>{node.evidence.rationale}</p> : null}
            </dd>
          </>
        ) : null}
      </dl>

      <section className="node-section node-relations">
        <h4>Parent Relations</h4>
        {parentRelations.length ? (
          <div className="node-relations__list">
            {parentRelations.map((relation) => (
              <article className="node-relation" key={relation.edge.id}>
                <p>
                  This node {formatEdgeVerb(relation.edge.kind)} <strong>{relation.parent.title}</strong>
                </p>
                <span>Importance to that claim: {relation.edge.importanceToParent}/10</span>
              </article>
            ))}
          </div>
        ) : (
          <p className="node-relations__empty">No parent relations.</p>
        )}
      </section>

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
            aria-label="Edit this node's title, type, likelihood, and description."
            data-tooltip="Edit this node's title, type, likelihood, and description."
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
