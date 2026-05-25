import React from 'react';
import type { Preview } from '@storybook/react-vite'
import { BrowserRouter } from 'react-router-dom'
import { initialize, mswLoader } from 'msw-storybook-addon'
import { sb } from 'storybook/test'
import '../src/styles/index.css'
import '../src/styles/App.css'
import { mswHandlers } from './msw-handlers'

sb.mock(import('../src/services/graphService.ts'))

initialize({ onUnhandledRequest: 'bypass' })

const preview: Preview = {
  decorators: [
    (Story) => (
      <BrowserRouter>
        <Story />
      </BrowserRouter>
    ),
  ],
  loaders: [mswLoader],
  parameters: {
    controls: {
      matchers: {
        color: /(background|color)$/i,
        date: /Date$/i,
      },
    },
    a11y: {
      // 'todo' - show a11y violations in the test UI only
      // 'error' - fail CI on a11y violations
      // 'off' - skip a11y checks entirely
      test: 'todo',
    },
    msw: {
      handlers: mswHandlers,
    },
  },
};

export default preview;
