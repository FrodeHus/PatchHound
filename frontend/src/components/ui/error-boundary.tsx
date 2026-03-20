import { useRouter } from '@tanstack/react-router'
import { AlertTriangle } from 'lucide-react'
import { Button } from '@/components/ui/button'

type RouteErrorProps = {
  error: Error
  reset?: () => void
}

export function RouteError({ error, reset }: RouteErrorProps) {
  const router = useRouter()

  return (
    <div className="flex min-h-[50vh] flex-col items-center justify-center gap-4 px-4 text-center">
      <div className="flex size-14 items-center justify-center rounded-2xl border border-destructive/20 bg-destructive/10">
        <AlertTriangle className="size-6 text-destructive" />
      </div>
      <div className="space-y-1">
        <h2 className="text-lg font-semibold">Something went wrong</h2>
        <p className="max-w-md text-sm text-muted-foreground">
          {error.message || 'An unexpected error occurred. Please try again.'}
        </p>
      </div>
      <div className="flex gap-2">
        <Button variant="outline" onClick={() => void router.navigate({ to: '/' })}>
          Go home
        </Button>
        <Button onClick={() => reset ? reset() : window.location.reload()}>
          Try again
        </Button>
      </div>
    </div>
  )
}
