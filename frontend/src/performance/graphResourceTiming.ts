export interface GraphResourceTimingObservation {
  nextHopProtocol: string | null
  resourceTimingLimitation: string | null
  resourceTiming: Record<string, number | string | null>
}

/** Reads only the exact browser resource entry; it never infers a protocol. */
export function observeGraphResourceTiming(url: string): GraphResourceTimingObservation {
  const entries = performance.getEntriesByName(url, 'resource')
  const entry = entries.at(-1) as PerformanceResourceTiming | undefined
  if (!entry) {
    return {
      nextHopProtocol: null,
      resourceTimingLimitation:
        'The browser exposed no PerformanceResourceTiming entry for the exact graph request URL.',
      resourceTiming: {
        name: url,
        entryCount: 0,
      },
    }
  }

  const nextHopProtocol = typeof entry.nextHopProtocol === 'string' && entry.nextHopProtocol.trim()
    ? entry.nextHopProtocol.trim()
    : null

  return {
    nextHopProtocol,
    resourceTimingLimitation: nextHopProtocol
      ? null
      : 'PerformanceResourceTiming was present, but the browser did not expose nextHopProtocol.',
    resourceTiming: {
      name: entry.name,
      entryCount: entries.length,
      startTimeMilliseconds: entry.startTime,
      durationMilliseconds: entry.duration,
      transferSizeBytes: entry.transferSize,
      encodedBodySizeBytes: entry.encodedBodySize,
      decodedBodySizeBytes: entry.decodedBodySize,
    },
  }
}
