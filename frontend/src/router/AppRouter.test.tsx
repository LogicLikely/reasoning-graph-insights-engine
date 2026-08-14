import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { AppRouter } from './AppRouter'

function renderAtRoute(path: string) {
  window.history.pushState({}, '', path)

  return render(<AppRouter />)
}

describe('AppRouter', () => {
  afterEach(() => {
    cleanup()
    window.history.pushState({}, '', '/')
  })

  it('renders the home page for the root route', () => {
    renderAtRoute('/')

    expect(screen.getByTestId('home-page')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: /primary/i })).toBeInTheDocument()
  })

  it('renders the demo page for the demo route', () => {
    renderAtRoute('/demo')

    expect(screen.getByTestId('demo-page')).toBeInTheDocument()
  })

  it('renders the about page for the about route', () => {
    renderAtRoute('/about')

    expect(screen.getByTestId('about-page')).toBeInTheDocument()
  })

  it('renders the not found page for an unknown route', () => {
    renderAtRoute('/not-a-real-route')

    expect(screen.getByTestId('not-found-page')).toBeInTheDocument()
  })
})
