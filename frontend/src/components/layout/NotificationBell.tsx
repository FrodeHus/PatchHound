import { useCallback, useState } from 'react'
import { Bell } from 'lucide-react'
import { useSSE } from '@/hooks/useSSE'
import { Button } from '@/components/ui/button'

export function NotificationBell() {
  const [unreadCount, setUnreadCount] = useState(0)

  const handleCountUpdated = useCallback((count: unknown) => {
    if (typeof count === 'number') {
      setUnreadCount(count)
    }
  }, [])

  useSSE('NotificationCountUpdated', handleCountUpdated)

  return (
    <Button
      aria-label="Notifications"
      variant="ghost"
      size="icon"
      className="relative rounded-full border border-border/70 bg-card/70 text-muted-foreground hover:bg-accent/60 hover:text-foreground"
      title={unreadCount > 0 ? `${unreadCount} unread notifications` : 'No unread notifications'}
    >
      <Bell size={18} />
      {unreadCount > 0 ? (
        <span className="absolute -right-1 -top-1 min-w-4 rounded-full bg-destructive px-1 text-center text-[10px] font-semibold text-white shadow-[0_0_0_2px_var(--background)]">
          {unreadCount}
        </span>
      ) : null}
    </Button>
  )
}
