import type { PagedApprovalTaskList } from '@/api/approval-tasks.schemas'
import { ApprovalWorkbench } from './ApprovalWorkbench'
import {
  genericApprovalInboxConfig,
  type ApprovalWorkbenchFilters,
} from './approval-workbench-config'

type Props = {
  data: PagedApprovalTaskList
  filters: ApprovalWorkbenchFilters
  onFiltersChange: (filters: ApprovalWorkbenchFilters) => void
  onPageChange: (page: number) => void
  onMarkRead: (id: string) => void
}

export function ApprovalInbox(props: Props) {
  return <ApprovalWorkbench config={genericApprovalInboxConfig} {...props} />
}
