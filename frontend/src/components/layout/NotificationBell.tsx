import { useEffect, useState } from 'react'
import { Bell } from 'lucide-react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { getAccessToken } from '@/lib/auth'

const HUB_BASE_URL = import.meta.env.VITE_SIGNALR_URL ?? import.meta.env.VITE_API_URL ?? ''

export function NotificationBell() {
  const [unreadCount, setUnreadCount] = useState(0)

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(`${HUB_BASE_URL}/hubs/notifications`, {
        accessTokenFactory: async () => (await getAccessToken()) ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('NotificationCountUpdated', (count: number) => {
      setUnreadCount(count)
    })

    void connection.start().catch(() => {
      setUnreadCount(0)
    })

    return () => {
      connection.off('NotificationCountUpdated')
      void connection.stop()
    }
  }, [])

  return (
    <button
      type="button"
      aria-label="Notifications"
      className="relative rounded-md p-2 text-muted-foreground hover:bg-muted hover:text-foreground"
    >
      <Bell size={18} />
      {unreadCount > 0 ? (
        <span className="absolute -right-1 -top-1 min-w-4 rounded-full bg-destructive px-1 text-center text-[10px] font-semibold text-white">
          {unreadCount}
        </span>
      ) : null}
    </button>
  )
}
