import type { BoundedAnalysisResultPayload } from './browserJourneyContract'
import './InsightsBrowserPerformanceHarness.css'

export const MAX_RENDERED_RESULT_ITEMS = 100
export const MAX_RENDERED_PATHS = 20
export const MAX_RENDERED_PATH_NODE_IDS = 128
export const MAX_RENDERED_VALUE_CHARACTERS = 512
const MAX_STRUCTURED_PREVIEW_ENTRIES = 8
const MAX_STRUCTURED_PREVIEW_DEPTH = 2

function structuredPreview(value: unknown, depth = 0): unknown {
  if (value === null || value === undefined ||
      typeof value === 'string' || typeof value === 'number' ||
      typeof value === 'boolean') {
    return value
  }

  if (typeof value === 'bigint' || typeof value === 'symbol' || typeof value === 'function') {
    return String(value)
  }

  if (Array.isArray(value)) {
    if (depth >= MAX_STRUCTURED_PREVIEW_DEPTH) {
      return `[${value.length} items not expanded]`
    }
    const preview = value
      .slice(0, MAX_STRUCTURED_PREVIEW_ENTRIES)
      .map((item) => structuredPreview(item, depth + 1))
    if (value.length > MAX_STRUCTURED_PREVIEW_ENTRIES) {
      preview.push(`… (${value.length - MAX_STRUCTURED_PREVIEW_ENTRIES} more items)`)
    }
    return preview
  }

  const record = value as Record<string, unknown>
  const keys = Object.keys(record).sort()
  if (depth >= MAX_STRUCTURED_PREVIEW_DEPTH) {
    return `{${keys.length} fields not expanded}`
  }
  const preview: Record<string, unknown> = {}
  for (const key of keys.slice(0, MAX_STRUCTURED_PREVIEW_ENTRIES)) {
    preview[key] = structuredPreview(record[key], depth + 1)
  }
  if (keys.length > MAX_STRUCTURED_PREVIEW_ENTRIES) {
    preview['…'] = `${keys.length - MAX_STRUCTURED_PREVIEW_ENTRIES} more fields`
  }
  return preview
}

function boundDisplayText(text: string): string {
  if (text.length <= MAX_RENDERED_VALUE_CHARACTERS) {
    return text
  }

  const suffix = `… [truncated from ${text.length} characters]`
  return `${text.slice(0, Math.max(0, MAX_RENDERED_VALUE_CHARACTERS - suffix.length))}${suffix}`
}

function displayValue(value: unknown): string {
  if (value === null) {
    return 'null'
  }

  if (value === undefined) {
    return '—'
  }

  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
    return boundDisplayText(String(value))
  }

  return boundDisplayText(JSON.stringify(structuredPreview(value)))
}

function itemColumns(items: Array<Record<string, unknown>>): string[] {
  const columns = new Set<string>()
  for (const item of items) {
    for (const key of Object.keys(item).sort()) {
      columns.add(key)
      if (columns.size === 6) {
        return [...columns]
      }
    }
  }

  return [...columns]
}

export interface BoundedAnalysisResultProps {
  payload: BoundedAnalysisResultPayload
}

export function BoundedAnalysisResult({ payload }: BoundedAnalysisResultProps) {
  const operationId = payload.operationId ?? 'analysis-result'
  const items = (payload.topItems ?? payload.items ?? []).slice(0, MAX_RENDERED_RESULT_ITEMS)
  const paths = (payload.orderedPaths ?? []).slice(0, MAX_RENDERED_PATHS)
  const columns = itemColumns(items)
  const summary = Object.entries(payload.summary ?? {}).sort(([left], [right]) => (
    left.localeCompare(right)
  ))
  const distribution = Array.isArray(payload.distribution)
    ? payload.distribution.filter((item): item is { label: string; value: number } => (
      Boolean(item) &&
      typeof item === 'object' &&
      typeof (item as Record<string, unknown>).label === 'string' &&
      typeof (item as Record<string, unknown>).value === 'number'
    ))
    : []

  return (
    <article
      className="insights-browser-result"
      data-result-status={payload.status ?? 'success'}
      data-testid="bounded-analysis-result"
    >
      <header className="insights-browser-result__header">
        <div>
          <span className="insights-browser-result__eyebrow">Bounded result fixture</span>
          <h1>{payload.title ?? operationId}</h1>
        </div>
        <dl className="insights-browser-result__identity">
          <div>
            <dt>Operation</dt>
            <dd>{operationId}</dd>
          </div>
          <div>
            <dt>Total results</dt>
            <dd data-testid="result-total-cardinality">{payload.totalResultCardinality}</dd>
          </div>
          {payload.resultDigest ? (
            <div>
              <dt>Digest</dt>
              <dd>{payload.resultDigest}</dd>
            </div>
          ) : null}
        </dl>
      </header>

      {payload.failure ? (
        <section aria-label="Result state" className="insights-browser-result__state">
          <h2>{payload.failure.kind}</h2>
          <p>{payload.failure.message}</p>
        </section>
      ) : null}

      {summary.length > 0 ? (
        <section aria-labelledby="result-summary-heading">
          <h2 id="result-summary-heading">Summary</h2>
          <dl className="insights-browser-result__summary">
            {summary.map(([label, value]) => (
              <div key={label}>
                <dt>{label}</dt>
                <dd>{displayValue(value)}</dd>
              </div>
            ))}
          </dl>
        </section>
      ) : null}

      {distribution.length > 0 ? (
        <section aria-labelledby="result-distribution-heading">
          <h2 id="result-distribution-heading">Distribution</h2>
          <ol className="insights-browser-result__distribution">
            {distribution.map(({ label, value }) => (
              <li key={label}>
                <span>{label}</span>
                <meter min="0" max="1" value={value} />
                <strong>{value}</strong>
              </li>
            ))}
          </ol>
        </section>
      ) : null}

      {payload.distribution !== undefined && distribution.length === 0 ? (
        <section aria-labelledby="result-distribution-data-heading">
          <h2 id="result-distribution-data-heading">Distribution</h2>
          <pre className="insights-browser-result__distribution-data">
            {displayValue(payload.distribution)}
          </pre>
        </section>
      ) : null}

      {items.length > 0 ? (
        <section aria-labelledby="result-items-heading">
          <h2 id="result-items-heading">Top results</h2>
          <div className="insights-browser-result__table-frame">
            <table>
              <thead>
                <tr>
                  {columns.map((column) => <th key={column}>{column}</th>)}
                </tr>
              </thead>
              <tbody>
                {items.map((item, index) => (
                  <tr key={String(item.nodeId ?? item.id ?? index)}>
                    {columns.map((column) => (
                      <td key={column}>{displayValue(item[column])}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="insights-browser-result__bound" data-testid="bounded-result-item-count">
            Rendering {items.length} of {payload.totalResultCardinality} result items.
          </p>
        </section>
      ) : null}

      {paths.length > 0 ? (
        <section aria-labelledby="result-paths-heading">
          <h2 id="result-paths-heading">Ordered paths</h2>
          <ol className="insights-browser-result__paths">
            {paths.map((path, index) => (
              <li key={String(path.pathId ?? path.id ?? index)}>
                <span>
                  {Array.isArray(path.nodeIds)
                    ? path.nodeIds.slice(0, MAX_RENDERED_PATH_NODE_IDS).join(' → ')
                    : displayValue(path)}
                  {Array.isArray(path.nodeIds) && path.nodeIds.length > MAX_RENDERED_PATH_NODE_IDS
                    ? ` … (${path.nodeIds.length - MAX_RENDERED_PATH_NODE_IDS} more nodes)`
                    : null}
                </span>
                {path.score !== null && path.score !== undefined
                  ? <strong>{displayValue(path.score)}</strong>
                  : null}
              </li>
            ))}
          </ol>
          <p className="insights-browser-result__bound" data-testid="bounded-result-path-count">
            Rendering {paths.length} of {payload.orderedPaths?.length ?? 0} ordered paths.
          </p>
        </section>
      ) : null}

      {!payload.failure && summary.length === 0 && items.length === 0 && paths.length === 0 ? (
        <p className="insights-browser-result__empty">No result rows to display.</p>
      ) : null}
    </article>
  )
}
