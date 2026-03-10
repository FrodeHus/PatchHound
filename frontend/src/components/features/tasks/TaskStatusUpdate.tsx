import { useMemo, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { taskStatusRequiresJustification, taskUpdateStatusOptions } from '@/lib/options/tasks'

type TaskStatusUpdateProps = {
  currentStatus: string
  isSubmitting: boolean
  onSubmit: (status: string, justification?: string) => void
}

export function TaskStatusUpdate({ currentStatus, isSubmitting, onSubmit }: TaskStatusUpdateProps) {
  const [status, setStatus] = useState(currentStatus)
  const [justification, setJustification] = useState('')
  const shouldRequireJustification = useMemo(() => taskStatusRequiresJustification(status), [status])

  return (
    <div className="space-y-2 rounded-md border border-border/70 bg-muted/30 p-3">
      <label className="block space-y-1 text-xs text-muted-foreground">
        <span>Status</span>
        <Select
          value={status}
          onValueChange={(value) => {
            if (value) {
              setStatus(value)
            }
          }}
        >
          <SelectTrigger className="h-9 w-full rounded-md bg-background px-3">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {taskUpdateStatusOptions.map((value) => (
              <SelectItem key={value} value={value}>
                {value}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </label>

      {shouldRequireJustification ? (
        <label className="block space-y-1 text-xs text-muted-foreground">
          <span>Justification</span>
          <Textarea
            className="min-h-16 rounded-md bg-background"
            value={justification}
            onChange={(event) => {
              setJustification(event.target.value)
            }}
          />
        </label>
      ) : null}

      <Button
        type="button"
        className="w-fit rounded-md"
        disabled={isSubmitting || (shouldRequireJustification && justification.trim().length === 0)}
        onClick={() => {
          onSubmit(status, shouldRequireJustification ? justification.trim() : undefined)
        }}
      >
        {isSubmitting ? 'Updating...' : 'Update status'}
      </Button>
    </div>
  )
}
