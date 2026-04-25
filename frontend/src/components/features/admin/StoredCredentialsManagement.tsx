import { useMemo, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { toast } from 'sonner'
import { KeyRound, Pencil, Plus, Trash2 } from 'lucide-react'
import {
  createStoredCredential,
  deleteStoredCredential,
  fetchStoredCredentials,
  updateStoredCredential,
} from '@/api/stored-credentials.functions'
import { fetchTenants } from '@/api/settings.functions'
import type { CreateStoredCredentialInput, StoredCredential, UpdateStoredCredentialInput } from '@/api/stored-credentials.schemas'
import type { TenantListItem } from '@/api/settings.schemas'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { getApiErrorMessage } from '@/lib/api-errors'

type CredentialFormState = {
  id?: string
  type: string
  name: string
  credentialTenantId: string
  clientId: string
  clientSecret: string
  isGlobal: boolean
  tenantIds: string[]
}

const emptyForm: CredentialFormState = {
  type: 'entra-client-secret',
  name: '',
  credentialTenantId: '',
  clientId: '',
  clientSecret: '',
  isGlobal: false,
  tenantIds: [],
}

export function StoredCredentialsManagement() {
  const [formOpen, setFormOpen] = useState(false)
  const [form, setForm] = useState<CredentialFormState>(emptyForm)
  const [deleteTarget, setDeleteTarget] = useState<StoredCredential | null>(null)

  const credentialsQuery = useQuery({
    queryKey: ['stored-credentials'],
    queryFn: () => fetchStoredCredentials({ data: {} }),
    staleTime: 30_000,
  })

  const tenantsQuery = useQuery({
    queryKey: ['tenants', 'credential-scope'],
    queryFn: () => fetchTenants({ data: { page: 1, pageSize: 200 } }),
    staleTime: 60_000,
  })

  const createMutation = useMutation({
    mutationFn: createStoredCredential,
    onSuccess: async () => {
      toast.success('Credential created')
      closeForm()
      await credentialsQuery.refetch()
    },
    onError: (error) => toast.error(getApiErrorMessage(error, 'Failed to create credential')),
  })

  const updateMutation = useMutation({
    mutationFn: updateStoredCredential,
    onSuccess: async () => {
      toast.success('Credential updated')
      closeForm()
      await credentialsQuery.refetch()
    },
    onError: (error) => toast.error(getApiErrorMessage(error, 'Failed to update credential')),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteStoredCredential,
    onSuccess: async () => {
      toast.success('Credential deleted')
      setDeleteTarget(null)
      await credentialsQuery.refetch()
    },
    onError: (error) => toast.error(getApiErrorMessage(error, 'Failed to delete credential')),
  })

  const credentials = credentialsQuery.data ?? []
  const tenants = tenantsQuery.data?.items ?? []
  const isEditing = Boolean(form.id)
  const isSubmitting = createMutation.isPending || updateMutation.isPending

  function closeForm() {
    setFormOpen(false)
    setForm(emptyForm)
  }

  function openCreate() {
    setForm(emptyForm)
    setFormOpen(true)
  }

  function openEdit(credential: StoredCredential) {
    setForm({
      id: credential.id,
      type: credential.type,
      name: credential.name,
      credentialTenantId: credential.credentialTenantId,
      clientId: credential.clientId,
      clientSecret: '',
      isGlobal: credential.isGlobal,
      tenantIds: credential.tenantIds,
    })
    setFormOpen(true)
  }

  function submitForm() {
    if (isEditing && form.id) {
      const input: UpdateStoredCredentialInput = {
        id: form.id,
        name: form.name,
        isGlobal: form.isGlobal,
        credentialTenantId: form.type === 'api-key' ? '' : form.credentialTenantId,
        clientId: form.type === 'api-key' ? '' : form.clientId,
        clientSecret: form.clientSecret.trim() ? form.clientSecret : null,
        tenantIds: form.isGlobal ? [] : form.tenantIds,
      }
      updateMutation.mutate({ data: input })
      return
    }

    const input: CreateStoredCredentialInput = {
      name: form.name,
      type: form.type,
      isGlobal: form.isGlobal,
      credentialTenantId: form.type === 'api-key' ? '' : form.credentialTenantId,
      clientId: form.type === 'api-key' ? '' : form.clientId,
      clientSecret: form.clientSecret,
      tenantIds: form.isGlobal ? [] : form.tenantIds,
    }
    createMutation.mutate({ data: input })
  }

  return (
    <section className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="space-y-1">
          <h2 className="text-lg font-semibold">Stored credentials</h2>
          <p className="text-sm text-muted-foreground">
            Reusable identities for sources and integrations.
          </p>
        </div>
        <Button type="button" onClick={openCreate}>
          <Plus className="size-4" />
          New credential
        </Button>
      </div>

      <Card className="overflow-hidden rounded-2xl border-border/70">
        <CardHeader className="border-b border-border/60 px-4 py-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <KeyRound className="size-4 text-primary" />
            Credential inventory
          </CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow className="hover:bg-transparent">
                <TableHead className="pl-4">Name</TableHead>
                <TableHead>Type</TableHead>
                <TableHead>Scope</TableHead>
                <TableHead>Client ID</TableHead>
                <TableHead>Updated</TableHead>
                <TableHead className="pr-4 text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {credentials.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={6} className="px-4 py-8 text-center text-sm text-muted-foreground">
                    No stored credentials.
                  </TableCell>
                </TableRow>
              ) : (
                credentials.map((credential) => (
                  <TableRow key={credential.id}>
                    <TableCell className="pl-4 font-medium">{credential.name}</TableCell>
                    <TableCell>{credential.typeDisplayName}</TableCell>
                    <TableCell>
                      <ScopeBadge credential={credential} tenants={tenants} />
                    </TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground">
                      {credential.clientId || '-'}
                    </TableCell>
                    <TableCell>{formatDate(credential.updatedAt)}</TableCell>
                    <TableCell className="pr-4">
                      <div className="flex justify-end gap-2">
                        <Button type="button" variant="ghost" size="icon-sm" onClick={() => openEdit(credential)}>
                          <Pencil className="size-4" />
                        </Button>
                        <Button type="button" variant="ghost" size="icon-sm" onClick={() => setDeleteTarget(credential)}>
                          <Trash2 className="size-4" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      <CredentialDialog
        open={formOpen}
        form={form}
        tenants={tenants}
        isEditing={isEditing}
        isSubmitting={isSubmitting}
        onOpenChange={(open) => {
          if (!open) closeForm()
          else setFormOpen(true)
        }}
        onChange={setForm}
        onSubmit={submitForm}
      />

      <DeleteCredentialDialog
        credential={deleteTarget}
        isDeleting={deleteMutation.isPending}
        onCancel={() => setDeleteTarget(null)}
        onDelete={(credential) => deleteMutation.mutate({ data: { id: credential.id } })}
      />
    </section>
  )
}

function CredentialDialog({
  open,
  form,
  tenants,
  isEditing,
  isSubmitting,
  onOpenChange,
  onChange,
  onSubmit,
}: {
  open: boolean
  form: CredentialFormState
  tenants: TenantListItem[]
  isEditing: boolean
  isSubmitting: boolean
  onOpenChange: (open: boolean) => void
  onChange: (form: CredentialFormState) => void
  onSubmit: () => void
}) {
  const isApiKey = form.type === 'api-key'
  const canSubmit = useMemo(() => {
    if (!form.name.trim()) return false
    if (!isApiKey && (!form.credentialTenantId.trim() || !form.clientId.trim())) return false
    if (!isEditing && !form.clientSecret.trim()) return false
    if (!form.isGlobal && form.tenantIds.length === 0) return false
    return true
  }, [form, isApiKey, isEditing])

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent size="lg">
        <DialogHeader>
          <DialogTitle>{isEditing ? 'Edit credential' : 'New credential'}</DialogTitle>
          <DialogDescription>
            Stored secrets are kept in OpenBao and attached by reference.
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <Field label="Name">
            <Input value={form.name} onChange={(event) => onChange({ ...form, name: event.target.value })} />
          </Field>
          <Field label="Credential type">
            {isEditing ? (
              <Input value={getCredentialTypeDisplayName(form.type)} disabled />
            ) : (
              <Select
                value={form.type}
                onValueChange={(value) => {
                  const nextType = value ?? 'entra-client-secret'
                  onChange({
                    ...form,
                    type: nextType,
                    credentialTenantId: nextType === 'api-key' ? '' : form.credentialTenantId,
                    clientId: nextType === 'api-key' ? '' : form.clientId,
                  })
                }}
              >
                <SelectTrigger className="h-10 w-full rounded-lg border-border/70 bg-background px-3">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent className="rounded-xl border-border/70 bg-popover/95 backdrop-blur">
                  <SelectItem value="entra-client-secret">Entra ID identity</SelectItem>
                  <SelectItem value="api-key">API key</SelectItem>
                </SelectContent>
              </Select>
            )}
          </Field>
          {!isApiKey ? (
            <>
              <Field label="Entra Tenant ID">
                <Input
                  value={form.credentialTenantId}
                  onChange={(event) => onChange({ ...form, credentialTenantId: event.target.value })}
                />
              </Field>
              <Field label="Client ID">
                <Input value={form.clientId} onChange={(event) => onChange({ ...form, clientId: event.target.value })} />
              </Field>
            </>
          ) : null}
          <Field label={isEditing ? `Rotate ${isApiKey ? 'API key' : 'client secret'}` : isApiKey ? 'API key' : 'Client secret'}>
            <Input
              type="password"
              value={form.clientSecret}
              placeholder={
                isEditing
                  ? 'Leave blank to keep existing secret'
                  : isApiKey
                    ? 'Enter API key'
                    : 'Enter client secret'
              }
              onChange={(event) => onChange({ ...form, clientSecret: event.target.value })}
            />
          </Field>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={form.isGlobal}
              onChange={(event) => onChange({ ...form, isGlobal: event.target.checked })}
              className="size-4 rounded border-border"
            />
            Available globally
          </label>
          {!form.isGlobal ? (
            <div className="space-y-2">
              <p className="text-xs uppercase tracking-[0.14em] text-muted-foreground">Available tenants</p>
              <div className="max-h-44 overflow-auto rounded-xl border border-border/70 p-2">
                {tenants.map((tenant) => {
                  const checked = form.tenantIds.includes(tenant.id)
                  return (
                    <label key={tenant.id} className="flex items-center gap-2 rounded-lg px-2 py-1.5 text-sm hover:bg-accent/30">
                      <input
                        type="checkbox"
                        checked={checked}
                        onChange={(event) => {
                          onChange({
                            ...form,
                            tenantIds: event.target.checked
                              ? [...form.tenantIds, tenant.id]
                              : form.tenantIds.filter((id) => id !== tenant.id),
                          })
                        }}
                        className="size-4 rounded border-border"
                      />
                      <span>{tenant.name}</span>
                    </label>
                  )
                })}
              </div>
            </div>
          ) : null}
        </div>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="button" disabled={!canSubmit || isSubmitting} onClick={onSubmit}>
            {isSubmitting ? 'Saving…' : isEditing ? 'Save credential' : 'Create credential'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function DeleteCredentialDialog({
  credential,
  isDeleting,
  onCancel,
  onDelete,
}: {
  credential: StoredCredential | null
  isDeleting: boolean
  onCancel: () => void
  onDelete: (credential: StoredCredential) => void
}) {
  return (
    <Dialog open={credential !== null} onOpenChange={(open) => !open && onCancel()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Delete credential?</DialogTitle>
          <DialogDescription>
            Attached credentials cannot be deleted until they are detached from sources.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onCancel}>
            Cancel
          </Button>
          <Button
            type="button"
            variant="destructive"
            disabled={!credential || isDeleting}
            onClick={() => credential && onDelete(credential)}
          >
            {isDeleting ? 'Deleting…' : 'Delete'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function ScopeBadge({ credential, tenants }: { credential: StoredCredential; tenants: TenantListItem[] }) {
  if (credential.isGlobal) {
    return <Badge className="rounded-full">Global</Badge>
  }

  const names = credential.tenantIds
    .map((id) => tenants.find((tenant) => tenant.id === id)?.name)
    .filter(Boolean)

  return (
    <Badge variant="outline" className="rounded-full">
      {names.length === 1 ? names[0] : `${credential.tenantIds.length} tenants`}
    </Badge>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="grid gap-2">
      <span className="text-xs uppercase tracking-[0.14em] text-muted-foreground">{label}</span>
      {children}
    </label>
  )
}

function formatDate(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

function getCredentialTypeDisplayName(type: string) {
  if (type === 'api-key') return 'API key'
  if (type === 'entra-client-secret') return 'Entra ID identity'
  return type
}
