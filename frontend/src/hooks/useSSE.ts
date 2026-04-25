import { useEffect, useRef } from 'react'

type SSEEvent =
  | 'NotificationCountUpdated'
  | 'CriticalVulnerabilityDetected'
  | 'TaskStatusChanged'
  | 'IngestionRunProgress'
  | 'TenantDeleted'
  | 'TenantDeletionFailed'

export function useSSE(
  event: SSEEvent,
  handler: (data: unknown) => void,
  options?: {
    url?: string
    enabled?: boolean
  },
) {
  const handlerRef = useRef(handler)

  useEffect(() => {
    handlerRef.current = handler
  })

  useEffect(() => {
    if (options?.enabled === false) {
      return
    }

    const eventSource = new EventSource(options?.url ?? '/api/events')

    eventSource.addEventListener(event, (receivedEvent: Event) => {
      if (!(receivedEvent instanceof MessageEvent)) {
        return
      }
      if (typeof receivedEvent.data !== 'string') {
        return
      }

      try {
        const data: unknown = JSON.parse(receivedEvent.data)
        handlerRef.current(data)
      } catch {
        // Ignore parse errors
      }
    })

    return () => {
      eventSource.close()
    }
  }, [event, options?.enabled, options?.url])
}
