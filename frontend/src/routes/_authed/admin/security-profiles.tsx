import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { createFileRoute, useRouter } from '@tanstack/react-router'
import { createSecurityProfile, fetchSecurityProfiles } from '@/api/security-profiles.functions'
import { fetchTenants } from '@/api/settings.functions'

const environmentClassOptions = ['Workstation', 'Server', 'JumpHost', 'Lab', 'Kiosk', 'OT']
const internetReachabilityOptions = ['Internet', 'InternalNetwork', 'AdjacentOnly', 'LocalOnly']
const requirementOptions = ['Low', 'Medium', 'High']

export const Route = createFileRoute('/_authed/admin/security-profiles')({
  loader: async () => {
    const [profiles, tenants] = await Promise.all([
      fetchSecurityProfiles({ data: {} }),
      fetchTenants({ data: { page: 1, pageSize: 100 } }),
    ])

    return {
      profiles,
      tenants: tenants.items,
    }
  },
  component: SecurityProfilesPage,
})

function SecurityProfilesPage() {
  const router = useRouter()
  const data = Route.useLoaderData()
  const [tenantId, setTenantId] = useState(data.tenants[0]?.id ?? '')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [environmentClass, setEnvironmentClass] = useState(environmentClassOptions[0])
  const [internetReachability, setInternetReachability] = useState(internetReachabilityOptions[0])
  const [confidentialityRequirement, setConfidentialityRequirement] = useState(requirementOptions[1])
  const [integrityRequirement, setIntegrityRequirement] = useState(requirementOptions[1])
  const [availabilityRequirement, setAvailabilityRequirement] = useState(requirementOptions[1])
  const tenantNames = new Map(data.tenants.map((tenant) => [tenant.id, tenant.name]))

  const mutation = useMutation({
    mutationFn: async () => {
      await createSecurityProfile({
        data: {
          tenantId,
          name,
          description,
          environmentClass,
          internetReachability,
          confidentialityRequirement,
          integrityRequirement,
          availabilityRequirement,
        },
      })
    },
    onSuccess: async () => {
      setName('')
      setDescription('')
      await router.invalidate()
    },
  })

  return (
    <section className="space-y-4">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold">Security Profiles</h1>
        <p className="text-sm text-muted-foreground">
          Create reusable environment profiles that PatchHound uses to recalculate effective device vulnerability severity.
        </p>
      </div>

      <section className="rounded-lg border border-border bg-card p-4">
        <h2 className="text-lg font-semibold">Create Security Profile</h2>
        <div className="mt-3 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <select
            className="rounded-md border border-input bg-background px-3 py-2 text-sm"
            value={tenantId}
            onChange={(event) => setTenantId(event.target.value)}
          >
            {data.tenants.map((tenant) => (
              <option key={tenant.id} value={tenant.id}>
                {tenant.name}
              </option>
            ))}
          </select>
          <input className="rounded-md border border-input bg-background px-3 py-2 text-sm" placeholder="Profile name" value={name} onChange={(event) => setName(event.target.value)} />
          <input className="rounded-md border border-input bg-background px-3 py-2 text-sm md:col-span-2" placeholder="Description" value={description} onChange={(event) => setDescription(event.target.value)} />
          <select className="rounded-md border border-input bg-background px-3 py-2 text-sm" value={environmentClass} onChange={(event) => setEnvironmentClass(event.target.value)}>
            {environmentClassOptions.map((option) => <option key={option} value={option}>{option}</option>)}
          </select>
          <select className="rounded-md border border-input bg-background px-3 py-2 text-sm" value={internetReachability} onChange={(event) => setInternetReachability(event.target.value)}>
            {internetReachabilityOptions.map((option) => <option key={option} value={option}>{option}</option>)}
          </select>
          <select className="rounded-md border border-input bg-background px-3 py-2 text-sm" value={confidentialityRequirement} onChange={(event) => setConfidentialityRequirement(event.target.value)}>
            {requirementOptions.map((option) => <option key={option} value={option}>{option}</option>)}
          </select>
          <select className="rounded-md border border-input bg-background px-3 py-2 text-sm" value={integrityRequirement} onChange={(event) => setIntegrityRequirement(event.target.value)}>
            {requirementOptions.map((option) => <option key={option} value={option}>{option}</option>)}
          </select>
          <select className="rounded-md border border-input bg-background px-3 py-2 text-sm" value={availabilityRequirement} onChange={(event) => setAvailabilityRequirement(event.target.value)}>
            {requirementOptions.map((option) => <option key={option} value={option}>{option}</option>)}
          </select>
          <button
            type="button"
            className="rounded-md bg-primary px-3 py-2 text-sm text-primary-foreground hover:opacity-90 disabled:opacity-50"
            disabled={mutation.isPending || !tenantId.trim() || !name.trim()}
            onClick={() => {
              mutation.mutate()
            }}
          >
            {mutation.isPending ? 'Creating...' : 'Create profile'}
          </button>
        </div>
      </section>

      <section className="rounded-lg border border-border bg-card p-4">
        <div className="mb-3 flex items-end justify-between">
          <h2 className="text-lg font-semibold">Profiles</h2>
          <p className="text-xs text-muted-foreground">{data.profiles.totalCount} total</p>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[960px] border-collapse text-sm">
            <thead>
              <tr className="border-b border-border text-left text-muted-foreground">
                <th className="py-2 pr-2">Name</th>
                <th className="py-2 pr-2">Tenant</th>
                <th className="py-2 pr-2">Environment</th>
                <th className="py-2 pr-2">Reachability</th>
                <th className="py-2 pr-2">Requirements</th>
                <th className="py-2 pr-2">Updated</th>
              </tr>
            </thead>
            <tbody>
              {data.profiles.items.length === 0 ? (
                <tr>
                  <td colSpan={6} className="py-3 text-muted-foreground">No security profiles found.</td>
                </tr>
              ) : data.profiles.items.map((profile) => (
                <tr key={profile.id} className="border-b border-border/60">
                  <td className="py-2 pr-2">
                    <div>
                      <p className="font-medium">{profile.name}</p>
                      <p className="text-xs text-muted-foreground">{profile.description ?? 'No description'}</p>
                    </div>
                  </td>
                  <td className="py-2 pr-2">{tenantNames.get(profile.tenantId) ?? profile.tenantId}</td>
                  <td className="py-2 pr-2">{profile.environmentClass}</td>
                  <td className="py-2 pr-2">{profile.internetReachability}</td>
                  <td className="py-2 pr-2">
                    C:{profile.confidentialityRequirement} I:{profile.integrityRequirement} A:{profile.availabilityRequirement}
                  </td>
                  <td className="py-2 pr-2">{new Date(profile.updatedAt).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </section>
  )
}
