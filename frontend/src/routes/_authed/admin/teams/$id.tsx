import { useMemo, useState } from 'react'
import { Link, createFileRoute } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import type { FilterGroup } from '@/api/asset-rules.schemas'
import type { TeamMembershipRulePreview } from '@/api/teams.schemas'
import { fetchTeamDetail, previewTeamMembershipRule, updateTeamMembers, updateTeamMembershipRule } from '@/api/teams.functions'
import { fetchUsers } from '@/api/users.functions'
import { AssignmentGroupDetailView } from '@/components/features/admin/AssignmentGroupDetailView'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'

export const Route = createFileRoute('/_authed/admin/teams/$id')({
  loader: async ({ params }) => {
    const team = await fetchTeamDetail({ data: { teamId: params.id } })
    return { team }
  },
  component: AssignmentGroupDetailPage,
})

const emptyRuleFilter: FilterGroup = {
  type: 'group',
  operator: 'AND',
  conditions: [],
}

function AssignmentGroupDetailPage() {
  const { id } = Route.useParams()
  const data = Route.useLoaderData()
  const { user } = Route.useRouteContext()
  const canManageGroup = user?.roles.includes('GlobalAdmin') ?? false

  const teamDetailQuery = useQuery({
    queryKey: ['team-detail', id],
    queryFn: () => fetchTeamDetail({ data: { teamId: id } }),
    initialData: data.team,
  })

  const usersQuery = useQuery({
    queryKey: ['team-member-candidates', teamDetailQuery.data.tenantId],
    queryFn: () =>
      fetchUsers({
        data: {
          page: 1,
          pageSize: 500,
          status: 'Enabled',
        },
      }),
    enabled: canManageGroup,
  })

  const teamSnapshotKey = [
    teamDetailQuery.data.id,
    String(teamDetailQuery.data.isDynamic),
    teamDetailQuery.data.membershipRule?.filterDefinition?.type ?? '',
    teamDetailQuery.data.members.map((member) => member.userId).join(','),
  ].join('|')

  return (
    <AssignmentGroupDetailEditor
      key={teamSnapshotKey}
      id={id}
      team={teamDetailQuery.data}
      canManageGroup={canManageGroup}
      candidateMembers={usersQuery.data?.items ?? []}
    />
  )
}

function AssignmentGroupDetailEditor({
  id,
  team,
  canManageGroup,
  candidateMembers,
}: {
  id: string
  team: Awaited<ReturnType<typeof fetchTeamDetail>>
  canManageGroup: boolean
  candidateMembers: Awaited<ReturnType<typeof fetchUsers>>['items']
}) {
  const queryClient = useQueryClient()
  const [memberSearch, setMemberSearch] = useState('')
  const [selectedMemberId, setSelectedMemberId] = useState('')
  const [isDynamic, setIsDynamic] = useState(team.isDynamic)
  const [ruleFilter, setRuleFilter] = useState<FilterGroup>(() => {
    const filterDefinition = team.membershipRule?.filterDefinition
    return filterDefinition?.type === 'group' ? filterDefinition : emptyRuleFilter
  })
  const [rulePreview, setRulePreview] = useState<TeamMembershipRulePreview | null>(null)
  const [isDynamicConfirmOpen, setIsDynamicConfirmOpen] = useState(false)

  const availableMembers = useMemo(() => {
    const currentMemberIds = new Set(team.members.map((member) => member.userId))
    const query = memberSearch.trim().toLowerCase()

    return candidateMembers
      .filter((userItem) => !currentMemberIds.has(userItem.id))
      .filter((userItem) => {
        if (!query) {
          return true
        }

        return (
          userItem.displayName.toLowerCase().includes(query)
          || userItem.email.toLowerCase().includes(query)
          || (userItem.company ?? '').toLowerCase().includes(query)
        )
      })
      .sort((left, right) => left.displayName.localeCompare(right.displayName))
  }, [candidateMembers, memberSearch, team.members])

  const effectiveSelectedMemberId = availableMembers.some((member) => member.id === selectedMemberId)
    ? selectedMemberId
    : ''

  const updateMembersMutation = useMutation({
    mutationFn: async (payload: { userId: string; action: 'add' | 'remove' }) =>
      updateTeamMembers({
        data: {
          teamId: id,
          ...payload,
        },
      }),
    onSuccess: async (_, variables) => {
      setSelectedMemberId('')
      toast.success(variables.action === 'add' ? 'Member added' : 'Member removed')
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['team-detail', id] }),
        queryClient.invalidateQueries({ queryKey: ['teams'] }),
        queryClient.invalidateQueries({ queryKey: ['team-member-candidates'] }),
      ])
    },
    onError: () => {
      toast.error('Failed to update group membership')
    },
  })

  const previewRuleMutation = useMutation({
    mutationFn: async () =>
      previewTeamMembershipRule({
        data: {
          teamId: id,
          isDynamic,
          acknowledgeMemberReset: false,
          filterDefinition: ruleFilter,
        },
      }),
    onSuccess: (preview) => {
      setRulePreview(preview)
    },
    onError: () => {
      toast.error('Failed to preview membership rule')
    },
  })

  const saveRuleMutation = useMutation({
    mutationFn: async (acknowledgeMemberReset: boolean) =>
      updateTeamMembershipRule({
        data: {
          teamId: id,
          isDynamic,
          acknowledgeMemberReset,
          filterDefinition: ruleFilter,
        },
      }),
    onSuccess: async () => {
      toast.success('Membership rule saved')
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['team-detail', id] }),
        queryClient.invalidateQueries({ queryKey: ['teams'] }),
      ])
    },
    onError: (error) => {
      const message = error instanceof Error ? error.message : 'Failed to save membership rule'
      if (message.includes('will remove all current members')) {
        setIsDynamicConfirmOpen(true)
        return
      }

      toast.error(message)
    },
  })

  return (
    <section className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="space-y-1">
          <div className="text-sm text-muted-foreground">
            <Link
              to="/admin/teams"
              search={{ page: 1, pageSize: 25 }}
              className="underline decoration-border/70 underline-offset-4 hover:decoration-foreground"
            >
              Assignment Groups
            </Link>
            <span className="mx-2">/</span>
            <span>{team.name}</span>
          </div>
          <h1 className="text-2xl font-semibold tracking-[-0.04em]">Assignment group</h1>
        </div>
      </div>

      <AssignmentGroupDetailView
        team={team}
        canManageGroup={canManageGroup}
        availableMembers={availableMembers}
        selectedMemberId={effectiveSelectedMemberId}
        memberSearch={memberSearch}
        ruleFilter={ruleFilter}
        isDynamic={isDynamic}
        rulePreview={rulePreview}
        isUpdatingMembers={updateMembersMutation.isPending}
        isPreviewingRule={previewRuleMutation.isPending}
        isSavingRule={saveRuleMutation.isPending}
        onMemberSearchChange={setMemberSearch}
        onSelectedMemberChange={setSelectedMemberId}
        onAddMember={() => {
          if (!effectiveSelectedMemberId) {
            return
          }

          updateMembersMutation.mutate({
            userId: effectiveSelectedMemberId,
            action: 'add',
          })
        }}
        onRemoveMember={(userId) => {
          updateMembersMutation.mutate({
            userId,
            action: 'remove',
          })
        }}
        onRuleFilterChange={setRuleFilter}
        onDynamicChange={setIsDynamic}
        onPreviewRule={() => {
          previewRuleMutation.mutate()
        }}
        onSaveRule={() => {
          saveRuleMutation.mutate(false)
        }}
      />

      <Dialog open={isDynamicConfirmOpen} onOpenChange={setIsDynamicConfirmOpen}>
        <DialogContent className="max-w-lg">
          <DialogHeader>
            <DialogTitle>Enable dynamic membership?</DialogTitle>
            <DialogDescription>
              This assignment group currently has manually added members. Enabling dynamic membership will remove all current members before applying the rule.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setIsDynamicConfirmOpen(false)}>
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              onClick={async () => {
                try {
                  await saveRuleMutation.mutateAsync(true)
                  setIsDynamicConfirmOpen(false)
                } catch {
                  toast.error('Failed to enable dynamic membership')
                }
              }}
            >
              Enable dynamic membership
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  )
}
