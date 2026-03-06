import { createFileRoute } from '@tanstack/react-router'
import { useUpdateTenantSettings, useTenants } from '@/api/useSettings'
import { AiProviderForm } from '@/components/features/settings/AiProviderForm'
import { FallbackChainForm } from '@/components/features/settings/FallbackChainForm'
import { SlaConfigForm } from '@/components/features/settings/SlaConfigForm'
import { TenantManagement } from '@/components/features/settings/TenantManagement'

export const Route = createFileRoute('/settings/')({
  component: SettingsPage,
})

function SettingsPage() {
  const tenantsQuery = useTenants(1, 100)
  const updateSettingsMutation = useUpdateTenantSettings()

  return (
    <section className="space-y-4">
      <h1 className="text-2xl font-semibold">Settings</h1>

      <SlaConfigForm
        onApply={(criticalDays, highDays, mediumDays, lowDays) => {
          console.info('SLA config updated', { criticalDays, highDays, mediumDays, lowDays })
        }}
      />

      <FallbackChainForm
        onApply={(fallbackTeamId, defaultTeamId) => {
          console.info('Fallback chain updated', { fallbackTeamId, defaultTeamId })
        }}
      />

      <AiProviderForm
        onApply={(provider, model) => {
          console.info('AI provider updated', { provider, model })
        }}
      />

      {tenantsQuery.isLoading ? <p className="text-sm text-muted-foreground">Loading tenant settings...</p> : null}
      {tenantsQuery.isError ? <p className="text-sm text-destructive">Failed to load tenant settings.</p> : null}
      {tenantsQuery.data ? (
        <TenantManagement
          tenants={tenantsQuery.data.items}
          isSaving={updateSettingsMutation.isPending}
          onSaveSettings={(tenantId, settings) => {
            updateSettingsMutation.mutate({ tenantId, settings })
          }}
        />
      ) : null}
    </section>
  )
}
