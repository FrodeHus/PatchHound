import { useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'

type CreateTeamDialogProps = {
  isSubmitting: boolean
  tenants: Array<{ id: string; name: string }>
  onCreate: (payload: { name: string; tenantId: string }) => void
}

export function CreateTeamDialog({ isSubmitting, tenants, onCreate }: CreateTeamDialogProps) {
  const [name, setName] = useState('')
  const [tenantId, setTenantId] = useState('')

  return (
    <Card className="rounded-[28px] border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_8%,transparent),transparent_52%),var(--color-card)]">
      <CardHeader className="space-y-3">
        <Badge variant="outline" className="w-fit rounded-full border-primary/20 bg-primary/10 text-primary">
          New Assignment Group
        </Badge>
        <CardTitle className="text-2xl font-semibold tracking-[-0.03em]">
          Create an ownership lane for a tenant, then assign assets into it in bulk.
        </CardTitle>
        <p className="max-w-3xl text-sm leading-6 text-muted-foreground">
          Assignment groups are best used for operational teams such as endpoint operations, server operations, or line-of-business ownership.
        </p>
      </CardHeader>
      <CardContent className="grid gap-5 lg:grid-cols-[minmax(0,1.3fr)_minmax(18rem,0.7fr)]">
        <div className="grid gap-4 md:grid-cols-2">
          <label className="space-y-2">
            <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Assignment Group Name</span>
            <Input
              placeholder="Endpoint Operations"
              value={name}
              onChange={(event) => setName(event.target.value)}
            />
          </label>
          <label className="space-y-2">
            <span className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Tenant</span>
            <select
              className="rounded-xl border border-input bg-background px-3 py-2.5 text-sm"
              value={tenantId}
              onChange={(event) => setTenantId(event.target.value)}
            >
              <option value="">Select tenant</option>
              {tenants.map((tenant) => (
                <option key={tenant.id} value={tenant.id}>
                  {tenant.name}
                </option>
              ))}
            </select>
          </label>
          <div className="md:col-span-2 flex flex-wrap items-center gap-3">
            <Button
              type="button"
              disabled={isSubmitting || name.trim().length === 0 || tenantId.trim().length === 0}
              onClick={() => {
                onCreate({ name: name.trim(), tenantId: tenantId.trim() })
                setName('')
                setTenantId('')
              }}
            >
              {isSubmitting ? 'Creating group...' : 'Create assignment group'}
            </Button>
            <p className="text-sm text-muted-foreground">
              Members can be added afterwards, and assets can be assigned immediately from the detail workspace below.
            </p>
          </div>
        </div>

        <div className="rounded-3xl border border-border/70 bg-background/35 p-4">
          <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Recommended Pattern</p>
          <div className="mt-4 space-y-2">
            <GuideRow label="Scope" value="One tenant per group" />
            <GuideRow label="Naming" value="Use the actual owning function" />
            <GuideRow label="Next step" value="Select the group and bulk-assign assets" />
          </div>
        </div>
      </CardContent>
    </Card>
  )
}

function GuideRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-start justify-between gap-3 rounded-xl border border-border/60 bg-card/40 px-3 py-3">
      <span className="text-sm font-medium text-foreground">{label}</span>
      <span className="max-w-[14rem] text-right text-xs text-muted-foreground">{value}</span>
    </div>
  )
}
