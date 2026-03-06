import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'
import { getAccessToken } from '@/lib/auth'

const hubBaseUrl = import.meta.env.VITE_SIGNALR_URL ?? import.meta.env.VITE_API_URL ?? ''

type EventHandler = (...args: unknown[]) => void

export type SignalREventMap = {
  NotificationCountUpdated: (count: number) => void
  CriticalVulnerabilityDetected: (payload: unknown) => void
  TaskStatusChanged: (payload: unknown) => void
}

class SignalRManager {
  private connection: HubConnection | null = null
  private handlers: Map<keyof SignalREventMap, Set<EventHandler>> = new Map()

  async start(): Promise<void> {
    if (!this.connection) {
      this.connection = new HubConnectionBuilder()
        .withUrl(`${hubBaseUrl}/hubs/notifications`, {
          accessTokenFactory: async () => (await getAccessToken()) ?? '',
        })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build()

      this.connection.on('NotificationCountUpdated', (count: number) => {
        this.emit('NotificationCountUpdated', count)
      })
      this.connection.on('CriticalVulnerabilityDetected', (payload: unknown) => {
        this.emit('CriticalVulnerabilityDetected', payload)
      })
      this.connection.on('TaskStatusChanged', (payload: unknown) => {
        this.emit('TaskStatusChanged', payload)
      })
    }

    if (this.connection.state === HubConnectionState.Disconnected) {
      await this.connection.start()
    }
  }

  async stop(): Promise<void> {
    if (this.connection && this.connection.state !== HubConnectionState.Disconnected) {
      await this.connection.stop()
    }
  }

  subscribe<T extends keyof SignalREventMap>(event: T, handler: SignalREventMap[T]): () => void {
    const currentSet = this.handlers.get(event) ?? new Set<EventHandler>()
    currentSet.add(handler as EventHandler)
    this.handlers.set(event, currentSet)

    return () => {
      const handlers = this.handlers.get(event)
      if (!handlers) {
        return
      }

      handlers.delete(handler as EventHandler)
      if (handlers.size === 0) {
        this.handlers.delete(event)
      }
    }
  }

  private emit<T extends keyof SignalREventMap>(event: T, ...args: Parameters<SignalREventMap[T]>) {
    const handlers = this.handlers.get(event)
    if (!handlers) {
      return
    }

    handlers.forEach((handler) => {
      ;(handler as (...handlerArgs: Parameters<SignalREventMap[T]>) => void)(...args)
    })
  }
}

export const signalRManager = new SignalRManager()
