import { After, Before, setDefaultTimeout } from '@cucumber/cucumber'
import { launchBrowser, type StorybookWorld } from './world.js'

setDefaultTimeout(30_000)

Before(async function (this: StorybookWorld) {
  this.browser = await launchBrowser()
  this.context = await this.browser.newContext()
  this.page = await this.context.newPage()
})

After(async function (this: StorybookWorld) {
  await this.page?.close()
  await this.context?.close()
  await this.browser?.close()
})
