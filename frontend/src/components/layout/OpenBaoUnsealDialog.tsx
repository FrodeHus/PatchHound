import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, DialogDescription } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'

type OpenBaoUnsealDialogProps = {
  isOpen: boolean
  isSubmitting: boolean
  errorMessage: string | null
  onClose: () => void
  onSubmit: (keys: [string, string, string]) => void
}

export function OpenBaoUnsealDialog({
  isOpen,
  isSubmitting,
  errorMessage,
  onClose,
  onSubmit,
}: OpenBaoUnsealDialogProps) {
  const [keys, setKeys] = useState<[string, string, string]>(['', '', ''])

  if (!isOpen) {
    return null
  }

  const isDisabled = isSubmitting || keys.some((key) => key.trim().length === 0)

  return (
    <Dialog open={isOpen} onOpenChange={(open) => {
      if (!open) {
        onClose()
      }
    }}>
      <DialogContent className="w-full max-w-xl rounded-2xl border-border/80 bg-card p-0 sm:max-w-xl">
        <DialogHeader className="border-b border-border/60 px-6 py-5">
          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-tone-warning-foreground">
            OpenBao recovery
          </p>
          <DialogTitle className="text-2xl font-semibold tracking-[-0.04em]">Unseal vault</DialogTitle>
          <DialogDescription className="text-sm text-muted-foreground">
            Provide three unseal keys to reopen OpenBao so workers can resume credential-backed ingestion.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 px-6 py-5">
          {keys.map((value, index) => (
            <label key={index} className="block space-y-2">
              <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                Unseal key {index + 1}
              </span>
              <Input
                type="password"
                value={value}
                onChange={(event) => {
                  const nextKeys = [...keys] as [string, string, string]
                  nextKeys[index] = event.target.value
                  setKeys(nextKeys)
                }}
              />
            </label>
          ))}

          {errorMessage ? (
            <div className="rounded-xl border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
              {errorMessage}
            </div>
          ) : null}
        </div>
        <DialogFooter className="border-t border-border/60 bg-card px-6 py-4">
          <Button type="button" variant="outline" onClick={onClose} disabled={isSubmitting}>
            Cancel
          </Button>
          <Button
            type="button"
            disabled={isDisabled}
            onClick={() => {
              onSubmit([
                keys[0].trim(),
                keys[1].trim(),
                keys[2].trim(),
              ])
            }}
          >
            {isSubmitting ? 'Unsealing...' : 'Unseal OpenBao'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
