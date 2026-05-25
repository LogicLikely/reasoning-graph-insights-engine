import { setWorldConstructor, World } from '@cucumber/cucumber'
import { chromium, type Browser, type BrowserContext, type Page } from 'playwright'

const DEFAULT_STORYBOOK_URL = 'http://127.0.0.1:6006'

export class StorybookWorld extends World {
  browser?: Browser
  context?: BrowserContext
  page?: Page

  get baseUrl() {
    return process.env.STORYBOOK_BASE_URL ?? DEFAULT_STORYBOOK_URL
  }

  get storyPage() {
    if (!this.page) {
      throw new Error('Playwright page is not available yet.')
    }

    return this.page
  }
}

setWorldConstructor(StorybookWorld)

export async function launchBrowser() {
  return chromium.launch({
    headless: process.env.BDD_HEADLESS !== 'false',
  })
}
