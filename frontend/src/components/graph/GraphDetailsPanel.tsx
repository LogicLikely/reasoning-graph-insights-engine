import { useState } from 'react'
import type { GraphFixtureEdge, GraphFixtureNode } from '../../fixtures/sampleGraph'
import './GraphDetailsPanel.css'

type EditableEdgeWeights = Pick<
  GraphFixtureEdge,
  | 'importanceToParent'
  | 'probabilityGivenParent'
  | 'probabilityGivenNotParent'
>

type EdgeCreateFields = Pick<GraphFixtureEdge, 'kind'> & EditableEdgeWeights
type EdgeWeightFormData = Record<keyof EditableEdgeWeights, string>

interface GraphDetailsPanelProps {
  node?: GraphFixtureNode
  nodes?: GraphFixtureNode[]
  edges?: GraphFixtureEdge[]
  onDelete?: (id: string) => void
  onAddSupporting?: (
    parentId: string,
    data: Partial<GraphFixtureNode>,
    edge: EdgeCreateFields,
  ) => void
  onUpdate?: (id: string, data: Partial<GraphFixtureNode>) => void
  onUpdateEdge?: (edgeId: string, data: Partial<EditableEdgeWeights>) => void
  onAddParentEdge?: (edge: Omit<GraphFixtureEdge, 'id'>) => void
}

type PanelMode = 'view' | 'add' | 'edit'
type PanelModeState = {
  nodeId?: string
  mode: PanelMode
}

const neutralEdgeWeightFormData: EdgeWeightFormData = {
  importanceToParent: '1',
  probabilityGivenParent: '0.5',
  probabilityGivenNotParent: '0.5',
}

function createEmptyParentEdgeFormData() {
  return {
    parentId: '',
    ...neutralEdgeWeightFormData,
  }
}

function getEdgeWeightFormData(edge: GraphFixtureEdge): EdgeWeightFormData {
  return {
    importanceToParent: String(edge.importanceToParent),
    probabilityGivenParent: String(edge.probabilityGivenParent),
    probabilityGivenNotParent: String(edge.probabilityGivenNotParent),
  }
}

function parseEdgeWeightFormData(data: EdgeWeightFormData): EditableEdgeWeights {
  return {
    importanceToParent: Number(data.importanceToParent),
    probabilityGivenParent: Number(data.probabilityGivenParent),
    probabilityGivenNotParent: Number(data.probabilityGivenNotParent),
  }
}

function isImportanceValid(value: number) {
  return Number.isFinite(value) && value > 0 && value <= 10
}

function isProbabilityValid(value: number) {
  return Number.isFinite(value) && value >= 0 && value <= 1
}

function areEdgeWeightsValid(weights: EditableEdgeWeights) {
  return isImportanceValid(weights.importanceToParent)
    && isProbabilityValid(weights.probabilityGivenParent)
    && isProbabilityValid(weights.probabilityGivenNotParent)
}

function isEdgeWeightFormDataValid(data: EdgeWeightFormData) {
  return Object.values(data).every((value) => value.trim().length > 0)
    && areEdgeWeightsValid(parseEdgeWeightFormData(data))
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
  parentEdgeWeights: neutralEdgeWeightFormData,
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
  const [edgeWeightData, setEdgeWeightData] = useState<Record<string, EdgeWeightFormData>>({})
  const [newParentEdge, setNewParentEdge] = useState(createEmptyParentEdgeFormData)
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
  const parentEdgeWeights = parseEdgeWeightFormData(formData.parentEdgeWeights)
  const derivedEdgeKind = getEdgeKindForNodeKind(formData.kind)
  const derivedEdgeKindLabel = formatEdgeKindLabel(derivedEdgeKind)
  const existingEdgeWeightForms = parentRelations.map((relation) => (
    edgeWeightData[relation.edge.id] ?? getEdgeWeightFormData(relation.edge)
  ))
  const newParentEdgeWeights = parseEdgeWeightFormData(newParentEdge)
  const isLikelihoodValid =
    formData.likelihoodPercent.trim().length > 0 &&
    Number.isFinite(likelihoodPercentValue) &&
    likelihoodPercentValue > 0 &&
    likelihoodPercentValue < 100
  const areParentEdgeWeightsValid = isEdgeWeightFormDataValid(formData.parentEdgeWeights)
  const areExistingEdgeWeightsValid = existingEdgeWeightForms.every(isEdgeWeightFormDataValid)
  const isNewParentEdgeValid =
    newParentEdge.parentId.length === 0 ||
    (isEdgeWeightFormDataValid(newParentEdge) && availableParentNodes.some((parent) => parent.id === newParentEdge.parentId))
  const isFormValid =
    (formData.title?.trim() ?? '').length > 0 &&
    (formData.bodyText?.trim() ?? '').length > 0 &&
    isLikelihoodValid &&
    (mode === 'add' ? areParentEdgeWeightsValid : areExistingEdgeWeightsValid && isNewParentEdgeValid)

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
        ...parentEdgeWeights,
      })
    } else if (mode === 'edit') {
      onUpdate?.(node.id, submissionData)
      parentRelations.forEach((relation) => {
        const nextWeights = parseEdgeWeightFormData(
          edgeWeightData[relation.edge.id] ?? getEdgeWeightFormData(relation.edge),
        )
        const update: Partial<EditableEdgeWeights> = {}

        if (nextWeights.importanceToParent !== relation.edge.importanceToParent) {
          update.importanceToParent = nextWeights.importanceToParent
        }
        if (nextWeights.probabilityGivenParent !== relation.edge.probabilityGivenParent) {
          update.probabilityGivenParent = nextWeights.probabilityGivenParent
        }
        if (nextWeights.probabilityGivenNotParent !== relation.edge.probabilityGivenNotParent) {
          update.probabilityGivenNotParent = nextWeights.probabilityGivenNotParent
        }

        if (Object.keys(update).length > 0) {
          onUpdateEdge?.(relation.edge.id, update)
        }
      })
      if (newParentEdge.parentId.length > 0) {
        onAddParentEdge?.({
          from: node.id,
          to: newParentEdge.parentId,
          kind: derivedEdgeKind,
          ...newParentEdgeWeights,
        })
      }
    }
    setModeState({ nodeId: node.id, mode: 'view' })
    setFormData(emptyFormData)
    setEdgeWeightData({})
    setNewParentEdge(createEmptyParentEdgeFormData())
  }

  const enterAddMode = () => {
    setFormData(emptyFormData)
    setEdgeWeightData({})
    setNewParentEdge(createEmptyParentEdgeFormData())
    setModeState({ nodeId: node.id, mode: 'add' })
  }

  const enterEditMode = () => {
    const parentEdgeWeightEntries = Object.fromEntries(
      parentRelations.map((relation) => [relation.edge.id, getEdgeWeightFormData(relation.edge)]),
    )
    setFormData({
      title: node.title ?? '',
      bodyText: node.bodyText ?? '',
      kind: node.kind,
      likelihoodPercent: formatLogOddsAsPercent(node.priorOdds) ?? '',
      parentEdgeWeights: neutralEdgeWeightFormData,
    })
    setEdgeWeightData(parentEdgeWeightEntries)
    setNewParentEdge(createEmptyParentEdgeFormData())
    setModeState({ nodeId: node.id, mode: 'edit' })
  }

  const setExistingEdgeWeight = (
    edge: GraphFixtureEdge,
    field: keyof EditableEdgeWeights,
    value: string,
  ) => {
    setEdgeWeightData((currentEdgeWeightData) => ({
      ...currentEdgeWeightData,
      [edge.id]: {
        ...(currentEdgeWeightData[edge.id] ?? getEdgeWeightFormData(edge)),
        [field]: value,
      },
    }))
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
                  {parentRelations.map((relation) => {
                    const weights = edgeWeightData[relation.edge.id]
                      ?? getEdgeWeightFormData(relation.edge)

                    return (
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
                          onChange={(event) => setExistingEdgeWeight(
                            relation.edge,
                            'importanceToParent',
                            event.target.value,
                          )}
                          step="0.001"
                          type="number"
                          value={weights.importanceToParent}
                        />
                        <label htmlFor={`edge-probability-given-parent-${relation.edge.id}`}>
                          P(this node | parent claim)
                        </label>
                        <input
                          id={`edge-probability-given-parent-${relation.edge.id}`}
                          className="form-input"
                          max="1"
                          min="0"
                          onChange={(event) => setExistingEdgeWeight(
                            relation.edge,
                            'probabilityGivenParent',
                            event.target.value,
                          )}
                          step="0.001"
                          type="number"
                          value={weights.probabilityGivenParent}
                        />
                        <label htmlFor={`edge-probability-given-not-parent-${relation.edge.id}`}>
                          P(this node | not parent claim)
                        </label>
                        <input
                          id={`edge-probability-given-not-parent-${relation.edge.id}`}
                          className="form-input"
                          max="1"
                          min="0"
                          onChange={(event) => setExistingEdgeWeight(
                            relation.edge,
                            'probabilityGivenNotParent',
                            event.target.value,
                          )}
                          step="0.001"
                          type="number"
                          value={weights.probabilityGivenNotParent}
                        />
                      </div>
                    )
                  })}
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
                  <div className="form-group">
                    <label htmlFor="new-parent-probability-given-parent">
                      P(this node | parent claim)
                    </label>
                    <input
                      id="new-parent-probability-given-parent"
                      className="form-input"
                      max="1"
                      min="0"
                      onChange={(event) => setNewParentEdge({
                        ...newParentEdge,
                        probabilityGivenParent: event.target.value,
                      })}
                      step="0.001"
                      type="number"
                      value={newParentEdge.probabilityGivenParent}
                    />
                  </div>
                  <div className="form-group">
                    <label htmlFor="new-parent-probability-given-not-parent">
                      P(this node | not parent claim)
                    </label>
                    <input
                      id="new-parent-probability-given-not-parent"
                      className="form-input"
                      max="1"
                      min="0"
                      onChange={(event) => setNewParentEdge({
                        ...newParentEdge,
                        probabilityGivenNotParent: event.target.value,
                      })}
                      step="0.001"
                      type="number"
                      value={newParentEdge.probabilityGivenNotParent}
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
                  onChange={(event) => setFormData({
                    ...formData,
                    parentEdgeWeights: {
                      ...formData.parentEdgeWeights,
                      importanceToParent: event.target.value,
                    },
                  })}
                  step="0.001"
                  type="number"
                  value={formData.parentEdgeWeights.importanceToParent}
                />
              </div>
              <div className="form-group">
                <label htmlFor="parent-edge-probability-given-parent">
                  P(new node | selected node)
                </label>
                <input
                  id="parent-edge-probability-given-parent"
                  className="form-input"
                  max="1"
                  min="0"
                  onChange={(event) => setFormData({
                    ...formData,
                    parentEdgeWeights: {
                      ...formData.parentEdgeWeights,
                      probabilityGivenParent: event.target.value,
                    },
                  })}
                  step="0.001"
                  type="number"
                  value={formData.parentEdgeWeights.probabilityGivenParent}
                />
              </div>
              <div className="form-group">
                <label htmlFor="parent-edge-probability-given-not-parent">
                  P(new node | selected node is false)
                </label>
                <input
                  id="parent-edge-probability-given-not-parent"
                  className="form-input"
                  max="1"
                  min="0"
                  onChange={(event) => setFormData({
                    ...formData,
                    parentEdgeWeights: {
                      ...formData.parentEdgeWeights,
                      probabilityGivenNotParent: event.target.value,
                    },
                  })}
                  step="0.001"
                  type="number"
                  value={formData.parentEdgeWeights.probabilityGivenNotParent}
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
                <span>P(this node | parent claim): {relation.edge.probabilityGivenParent}</span>
                <span>P(this node | not parent claim): {relation.edge.probabilityGivenNotParent}</span>
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
