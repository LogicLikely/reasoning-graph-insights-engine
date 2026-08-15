import { http, HttpResponse } from 'msw'
import { sampleGraph } from '../src/fixtures/sampleGraph'

export const mswHandlers = {
  graphs: [
    http.get('/api/graphs', () => HttpResponse.json([
      {
        slug: sampleGraph.slug,
        title: sampleGraph.title,
        description: sampleGraph.description,
        nodeCount: sampleGraph.nodes.length,
        edgeCount: sampleGraph.edges.length,
      },
    ])),
    http.get('/api/graphs/sample-medium', () => HttpResponse.json(sampleGraph)),
    http.post('/api/graphs/reset', () => new HttpResponse(null, { status: 204 })),
  ],
}
