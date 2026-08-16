import { fileURLToPath } from 'node:url'
import type { StorybookConfig } from '@storybook/react-vite'
import { mergeConfig } from 'vite'

const reactDomProfilingShim = fileURLToPath(
  new URL('./react-dom-profiling-shim.mjs', import.meta.url),
)

const config: StorybookConfig = {
  stories: [
    '../src/**/*.mdx',
    '../src/**/*.stories.@(js|jsx|mjs|ts|tsx)',
  ],
  addons: [
    '@chromatic-com/storybook',
    // '@storybook/addon-vitest',
    '@storybook/addon-a11y',
    '@storybook/addon-docs',
    '@storybook/addon-mcp',
  ],
  staticDirs: ['../public'],
  framework: '@storybook/react-vite',
  viteFinal: async (viteConfig, { configType }) => {
    const harnessBuildIdentity = configType === 'PRODUCTION'
      ? 'storybook-production-profiling'
      : 'storybook-development'
    const identifiedConfig = mergeConfig(viteConfig, {
      define: {
        __LOGICLIKELY_INSIGHTS_HARNESS_BUILD__: JSON.stringify(harnessBuildIdentity),
      },
    })

    if (configType !== 'PRODUCTION') {
      return identifiedConfig
    }

    // The ordinary production ReactDOM build intentionally omits Profiler
    // callbacks. Only the test-only static Storybook harness uses ReactDOM's
    // official profiling production entry; the product Vite build is untouched.
    return mergeConfig(identifiedConfig, {
      resolve: {
        alias: [
          {
            find: /^@storybook\/react-dom-shim$/,
            replacement: reactDomProfilingShim,
          },
        ],
      },
    })
  },
}
export default config
