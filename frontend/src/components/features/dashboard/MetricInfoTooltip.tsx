import { CircleQuestionMark } from 'lucide-react'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

export function MetricInfoTooltip({ content }: { content: string }) {
  return (
    <Tooltip>
      <TooltipTrigger className="inline-flex items-center text-muted-foreground/80 transition-colors hover:text-foreground focus-visible:outline-none focus-visible:text-foreground">
        <CircleQuestionMark className="size-4" />
      </TooltipTrigger>
      <TooltipContent className="max-w-sm text-sm">
        {content}
      </TooltipContent>
    </Tooltip>
  )
}
