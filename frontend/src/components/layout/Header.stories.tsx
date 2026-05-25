import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, userEvent } from 'storybook/test'
import { Header } from './Header'

const meta = {
  component: Header,
  parameters: {
    docs: {
      description: {
        component:
          'Storybook coverage for the global site header. These stories verify that the primary navigation renders and that the active nav styling follows route changes when users click between destinations.',
      },
    },
  },
  tags: ['autodocs', 'ai-generated', 'needs-work'],
} satisfies Meta<typeof Header>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Checks the baseline header render by asserting that the primary navigation is visible. This confirms that the global nav container mounts correctly.',
      },
    },
  },
  play: async ({ canvas }) => {
    await expect(canvas.getByRole('navigation')).toBeVisible()
  },
}

export const HomeNav: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Clicks the Home link and verifies that only Home receives the active navigation class. This exercises the route-aware selected state for the home destination.',
      },
    },
  },
  play: async ({ canvas }) => {
    const homeLink = canvas.getByRole('link', { name: 'Home' })
    const demoLink = canvas.getByRole('link', { name: 'Demo' })
    const aboutLink = canvas.getByRole('link', { name: 'About' })

    await userEvent.click(homeLink)

    await expect(homeLink).toHaveClass('site-nav__link--active')
    await expect(demoLink).not.toHaveClass('site-nav__link--active')
    await expect(aboutLink).not.toHaveClass('site-nav__link--active')
  },
}

export const DemoNav: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Clicks the Demo link and verifies that Demo becomes the only active navigation item. This confirms that the header updates active styling when navigating to the demo route.',
      },
    },
  },
  play: async ({ canvas }) => {
    const homeLink = canvas.getByRole('link', { name: 'Home' })
    const demoLink = canvas.getByRole('link', { name: 'Demo' })
    const aboutLink = canvas.getByRole('link', { name: 'About' })

    await userEvent.click(demoLink)

    await expect(demoLink).toHaveClass('site-nav__link--active')
    await expect(homeLink).not.toHaveClass('site-nav__link--active')
    await expect(aboutLink).not.toHaveClass('site-nav__link--active')
  },
}

export const AboutNav: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Clicks the About link and verifies that About alone receives the active class. This rounds out the navigation-state checks across all primary header destinations.',
      },
    },
  },
  play: async ({ canvas }) => {
    const homeLink = canvas.getByRole('link', { name: 'Home' })
    const demoLink = canvas.getByRole('link', { name: 'Demo' })
    const aboutLink = canvas.getByRole('link', { name: 'About' })

    await userEvent.click(aboutLink)

    await expect(aboutLink).toHaveClass('site-nav__link--active')
    await expect(homeLink).not.toHaveClass('site-nav__link--active')
    await expect(demoLink).not.toHaveClass('site-nav__link--active')
  },
}
