import { createFileRoute } from '@tanstack/react-router'
import { sendToUser, broadcastToAll } from '@/server/events'

export const Route = createFileRoute('/api/internal/events')({
  server: {
    handlers: {
      POST: async ({ request }) => {
        const internalToken = request.headers.get('X-Internal-Token')
        if (!internalToken || internalToken !== process.env.INTERNAL_EVENT_SECRET) {
          return new Response('Forbidden', { status: 403 })
        }

        const body = await request.json() as {
          event: string
          data: unknown
          userId?: string
        }

        if (body.userId) {
          sendToUser(body.userId, body.event, body.data)
        } else {
          broadcastToAll(body.event, body.data)
        }

        return Response.json({ ok: true })
      },
    },
  },
})
