import { useEffect } from 'react'

type SSEEvent = 'NotificationCountUpdated' | 'CriticalVulnerabilityDetected' | 'TaskStatusChanged'

export function useSSE(event: SSEEvent, handler: (data: unknown) => void) {
  useEffect(() => {
    const eventSource = new EventSource('/api/events')

    eventSource.addEventListener(event, (e) => {
      try {
        const data = JSON.parse(e.data)
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
