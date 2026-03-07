import { createFileRoute } from '@tanstack/react-router'
import { getSession } from '@/server/session'
import { addClient } from '@/server/events'

export const Route = createFileRoute('/api/events')({
  server: {
    handlers: {
      GET: async ({ request }) => {
        const session = await getSession()
        if (!session.userId) {
          return new Response('Unauthorized', { status: 401 })
        }

        const userId = session.userId
        const stream = new ReadableStream({
          start(controller) {
            const encoder = new TextEncoder()

            const send = (event: string, data: unknown) => {
              controller.enqueue(encoder.encode(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`))
            }

            // Send keepalive every 30s
            const keepalive = setInterval(() => {
              controller.enqueue(encoder.encode(': keepalive\n\n'))
            }, 30_000)

            const removeClient = addClient(userId, send)

            // Clean up on disconnect
            request.signal.addEventListener('abort', () => {
              clearInterval(keepalive)
              removeClient()
            })
          },
        })

        return new Response(stream, {
          headers: {
            'Content-Type': 'text/event-stream',
            'Cache-Control': 'no-cache',
            Connection: 'keep-alive',
          },
        })
      },
    },
  },
})
