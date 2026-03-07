import { useEffect } from 'react'

type SSEEvent = 'NotificationCountUpdated' | 'CriticalVulnerabilityDetected' | 'TaskStatusChanged'

export function useSSE(event: SSEEvent, handler: (data: unknown) => void) {
  useEffect(() => {
    const eventSource = new EventSource('/api/events')

    eventSource.addEventListener(event, (receivedEvent: Event) => {
      if (!(receivedEvent instanceof MessageEvent)) {
        return
      }
      if (typeof receivedEvent.data !== 'string') {
        return
      }

      try {
        const data: unknown = JSON.parse(receivedEvent.data)
        handler(data)
      } catch {
        // Ignore parse errors
      }
    })

    return () => {
      eventSource.close()
    }
  }, [event, handler])
}
