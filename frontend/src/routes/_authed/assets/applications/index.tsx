import { createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { fetchCloudApplications } from '@/api/cloud-applications.functions'
import type { CloudApplicationListItem } from '@/api/cloud-applications.schemas'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { baseListSearchSchema, searchStringSchema } from '@/routes/-list-search'
import { createListSearchUpdater } from '@/routes/-list-search-helpers'
import { DataTable } from '@/components/ui/data-table'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { PaginationControls } from '@/components/ui/pagination-controls'
import type { ColumnDef } from '@tanstack/react-table'
import { z } from 'zod'

const applicationsSearchSchema = baseListSearchSchema.extend({
  search: searchStringSchema,
  credentialFilter: searchStringSchema,
})

type ApplicationsSearch = z.infer<typeof applicationsSearchSchema>

export const Route = createFileRoute('/_authed/assets/applications/')({
  validateSearch: applicationsSearchSchema,
  loaderDeps: ({ search }) => search,
  loader: async ({ deps }) =>
    fetchCloudApplications({
      data: {
        search: deps.search || undefined,
        credentialFilter: deps.credentialFilter || undefined,
        page: deps.page,
        pageSize: deps.pageSize,
      },
    }),
  component: ApplicationsPage,
})

function formatExpiryDuration(expiresAt: string | null): string {
  if (!expiresAt) return '—'
  const now = new Date()
  const expiry = new Date(expiresAt)
  const diffMs = expiry.getTime() - now.getTime()
  if (diffMs < 0) return 'Expired'
  const days = Math.floor(diffMs / (1000 * 60 * 60 * 24))
  if (days === 0) return 'Today'
  if (days === 1) return '1 day'
  if (days < 7) return `${days} days`
  if (days < 14) return '1 week'
  if (days < 30) return `${Math.floor(days / 7)} weeks`
  if (days < 60) return '1 month'
  return `${Math.floor(days / 30)} months`
}

function formatExpiryDate(expiresAt: string | null): string {
  if (!expiresAt) return '—'
  return new Date(expiresAt).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

const columns: ColumnDef<CloudApplicationListItem>[] = [
  {
    accessorKey: 'name',
    header: 'Application',
    cell: ({ row }) => (
      <div>
        <div className="font-medium text-sm">{row.original.name}</div>
        {row.original.description && (
          <div className="text-xs text-muted-foreground truncate max-w-xs">{row.original.description}</div>
        )}
      </div>
    ),
  },
  {
    id: 'credentials',
    header: 'Credentials',
    cell: ({ row }) => {
      const item = row.original
      if (item.credentialCount === 0) {
        return <span className="text-xs text-muted-foreground">None</span>
      }
      return (
        <div className="flex items-center gap-1.5">
          <span className="text-sm">{item.credentialCount}</span>
          {item.expiredCredentialCount > 0 && (
            <Badge variant="destructive" className="text-xs px-1.5 py-0">
              {item.expiredCredentialCount} expired
            </Badge>
          )}
          {item.expiringCredentialCount > 0 && (
            <Badge variant="outline" className="text-xs px-1.5 py-0 border-yellow-500 text-yellow-600">
              {item.expiringCredentialCount} expiring
            </Badge>
          )}
        </div>
      )
    },
  },
  {
    id: 'nextExpiry',
    header: 'Next expiry',
    cell: ({ row }) => {
      const item = row.original
      const isExpired = item.expiredCredentialCount > 0
      const duration = formatExpiryDuration(item.nextExpiryAt)
      const date = formatExpiryDate(item.nextExpiryAt)

      if (!item.nextExpiryAt) {
        return isExpired ? (
          <span className="text-xs text-destructive font-medium">All expired</span>
        ) : (
          <span className="text-xs text-muted-foreground">—</span>
        )
      }

      const isExpiredDuration = duration === 'Expired'
      const isExpiringSoon = item.expiringCredentialCount > 0 && !isExpiredDuration

      return (
        <div>
          <div
            className={
              isExpiredDuration
                ? 'text-sm font-medium text-destructive'
                : isExpiringSoon
                  ? 'text-sm font-medium text-yellow-600'
                  : 'text-sm font-medium'
            }
          >
            {duration}
          </div>
          <div className="text-xs text-muted-foreground">{date}</div>
        </div>
      )
    },
  },
]

function ApplicationsPage() {
  const initialData = Route.useLoaderData()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId
  const searchActions = createListSearchUpdater<ApplicationsSearch>(navigate)

  const query = useQuery({
    queryKey: ['cloud-applications', selectedTenantId, search],
    queryFn: () =>
      fetchCloudApplications({
        data: {
          search: search.search || undefined,
          credentialFilter: search.credentialFilter || undefined,
          page: search.page,
          pageSize: search.pageSize,
        },
      }),
    initialData: canUseInitialData ? initialData : undefined,
  })

  const data = query.data ?? (canUseInitialData ? initialData : undefined)
  const items = data?.items ?? []
  const activeFilter = search.credentialFilter || ''

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">Applications</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Entra app registrations and their credential expiry status.
          </p>
        </div>
      </div>

      <div className="flex items-center gap-2">
        <Input
          placeholder="Search applications…"
          value={search.search}
          onChange={(e) => searchActions.updateField('search', e.target.value)}
          className="max-w-xs h-8 text-sm"
        />
        <div className="flex items-center gap-1">
          <Button
            variant={activeFilter === 'expired' ? 'default' : 'outline'}
            size="sm"
            className="h-8"
            onClick={() =>
              searchActions.updateField('credentialFilter', activeFilter === 'expired' ? '' : 'expired')
            }
          >
            Expired
          </Button>
          <Button
            variant={activeFilter === 'expiring-soon' ? 'default' : 'outline'}
            size="sm"
            className="h-8"
            onClick={() =>
              searchActions.updateField(
                'credentialFilter',
                activeFilter === 'expiring-soon' ? '' : 'expiring-soon',
              )
            }
          >
            Expiring soon
          </Button>
        </div>
        {data && (
          <span className="text-xs text-muted-foreground ml-auto">
            {data.totalCount.toLocaleString()} application{data.totalCount !== 1 ? 's' : ''}
          </span>
        )}
      </div>

      <DataTable
        columns={columns}
        data={items}
        getRowId={(row) => row.id}
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        onRowClick={(row) => navigate({ to: '/assets/applications/$id', params: { id: row.id } } as any)}
        emptyState={
          <div className="py-12 text-center text-sm text-muted-foreground">
            {activeFilter
              ? 'No applications match the selected filter.'
              : 'No applications have been ingested yet.'}
          </div>
        }
      />

      {data && data.totalPages > 1 && (
        <PaginationControls
          page={data.page}
          pageSize={data.pageSize}
          totalCount={data.totalCount}
          totalPages={data.totalPages}
          onPageChange={(p) => searchActions.updatePage(p)}
          onPageSizeChange={(ps) => searchActions.updatePageSize(ps)}
        />
      )}
    </div>
  )
}
