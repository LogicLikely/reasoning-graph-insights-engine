import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { AboutPage } from './AboutPage'

const meta = {
  component: AboutPage,
  parameters: {
    docs: {
      description: {
        component:
          'Storybook coverage for the About page. This story validates the project narrative, internship acknowledgment, architecture, and primary next steps.',
      },
    },
  },
  tags: ['autodocs'],
} satisfies Meta<typeof AboutPage>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Checks that the project overview, internship contribution, architecture, and exploration links render together.',
      },
    },
  },
  play: async ({ canvas }) => {
    await expect(
      canvas.getByRole('heading', {
        name: /A working laboratory for reasoning graphs/i,
        level: 1,
      }),
    ).toBeVisible()
    await expect(
      canvas.getByRole('heading', {
        name: /A collaborative engineering project/i,
        level: 2,
      }),
    ).toBeVisible()
    await expect(
      canvas.getByRole('heading', {
        name: /From edge delivery to graph analysis/i,
        level: 2,
      }),
    ).toBeVisible()
    await expect(canvas.getByText('Cloudflare Pages')).toBeVisible()
    await expect(canvas.getByText('JSON run history')).toBeVisible()
    await expect(canvas.getByRole('link', { name: /Open the demo/i })).toHaveAttribute(
      'href',
      '/demo',
    )
    await expect(
      canvas.getByRole('link', { name: /View source on GitHub/i }),
    ).toHaveAttribute(
      'href',
      'https://github.com/LogicLikely/reasoning-graph-insights-engine',
    )
  },
}
