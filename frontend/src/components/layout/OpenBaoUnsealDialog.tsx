import { useState } from 'react'

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
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/35 p-4">
      <div className="w-full max-w-xl rounded-[28px] border border-border/70 bg-background p-6 shadow-2xl">
        <div className="space-y-2">
          <p className="text-xs font-semibold uppercase tracking-[0.2em] text-amber-600">
            OpenBao recovery
          </p>
          <h2 className="text-2xl font-semibold tracking-[-0.03em]">Unseal vault</h2>
          <p className="text-sm text-muted-foreground">
            Provide three unseal keys to reopen OpenBao so workers can resume credential-backed ingestion.
          </p>
        </div>

        <div className="mt-5 space-y-3">
          {keys.map((value, index) => (
            <label key={index} className="block space-y-2">
              <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                Unseal key {index + 1}
              </span>
              <input
                type="password"
                className="w-full rounded-xl border border-input bg-background px-3 py-2 text-sm"
                value={value}
                onChange={(event) => {
                  const nextKeys = [...keys] as [string, string, string]
                  nextKeys[index] = event.target.value
                  setKeys(nextKeys)
                }}
              />
            </label>
          ))}
        </div>

        {errorMessage ? (
          <div className="mt-4 rounded-xl border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
            {errorMessage}
          </div>
        ) : null}

        <div className="mt-6 flex items-center justify-between gap-3">
          <button
            type="button"
            className="rounded-full border border-border/70 px-4 py-2 text-sm hover:bg-muted"
            onClick={onClose}
            disabled={isSubmitting}
          >
            Cancel
          </button>
          <button
            type="button"
            className="rounded-full bg-primary px-4 py-2 text-sm text-primary-foreground disabled:opacity-50"
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
          </button>
        </div>
      </div>
    </div>
  )
}
