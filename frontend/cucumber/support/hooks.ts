import { After, Before, setDefaultTimeout } from '@cucumber/cucumber'
import { launchBrowser, type StorybookWorld } from './world.js'

setDefaultTimeout(30_000)

Before(async function (this: StorybookWorld) {
  this.browser = await launchBrowser()
  this.context = await this.browser.newContext()
  this.page = await this.context.newPage()
  this.unexpectedConsoleErrors = []
  this.pageErrors = []
  this.page.on('console', (message) => {
    if (message.type() === 'error') {
      this.unexpectedConsoleErrors.push(message.text())
    }
  })
  this.page.on('pageerror', (error) => {
    this.pageErrors.push(error.message)
  })
})

After(async function (this: StorybookWorld) {
  await this.page?.close()
  await this.context?.close()
  await this.browser?.close()

  const errors = [
    ...this.unexpectedConsoleErrors.map((message) => `console: ${message}`),
    ...this.pageErrors.map((message) => `page: ${message}`),
  ]
  if (errors.length > 0) {
    throw new Error(`Unexpected browser errors:\n${errors.join('\n')}`)
  }
})
