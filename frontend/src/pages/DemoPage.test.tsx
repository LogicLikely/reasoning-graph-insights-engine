import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { sampleGraph } from '../fixtures/sampleGraph'
import { DemoPage } from './DemoPage'

const getGraphBySlugMock = vi.fn()

vi.mock('../services/graphService', () => ({
  getGraphBySlug: (slug: string) => getGraphBySlugMock(slug),
}))

describe('DemoPage', () => {
  beforeEach(() => {
    getGraphBySlugMock.mockReset()
  })

  it('shows a loading state while the graph is loading', () => {
    getGraphBySlugMock.mockReturnValue(new Promise(() => {}))

    render(<DemoPage />)

    expect(screen.getByTestId('demo-loading-state')).toBeInTheDocument()
  })

  it('renders the graph on successful load', async () => {
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    expect(await screen.findByTestId('graph-canvas')).toBeInTheDocument()
    expect(
      screen.getByRole('heading', { level: 2, name: sampleGraph.title }),
    ).toBeInTheDocument()
  })

  it('shows an error state and retries loading when requested', async () => {
    getGraphBySlugMock
      .mockRejectedValueOnce(new Error('Request failed'))
      .mockResolvedValueOnce(sampleGraph)

    render(<DemoPage />)

    expect(await screen.findByTestId('demo-error-state')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /retry/i }))

    await waitFor(() => {
      expect(getGraphBySlugMock).toHaveBeenCalledTimes(2)
    })

    expect(await screen.findByTestId('graph-canvas')).toBeInTheDocument()
  })
})
