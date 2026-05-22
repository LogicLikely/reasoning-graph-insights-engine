import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, userEvent } from 'storybook/test'
import { Header } from './Header'

const meta = {
  component: Header,
  tags: ['ai-generated', 'needs-work'],
} satisfies Meta<typeof Header>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  play: async ({ canvas }) => {
    await expect(canvas.getByRole('navigation')).toBeVisible()
  },
}

export const HomeNav: Story = {
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
