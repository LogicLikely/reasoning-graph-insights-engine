# Frontend Cucumber Tests

This is a tiny local Gherkin setup for checking Storybook stories with Playwright.

## What it does

- writes features in Gherkin under `cucumber/features`
- keeps step definitions in TypeScript under `cucumber/steps`
- runs TypeScript step files directly with `ts-node`
- opens existing Storybook stories in Playwright
- avoids the backend by targeting Storybook directly

## First run

Run the full local flow with one command:

```bash
npm run func
```

That command starts Storybook, waits for it to be ready, runs the Cucumber scenario, and then stops the Storybook server.

## Useful options

- `npm run cucumber:dry` checks that Cucumber can discover the feature and step definitions
- `npm run cucumber` runs against an already-running Storybook server
- `npm run cucumber:headed` runs against an already-running Storybook server with a visible browser window
- `npm run func:headed` starts Storybook automatically and shows the browser window
- `STORYBOOK_BASE_URL=http://127.0.0.1:6006 npm run cucumber` points to a specific Storybook server

## Example

The initial scenario opens the `GraphDetailsPanel` default story and checks that the selected node title is visible.
