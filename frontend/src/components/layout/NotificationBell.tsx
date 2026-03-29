import { useCallback, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { Bell } from 'lucide-react'
import { useSSE } from '@/hooks/useSSE'
import { fetchNotifications, fetchUnreadNotificationCount, markAllNotificationsRead, markNotificationRead } from '@/api/notifications.functions'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { DropdownMenu, DropdownMenuContent, DropdownMenuGroup, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'

export function NotificationBell() {
  const [unreadCountOverride, setUnreadCountOverride] = useState<number | null>(null)
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  const unreadCountQuery = useQuery({
    queryKey: ['notifications', 'count'],
    queryFn: () => fetchUnreadNotificationCount(),
    staleTime: 30_000,
  })

  const notificationsQuery = useQuery({
    queryKey: ['notifications', 'recent'],
    queryFn: () => fetchNotifications({ data: { take: 8 } }),
    staleTime: 15_000,
  })

  const markReadMutation = useMutation({
    mutationFn: async (id: string) => {
      await markNotificationRead({ data: { id } })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['notifications'] })
    },
  })

  const markAllReadMutation = useMutation({
    mutationFn: async () => {
      await markAllNotificationsRead()
    },
    onSuccess: async () => {
      setUnreadCountOverride(0)
      await queryClient.invalidateQueries({ queryKey: ['notifications'] })
    },
  })

  const handleCountUpdated = useCallback((count: unknown) => {
    if (typeof count === 'number') {
      setUnreadCountOverride(count)
    }
  }, [])

  const unreadCount =
    unreadCountOverride ?? (typeof unreadCountQuery.data === 'number' ? unreadCountQuery.data : 0)

  useSSE('NotificationCountUpdated', handleCountUpdated)

  return (
    <DropdownMenu>
      <Tooltip>
        <TooltipTrigger
          render={(
            <DropdownMenuTrigger
              render={
                <Button
                  aria-label="Notifications"
                  variant="ghost"
                  size="icon"
                  className="relative rounded-full border border-border/70 bg-card/70 text-muted-foreground hover:bg-accent/60 hover:text-foreground"
                />
              }
            />
          )}
        >
          <Bell size={18} />
          {unreadCount > 0 ? (
            <span className="absolute -right-1 -top-1 min-w-4 rounded-full bg-destructive px-1 text-center text-[10px] font-semibold text-white shadow-[0_0_0_2px_var(--background)]">
              {unreadCount}
            </span>
          ) : null}
        </TooltipTrigger>
        <TooltipContent>
          {unreadCount > 0 ? `${unreadCount} unread notifications` : 'No unread notifications'}
        </TooltipContent>
      </Tooltip>
      <DropdownMenuContent align="end" className="w-96 rounded-xl border-border/70 bg-popover/95 p-1.5 backdrop-blur">
        <DropdownMenuGroup>
          <DropdownMenuLabel className="px-3 py-2">
            <div className="flex items-start justify-between gap-3">
              <div>
                <p className="text-sm font-medium text-foreground">Notifications</p>
                <p className="text-xs text-muted-foreground">
                  {unreadCount > 0 ? `${unreadCount} unread` : 'All caught up'}
                </p>
              </div>
              {unreadCount > 0 ? (
                <button
                  type="button"
                  className="text-xs font-medium text-primary transition hover:text-primary/80 disabled:pointer-events-none disabled:opacity-50"
                  disabled={markAllReadMutation.isPending}
                  onClick={async (event) => {
                    event.preventDefault()
                    event.stopPropagation()
                    await markAllReadMutation.mutateAsync()
                  }}
                >
                  Mark all as read
                </button>
              ) : null}
            </div>
          </DropdownMenuLabel>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        {notificationsQuery.data && notificationsQuery.data.length > 0 ? (
          notificationsQuery.data.map((item) => (
            <DropdownMenuItem
              key={item.id}
              className="block rounded-lg px-3 py-3"
              onClick={async () => {
                if (!item.readAt) {
                  await markReadMutation.mutateAsync(item.id)
                  setUnreadCountOverride((count) => Math.max(0, (count ?? unreadCount) - 1))
                }
                if (item.path) {
                  void navigate({ to: item.path as never })
                }
              }}
            >
              <div className="space-y-1">
                <div className="flex items-center justify-between gap-3">
                  <span className="text-sm font-medium text-foreground">{item.title}</span>
                  {!item.readAt ? (
                    <span className="rounded-full bg-destructive/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.18em] text-destructive">
                      New
                    </span>
                  ) : null}
                </div>
                <p className="text-xs leading-5 text-muted-foreground">{item.body}</p>
              </div>
            </DropdownMenuItem>
          ))
        ) : (
          <div className="px-3 py-8 text-center text-sm text-muted-foreground">
            No notifications yet.
          </div>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
