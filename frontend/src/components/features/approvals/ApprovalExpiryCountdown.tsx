import { useMemo } from 'react'

function getTimeRemaining(expiresAt: string) {
  const now = Date.now()
  const expires = new Date(expiresAt).getTime()
  const diffMs = expires - now

  if (diffMs <= 0) return { expired: true, hours: 0, minutes: 0, label: 'Expired' }

  const totalMinutes = Math.floor(diffMs / 60000)
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60

  let label: string
  if (hours > 0) {
    label = `${hours}h ${minutes}m`
  } else {
    label = `${minutes}m`
  }

  return { expired: false, hours, minutes: totalMinutes, label }
}

function getCountdownColor(expired: boolean, totalMinutes: number) {
  if (expired) return 'text-muted-foreground'
  if (totalMinutes > 720) return 'text-tone-success-foreground'
  if (totalMinutes > 240) return 'text-tone-warning-foreground'
  return 'text-tone-danger-foreground'
}

export function ApprovalExpiryCountdown({
  expiresAt,
  compact = false,
}: {
  expiresAt: string
  compact?: boolean
}) {
  const remaining = useMemo(() => getTimeRemaining(expiresAt), [expiresAt])
  const color = getCountdownColor(remaining.expired, remaining.minutes)

  if (compact) {
    return <span className={`text-xs font-medium ${color}`}>{remaining.label}</span>
  }

  return (
    <div className="space-y-0.5">
      <p className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">
        {remaining.expired ? 'Expired' : 'Expires in'}
      </p>
      <p className={`text-sm font-semibold tabular-nums ${color}`}>{remaining.label}</p>
    </div>
  )
}
