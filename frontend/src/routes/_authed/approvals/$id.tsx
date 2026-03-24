import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import {
  fetchApprovalTaskDetail,
  resolveApprovalTask,
  markApprovalTaskRead,
} from '@/api/approval-tasks.functions'
import { ApprovalTaskDetail } from '@/components/features/approvals/ApprovalTaskDetail'

export const Route = createFileRoute('/_authed/approvals/$id')({
  loader: ({ params }) =>
    fetchApprovalTaskDetail({ data: { id: params.id } }),
  component: ApprovalTaskDetailRoute,
})

function ApprovalTaskDetailRoute() {
  const { id } = Route.useParams()
  const initialData = Route.useLoaderData()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [vulnPage, setVulnPage] = useState(1)
  const [devicePage, setDevicePage] = useState(1)

  const query = useQuery({
    queryKey: ['approval-task', id, vulnPage, devicePage],
    queryFn: () =>
      fetchApprovalTaskDetail({
        data: {
          id,
          page: vulnPage,
          pageSize: 25,
          devicePage,
          devicePageSize: 25,
        },
      }),
    initialData: vulnPage === 1 && devicePage === 1 ? initialData : undefined,
  })

  const data = query.data ?? initialData
  if (!data) return null

  return (
    <ApprovalTaskDetail
      data={data}
      onResolve={async (action, justification) => {
        await resolveApprovalTask({ data: { id, action, justification } })
        void queryClient.invalidateQueries({ queryKey: ['approval-task', id] })
        void queryClient.invalidateQueries({ queryKey: ['approval-tasks'] })
        void navigate({ to: '/approvals', search: { page: 1, pageSize: 25, status: '', type: '', search: '', showRead: false } })
      }}
      onMarkRead={async () => {
        await markApprovalTaskRead({ data: { id } })
        void queryClient.invalidateQueries({ queryKey: ['approval-task', id] })
        void queryClient.invalidateQueries({ queryKey: ['approval-tasks'] })
      }}
      onVulnPageChange={setVulnPage}
      onDevicePageChange={setDevicePage}
    />
  )
}
