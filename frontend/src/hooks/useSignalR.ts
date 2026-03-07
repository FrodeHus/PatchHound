import { useSSE } from './useSSE'

type SignalREvent = 'NotificationCountUpdated' | 'CriticalVulnerabilityDetected' | 'TaskStatusChanged'

export function useSignalR<T>(event: SignalREvent, handler: (data: T) => void) {
  useSSE(event, (data) => {
    handler(data as T)
  })
}
