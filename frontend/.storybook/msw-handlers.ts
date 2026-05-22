import { http, HttpResponse } from 'msw'
import { sampleGraph } from '../src/fixtures/sampleGraph'

export const mswHandlers = {
  graphs: [
    http.get('/api/graphs/sample-medium', () => HttpResponse.json(sampleGraph)),
  ],
}
