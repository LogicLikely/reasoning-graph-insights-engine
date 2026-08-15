import type { Meta, StoryObj } from '@storybook/react-vite'
import type {
  CanonicalNode,
  GraphMapNodeRenderContext,
} from '@logiclikely/graphmap'
import '@logiclikely/graphmap/style.css'
import { expect, within } from 'storybook/test'
import {
  insightsNodeTitleCorpus,
  type InsightsNodeTitleCorpusEntry,
} from '../../fixtures/review/insightsNodeTitleCorpus'
import type { GraphFixtureNode } from '../../fixtures/sampleGraph'
import {
  getInsightsNodePresentation,
  renderInsightsGraphNode,
} from './insightsGraphPresentation'
import './InsightsGraphCanvas.css'
import './InsightsGraphTitleCorpus.css'

interface CorpusGridProps {
  childCount: 0 | 1
}

function CorpusCard({
  entry,
  childCount,
}: {
  entry: InsightsNodeTitleCorpusEntry
  childCount: 0 | 1
}) {
  const node: GraphFixtureNode = {
    id: entry.id,
    kind: entry.kind,
    title: entry.title,
    bodyText: entry.bodyText,
    tags: ['corpus-preview'],
    priorOdds: 0,
    posteriorOdds: 0,
  }
  const graphNode: CanonicalNode<GraphFixtureNode> = {
    id: node.id,
    kind: node.kind,
    title: node.title,
    text: node.bodyText,
    search: { title: node.title, text: node.bodyText },
    raw: node,
  }
  const presentation = getInsightsNodePresentation(node, graphNode)
  const context: GraphMapNodeRenderContext<GraphFixtureNode> = {
    node,
    graphNode,
    id: node.id,
    kind: node.kind,
    title: node.title,
    text: node.bodyText,
    selected: false,
    childCount,
    hiddenCount: 0,
    expanded: childCount > 0,
    onToggle: () => undefined,
    orientation: 'LR',
    width: 230,
    height: 112,
  }

  return (
    <article className="insights-title-corpus__entry" data-testid="corpus-title-card">
      <div className="insights-title-corpus__metadata">
        <span>{entry.id.replace('corpus-preview-', '#')}</span>
        <span>{entry.kind}</span>
        <span>{entry.titleCharacters} chars</span>
        <span>{entry.sampleClass}</span>
      </div>
      <div
        className={`insights-title-corpus__node react-flow__node ${presentation?.className ?? ''}`}
        data-full-title={entry.title}
        data-testid="corpus-node-frame"
      >
        {renderInsightsGraphNode(context)}
      </div>
      <p className="insights-title-corpus__source">{entry.sourceId}</p>
    </article>
  )
}

function CorpusGrid({ childCount }: CorpusGridProps) {
  return (
    <main className="ll-graphmap insights-title-corpus">
      <section data-theme="insights">
        <header className="insights-title-corpus__header">
          <p className="insights-title-corpus__eyebrow">Review artifact · round 01</p>
          <h1>Public-domain stress-corpus titles</h1>
          <p>
            All 180 candidates use the production renderer at 230×112 pixels and
            a pinned 21px node font. This view uses{' '}
            {childCount > 0 ? 'the narrower parent-node width' : 'the wider leaf-node width'}.
          </p>
        </header>
        <div className="insights-title-corpus__grid">
          {insightsNodeTitleCorpus.map((entry) => (
            <CorpusCard key={entry.id} entry={entry} childCount={childCount} />
          ))}
        </div>
      </section>
    </main>
  )
}

const meta = {
  title: 'Review/Stress Corpus Titles',
  component: CorpusGrid,
  args: {
    childCount: 1,
  },
  parameters: {
    layout: 'fullscreen',
    docs: {
      description: {
        component:
          'A review-only grid for the candidate public-domain stress corpus. It calls the production node renderer without loading Dagre, so every title can be inspected at a stable real-world width.',
      },
    },
  },
  tags: ['autodocs'],
} satisfies Meta<typeof CorpusGrid>

export default meta

type Story = StoryObj<typeof meta>

export const ParentWidth: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement)
    const cards = canvas.getAllByTestId('corpus-title-card')
    const frames = canvas.getAllByTestId('corpus-node-frame')
    const titles = canvasElement.querySelectorAll<HTMLElement>(
      '.insights-graphmap-card__title-text',
    )

    await expect(cards).toHaveLength(180)
    await expect(frames).toHaveLength(180)
    await expect(titles).toHaveLength(180)
    await expect(getComputedStyle(frames[0]).width).toBe('230px')
    await expect(getComputedStyle(frames[0]).height).toBe('112px')
    await expect(getComputedStyle(frames[0]).fontSize).toBe('21px')
    await expect(getComputedStyle(titles[0]).webkitLineClamp).toBe('2')

    for (let index = 0; index < titles.length; index += 1) {
      await expect(titles[index]).toHaveTextContent(insightsNodeTitleCorpus[index].title)
    }
  },
}

export const LeafWidth: Story = {
  args: {
    childCount: 0,
  },
}
