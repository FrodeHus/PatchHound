import { createFileRoute } from '@tanstack/react-router'
import { fetchSecurityProfiles } from '@/api/security-profiles.functions'
import { fetchTeams } from '@/api/teams.functions'
import { AssetRuleWizard } from '@/components/features/admin/asset-rules/AssetRuleWizard'

export const Route = createFileRoute('/_authed/admin/asset-rules/new')({
  loader: async () => {
    const [profiles, teams] = await Promise.all([
      fetchSecurityProfiles({ data: { pageSize: 100 } }),
      fetchTeams({ data: { pageSize: 100 } }),
    ])
    return { profiles, teams }
  },
  component: NewAssetRulePage,
})

function NewAssetRulePage() {
  const { profiles, teams } = Route.useLoaderData()

  return (
    <section className="space-y-5">
      <div>
        <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">Asset Rules</p>
        <h1 className="text-2xl font-semibold tracking-[-0.04em]">Create Rule</h1>
      </div>
      <AssetRuleWizard
        mode="create"
        securityProfiles={profiles.items}
        teams={teams.items}
      />
    </section>
  )
}
