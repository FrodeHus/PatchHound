import { createFileRoute } from '@tanstack/react-router'
import { sendToUser, broadcastToAll } from '@/server/events'

export const Route = createFileRoute('/api/internal/events')({
  server: {
    handlers: {
      POST: async ({ request }) => {
        // Only allow internal calls (from Docker network)
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
