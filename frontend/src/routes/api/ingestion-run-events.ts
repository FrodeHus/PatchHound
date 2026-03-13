import { createFileRoute } from '@tanstack/react-router'
import { getSession, isTokenExpired } from '@/server/session'
import { refreshAccessTokenByRefreshToken } from '@/server/auth'
import { apiGet } from '@/server/api'

type IngestionRunProgressDto = {
  id: string
  startedAt: string
  completedAt: string | null
  status: string
  stagedMachineCount: number
  stagedVulnerabilityCount: number
  stagedSoftwareCount: number
  persistedMachineCount: number
  persistedVulnerabilityCount: number
  persistedSoftwareCount: number
  error: string
  snapshotStatus: string | null
  latestPhase: string | null
  latestBatchNumber: number | null
  latestCheckpointStatus: string | null
  latestRecordsCommitted: number | null
  lastCheckpointCommittedAt: string | null
}

export const Route = createFileRoute('/api/ingestion-run-events' as never)({
  server: {
    handlers: {
      GET: async ({ request }) => {
        const url = new URL(request.url)
        const tenantId = url.searchParams.get('tenantId')
        const sourceKey = url.searchParams.get('sourceKey')
        const runId = url.searchParams.get('runId')

        if (!tenantId || !sourceKey || !runId) {
          return new Response('Missing required parameters', { status: 400 })
        }

        const session = await getSession()
        if (!session.userId || !session.accessToken) {
          return new Response('Unauthorized', { status: 401 })
        }

        if (isTokenExpired(session) && session.refreshToken) {
          try {
            const tokens = await refreshAccessTokenByRefreshToken(session.refreshToken)
            session.accessToken = tokens.access_token
            session.tokenExpiry = Date.now() + tokens.expires_in * 1000
            session.refreshToken = tokens.refresh_token ?? session.refreshToken
            await session.save()
          } catch {
            await session.destroy()
            return new Response('Unauthorized', { status: 401 })
          }
        }

        const encoder = new TextEncoder()
        let intervalId: ReturnType<typeof setInterval> | null = null

        const stream = new ReadableStream({
          async start(controller) {
            const send = (event: string, data: unknown) => {
              controller.enqueue(
                encoder.encode(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`),
              )
            }

            const sendProgress = async () => {
              const progress = await apiGet<IngestionRunProgressDto>(
                `/tenants/${tenantId}/ingestion-sources/${encodeURIComponent(sourceKey)}/runs/${runId}/progress`,
                {
                  token: session.accessToken!,
                  tenantId,
                },
              )

              send('IngestionRunProgress', progress)

              if (progress.completedAt) {
                if (intervalId) {
                  clearInterval(intervalId)
                }
                controller.close()
              }
            }

            try {
              await sendProgress()
            } catch {
              controller.error(new Error('Failed to stream ingestion run progress.'))
              return
            }

            intervalId = setInterval(() => {
              void sendProgress().catch(() => {
                if (intervalId) {
                  clearInterval(intervalId)
                }
                controller.close()
              })
            }, 3000)

            request.signal.addEventListener('abort', () => {
              if (intervalId) {
                clearInterval(intervalId)
              }
            })
          },
          cancel() {
            if (intervalId) {
              clearInterval(intervalId)
            }
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
