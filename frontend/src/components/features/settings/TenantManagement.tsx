import { useState } from 'react'
import type { TenantItem } from '@/api/settings.schemas'

type TenantManagementProps = {
  tenants: TenantItem[]
  onSaveSettings: (tenantId: string, settings: string) => void
  isSaving: boolean
}

export function TenantManagement({ tenants, onSaveSettings, isSaving }: TenantManagementProps) {
  const [draftSettings, setDraftSettings] = useState<Record<string, string>>({})

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <h3 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Tenant Settings</h3>

      <div className="mt-2 space-y-3">
        {tenants.length === 0 ? <p className="text-sm text-muted-foreground">No tenants found.</p> : null}
        {tenants.map((tenant) => {
          const value = draftSettings[tenant.id] ?? tenant.settings
          return (
            <article key={tenant.id} className="rounded-md border border-border/70 p-3">
              <p className="font-medium">{tenant.name}</p>
              <p className="text-xs text-muted-foreground">Entra ID: {tenant.entraTenantId}</p>
              <textarea
                className="mt-2 min-h-32 w-full rounded-md border border-input bg-background px-2 py-1.5 text-xs"
                value={value}
                onChange={(event) => {
                  setDraftSettings((current) => ({
                    ...current,
                    [tenant.id]: event.target.value,
                  }))
                }}
              />
              <button
                type="button"
                className="mt-2 rounded-md border border-input px-3 py-1.5 text-sm hover:bg-muted disabled:opacity-50"
                disabled={isSaving}
                onClick={() => {
                  onSaveSettings(tenant.id, value)
                }}
              >
                {isSaving ? 'Saving...' : 'Save tenant settings'}
              </button>
            </article>
          )
        })}
      </div>
    </section>
  )
}
