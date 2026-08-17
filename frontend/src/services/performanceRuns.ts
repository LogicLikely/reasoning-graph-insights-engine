import { httpClient } from './httpClient'

export type PerformanceAlgorithmInfo = {
  name?: string
  implementation?: string
  calculationModel?: string | null
  [key: string]: unknown
}

export type PerformanceBuildInfo = {
  gitCommit?: string | null
  dirty?: boolean | null
  gitBranch?: string | null
  configuration?: string
  dotNetVersion?: string
  operatingSystem?: string
  processArchitecture?: string
  logicalProcessorCount?: number
  serverGarbageCollection?: boolean
  [key: string]: unknown
}

export type PerformanceGraphInfo = {
  slug?: string
  type?: string | null
  nodeCount?: number
  edgeCount?: number
  maximumDepth?: number | null
  nodeKindCounts?: Record<string, number>
  fingerprint?: string | null
  [key: string]: unknown
}

export type PerformanceInvocationInfo = {
  dataSource?: string
  targetNodeId?: string | null
  changedNodeId?: string | null
  changedField?: string | null
  oldValue?: unknown
  newValue?: unknown
  parameters?: Record<string, unknown>
  [key: string]: unknown
}

export type PerformanceTimingInfo = {
  loadElapsedMilliseconds?: number | null
  computeElapsedMilliseconds?: number | null
  persistElapsedMilliseconds?: number | null
  operationElapsedMilliseconds?: number | null
  // Kept for compatibility with early report drafts.
  loadElapsedMs?: number | null
  computeElapsedMs?: number | null
  persistElapsedMs?: number | null
  operationElapsedMs?: number | null
  [key: string]: unknown
}

export type PerformanceResourceInfo = {
  cpuTimeMilliseconds?: number | null
  allocatedBytes?: number | null
  gen0Collections?: number
  gen1Collections?: number
  gen2Collections?: number
  cpuMeasurement?: string
  allocationMeasurement?: string
  [key: string]: unknown
}

export type PerformanceOutcomeInfo = {
  status?: string
  resultCount?: number | null
  resultDigest?: string | null
  errorType?: string | null
  errorMessage?: string | null
  proofStatus?: string | null
  [key: string]: unknown
}

export type PerformanceRunRecord = {
  runNumber: number
  startedAtUtc?: string
  algorithm?: PerformanceAlgorithmInfo
  build?: PerformanceBuildInfo
  graph?: PerformanceGraphInfo
  invocation?: PerformanceInvocationInfo
  timing?: PerformanceTimingInfo
  resources?: PerformanceResourceInfo
  outcome?: PerformanceOutcomeInfo
  details?: Record<string, unknown>
  [key: string]: unknown
}

export type PerformanceReportDocument = {
  schemaVersion?: number
  runs: PerformanceRunRecord[]
  [key: string]: unknown
}

export async function getPerformanceRuns(): Promise<PerformanceReportDocument> {
  const response = await httpClient.get<PerformanceReportDocument>('/api/performance-runs')

  return response.data
}
