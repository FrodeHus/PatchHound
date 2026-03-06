import { useCallback, useState } from 'react'
import { Bell } from 'lucide-react'
import { useSignalR } from '@/hooks/useSignalR'

export function NotificationBell() {
  const [unreadCount, setUnreadCount] = useState(0)

  const handleCountUpdated = useCallback((count: number) => {
    setUnreadCount(count)
  }, [])

  useSignalR('NotificationCountUpdated', handleCountUpdated)

  return (
    <button
      type="button"
      aria-label="Notifications"
      className="relative rounded-md p-2 text-muted-foreground hover:bg-muted hover:text-foreground"
      title={unreadCount > 0 ? `${unreadCount} unread notifications` : 'No unread notifications'}
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
