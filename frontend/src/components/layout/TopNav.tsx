import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { useRouter } from '@tanstack/react-router'
import { AlertTriangle, Menu, LogOut, ShieldCheck } from 'lucide-react'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { NotificationBell } from '@/components/layout/NotificationBell'
import { OpenBaoUnsealDialog } from '@/components/layout/OpenBaoUnsealDialog'
import { TenantSelector } from '@/components/layout/TenantSelector'
import { useTenantScope } from '@/components/layout/tenant-scope'
import type { CurrentUser } from '@/server/auth.functions'
import { unsealOpenBao } from '@/server/system.functions'
import { ThemeSelector } from '@/components/layout/ThemeSelector'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

type TopNavProps = {
  user: CurrentUser
  onToggleSidebar: () => void
  onLogout: () => void
}

function getInitials(displayName: string, email: string) {
  const source = displayName.trim() || email.trim()
  return source
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('')
}

export function TopNav({
  user,
  onToggleSidebar,
  onLogout,
}: TopNavProps) {
  const router = useRouter()
  const [isUnsealDialogOpen, setIsUnsealDialogOpen] = useState(false)
  const { selectedTenantId, setSelectedTenantId, tenants } = useTenantScope()
  const canUnsealOpenBao = user.roles.includes('GlobalAdmin')
  const unsealMutation = useMutation({
    mutationFn: (keys: [string, string, string]) => unsealOpenBao({ data: { keys } }),
    onSuccess: async () => {
      setIsUnsealDialogOpen(false)
      await router.invalidate()
    },
  })

  return (
    <header className="sticky top-0 z-20 px-4 pb-4 pt-4 sm:px-6">
      <div className="rounded-[28px] border border-border/70 bg-card/78 p-3 backdrop-blur-xl">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <Button
              type="button"
              variant="outline"
              size="icon"
              className="rounded-full border-border/70 bg-background/55 md:hidden"
              aria-label="Toggle menu"
              onClick={onToggleSidebar}
            >
              <Menu className="size-4" />
            </Button>

            <div>
              <div className="flex items-center gap-2">
                <Badge variant="outline" className="rounded-full border-primary/20 bg-primary/10 text-primary">
                  Live posture
                </Badge>
                <span className="hidden text-xs text-muted-foreground sm:inline">Unified vulnerability operations</span>
              </div>
              <div className="mt-2 flex flex-wrap items-center gap-3">
                <h1 className="text-xl font-semibold tracking-tight text-foreground sm:text-2xl">Security posture command</h1>
                <div
                  className={user.systemStatus?.openBaoSealed
                    ? 'flex items-center gap-2 rounded-full border border-amber-400/25 bg-amber-400/10 px-3 py-1 text-xs text-amber-200'
                    : 'flex items-center gap-2 rounded-full border border-emerald-400/20 bg-emerald-400/10 px-3 py-1 text-xs text-emerald-200'}
                >
                  {user.systemStatus?.openBaoSealed ? <AlertTriangle className="size-3.5" /> : <ShieldCheck className="size-3.5" />}
                  {user.systemStatus?.openBaoSealed ? 'Data ingest paused' : 'Data ingest active'}
                </div>
                {user.systemStatus?.openBaoSealed ? (
                  <Tooltip>
                    <TooltipTrigger
                      render={(
                        <button
                          type="button"
                          className={[
                            'inline-flex items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs font-semibold transition-colors',
                            'border-amber-400/25 bg-amber-400/10 text-amber-200',
                            canUnsealOpenBao ? 'cursor-pointer hover:bg-amber-400/15' : 'cursor-default',
                          ].join(' ')}
                          onClick={() => {
                            if (canUnsealOpenBao) {
                              setIsUnsealDialogOpen(true)
                            }
                          }}
                        />
                      )}
                      disabled={!canUnsealOpenBao}
                    >
                      <AlertTriangle className="size-3.5" />
                      OpenBao sealed
                    </TooltipTrigger>
                    <TooltipContent>
                      {canUnsealOpenBao
                        ? 'OpenBao is sealed. Click to provide unseal keys and resume ingestion.'
                        : 'OpenBao is sealed. A Global Admin must unseal the vault to resume ingestion.'}
                    </TooltipContent>
                  </Tooltip>
                ) : null}
              </div>
            </div>
          </div>

          <div className="flex flex-wrap items-center justify-end gap-2">
            <TenantSelector
              tenants={tenants}
              selectedTenantId={selectedTenantId}
              onSelectTenant={setSelectedTenantId}
            />
            <NotificationBell />
            <DropdownMenu>
              <DropdownMenuTrigger
                render={
                  <Button variant="ghost" className="h-11 rounded-full border border-border/70 bg-background/55 px-2 hover:bg-accent/70" />
                }
              >
                <Avatar className="size-8 border border-border/70">
                  <AvatarFallback className="bg-primary/15 text-xs font-semibold text-primary">
                    {getInitials(user.displayName, user.email)}
                  </AvatarFallback>
                </Avatar>
                <div className="hidden text-left sm:block">
                  <p className="text-sm font-medium leading-none">{user.displayName || user.email}</p>
                  <p className="mt-1 text-xs text-muted-foreground">{user.roles[0] ?? 'Member'}</p>
                </div>
              </DropdownMenuTrigger>
                <DropdownMenuContent align="end" className="w-64 rounded-2xl border-border/70 bg-popover/95 p-2 backdrop-blur">
                  <DropdownMenuGroup>
                    <DropdownMenuLabel className="px-3 py-2">
                    <div className="space-y-1">
                      <p className="text-sm font-medium text-foreground">{user.displayName || 'Signed in'}</p>
                      <p className="text-xs text-muted-foreground">{user.email}</p>
                    </div>
                  </DropdownMenuLabel>
                  </DropdownMenuGroup>
                <DropdownMenuSeparator />
                <div className="px-2 py-2">
                  <ThemeSelector />
                </div>
                <DropdownMenuSeparator />
                <DropdownMenuItem className="rounded-xl px-3 py-2">
                  {user.tenantIds.length} tenant scope{user.tenantIds.length === 1 ? '' : 's'}
                </DropdownMenuItem>
                <DropdownMenuItem className="rounded-xl px-3 py-2" onClick={onLogout}>
                  <LogOut className="size-4" />
                  Logout
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>
      </div>
      <OpenBaoUnsealDialog
        isOpen={isUnsealDialogOpen}
        isSubmitting={unsealMutation.isPending}
        errorMessage={unsealMutation.error instanceof Error ? unsealMutation.error.message : null}
        onClose={() => {
          if (!unsealMutation.isPending) {
            setIsUnsealDialogOpen(false)
            unsealMutation.reset()
          }
        }}
        onSubmit={(keys) => {
          unsealMutation.mutate(keys)
        }}
      />
    </header>
  )
}
