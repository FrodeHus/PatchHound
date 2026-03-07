import { useEffect, useRef } from 'react'

type SSEEvent = 'NotificationCountUpdated' | 'CriticalVulnerabilityDetected' | 'TaskStatusChanged'

export function useSSE(event: SSEEvent, handler: (data: unknown) => void) {
  const handlerRef = useRef(handler)

  useEffect(() => {
    handlerRef.current = handler
  })

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
        handlerRef.current(data)
      } catch {
        // Ignore parse errors
      }
    })

    return () => {
      eventSource.close()
    }
  }, [event])
}
