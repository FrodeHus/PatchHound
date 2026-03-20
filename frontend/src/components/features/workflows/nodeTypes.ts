import type { NodeTypes } from '@xyflow/react'
import { StartNode } from './nodes/StartNode'
import { EndNode } from './nodes/EndNode'
import { AssignGroupNode } from './nodes/AssignGroupNode'
import { ConditionNode } from './nodes/ConditionNode'
import { SendNotificationNode } from './nodes/SendNotificationNode'
import { MergeNode } from './nodes/MergeNode'
import { SystemTaskNode } from './nodes/SystemTaskNode'
import { WaitForActionNode } from './nodes/WaitForActionNode'

export const nodeTypeMap: NodeTypes = {
  Start: StartNode,
  End: EndNode,
  AssignGroup: AssignGroupNode,
  Condition: ConditionNode,
  SendNotification: SendNotificationNode,
  Merge: MergeNode,
  SystemTask: SystemTaskNode,
  WaitForAction: WaitForActionNode,
}
