import { createFileRoute } from '@tanstack/react-router'
import { fetchAssetRule } from '@/api/asset-rules.functions'
import { fetchSecurityProfiles } from '@/api/security-profiles.functions'
import { fetchTeams } from '@/api/teams.functions'
import { AssetRuleWizard } from '@/components/features/admin/asset-rules/AssetRuleWizard'

export const Route = createFileRoute('/_authed/admin/asset-rules/$id')({
  loader: async ({ params }) => {
    const [rule, profiles, teams] = await Promise.all([
      fetchAssetRule({ data: { id: params.id } }),
      fetchSecurityProfiles({ data: { pageSize: 100 } }),
      fetchTeams({ data: { pageSize: 100 } }),
    ])
    return { rule, profiles, teams }
  },
  component: EditAssetRulePage,
})

function EditAssetRulePage() {
  const { rule, profiles, teams } = Route.useLoaderData()

  return (
    <section className="space-y-5">
      <div>
        <p className="text-xs uppercase tracking-[0.24em] text-muted-foreground">Asset Rules</p>
        <h1 className="text-2xl font-semibold tracking-[-0.04em]">Edit: {rule.name}</h1>
      </div>
      <AssetRuleWizard
        mode="edit"
        initialData={rule}
        securityProfiles={profiles.items}
        teams={teams.items}
      />
    </section>
  )
}
