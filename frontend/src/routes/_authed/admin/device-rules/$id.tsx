import { createFileRoute } from '@tanstack/react-router'
import { fetchDeviceRule } from '@/api/device-rules.functions'
import { fetchScanProfiles } from '@/api/authenticated-scans.functions'
import { fetchBusinessLabels } from '@/api/business-labels.functions'
import { fetchSecurityProfiles } from '@/api/security-profiles.functions'
import { fetchTeams } from '@/api/teams.functions'
import { DeviceRuleWizard } from '@/components/features/admin/device-rules/DeviceRuleWizard'

export const Route = createFileRoute('/_authed/admin/device-rules/$id')({
  loader: async ({ params }) => {
    const [rule, profiles, businessLabels, teams, scanProfiles] = await Promise.all([
      fetchDeviceRule({ data: { id: params.id } }),
      fetchSecurityProfiles({ data: { pageSize: 100 } }),
      fetchBusinessLabels({ data: {} }),
      fetchTeams({ data: { pageSize: 100 } }),
      fetchScanProfiles({ data: { pageSize: 100 } }),
    ])
    return { rule, profiles, businessLabels, teams, scanProfiles }
  },
  component: EditDeviceRulePage,
})

function EditDeviceRulePage() {
  const { rule, profiles, businessLabels, teams, scanProfiles } = Route.useLoaderData()

  return (
    <section className="space-y-5">
      <div>
        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Asset Rules</p>
        <h1 className="text-2xl font-semibold tracking-[-0.04em]">Edit Asset Rule: {rule.name}</h1>
      </div>
      <DeviceRuleWizard
        mode="edit"
        initialData={rule}
        securityProfiles={profiles.items}
        businessLabels={businessLabels}
        teams={teams.items}
        scanProfiles={scanProfiles.items}
      />
    </section>
  )
}
