import { useState } from 'react'
import { Plus } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

type CreateTeamDialogProps = {
  isSubmitting: boolean
  tenants: Array<{ id: string; name: string }>
  onCreate: (payload: { name: string; tenantId: string }) => void
}

export function CreateTeamDialog({ isSubmitting, tenants, onCreate }: CreateTeamDialogProps) {
  const [open, setOpen] = useState(false)
  const [name, setName] = useState('')
  const [tenantId, setTenantId] = useState('')

  const canSubmit = name.trim().length > 0 && tenantId.trim().length > 0 && !isSubmitting

  return (
    <>
      <Button type="button" className="rounded-full" onClick={() => setOpen(true)}>
        <Plus className="mr-2 size-4" />
        New assignment group
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent size="lg" className="rounded-3xl border border-border/70 bg-background/95 p-0 backdrop-blur">
          <DialogHeader className="space-y-3 px-6 pt-6">
            <Badge variant="outline" className="w-fit rounded-full border-primary/20 bg-primary/10 text-primary">
              New Assignment Group
            </Badge>
            <DialogTitle className="text-2xl font-semibold tracking-[-0.04em]">
              Create an ownership lane for a tenant
            </DialogTitle>
            <DialogDescription className="max-w-2xl text-sm leading-6">
              Use assignment groups for the actual operating function that owns assets and remediation work, then manage members and rules from the group workspace.
            </DialogDescription>
          </DialogHeader>
          <div className="grid gap-6 px-6 pb-6 pt-2 lg:grid-cols-[minmax(0,1.2fr)_minmax(18rem,0.8fr)]">
            <div className="grid gap-4">
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
                <Select value={tenantId} onValueChange={(value) => setTenantId(value ?? '')}>
                  <SelectTrigger className="h-10 w-full rounded-xl bg-background px-3">
                    <SelectValue placeholder="Select tenant" />
                  </SelectTrigger>
                  <SelectContent className="rounded-2xl border-border/70 bg-popover/95 backdrop-blur">
                    {tenants.map((tenant) => (
                      <SelectItem key={tenant.id} value={tenant.id}>
                        {tenant.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </label>
            </div>
            <div className="rounded-3xl border border-border/70 bg-muted/20 p-4">
              <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">Recommended Pattern</p>
              <div className="mt-4 space-y-2">
                <GuideRow label="Scope" value="One tenant per group" />
                <GuideRow label="Naming" value="Use the actual owning function" />
                <GuideRow label="Next step" value="Open the workspace and add members or rules" />
              </div>
            </div>
          </div>
          <DialogFooter className="rounded-b-3xl">
            <Button type="button" variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button
              type="button"
              disabled={!canSubmit}
              onClick={() => {
                onCreate({ name: name.trim(), tenantId: tenantId.trim() })
                setName('')
                setTenantId('')
                setOpen(false)
              }}
            >
              {isSubmitting ? 'Creating group...' : 'Create assignment group'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}

function GuideRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-start justify-between gap-3 rounded-xl border border-border/60 bg-card/75 px-3 py-3">
      <span className="text-sm font-medium text-foreground">{label}</span>
      <span className="max-w-[14rem] text-right text-xs text-muted-foreground">{value}</span>
    </div>
  )
}
