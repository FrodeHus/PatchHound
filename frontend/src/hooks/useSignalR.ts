import { useEffect } from 'react'
import { signalRManager, type SignalREventMap } from '@/lib/signalr'

export function useSignalR<T extends keyof SignalREventMap>(
  event: T,
  handler: SignalREventMap[T],
) {
  useEffect(() => {
    const unsubscribe = signalRManager.subscribe(event, handler)

    void signalRManager.start().catch(() => {
      // Best effort; app should keep functioning without live updates.
    })

    return () => {
      unsubscribe()
    }
  }, [event, handler])
}
