import type { AnchorHTMLAttributes, HTMLAttributes, ThHTMLAttributes, TdHTMLAttributes } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { cn } from '@/lib/utils'

type MarkdownViewerProps = {
  content: string
  className?: string
}

export function MarkdownViewer({ content, className }: MarkdownViewerProps) {
  let sectionHeadingIndex = 0

  return (
    <div
      className={cn(
        'prose prose-sm max-w-none text-foreground prose-headings:mb-3 prose-headings:tracking-tight prose-p:my-4 prose-p:text-sm prose-p:leading-6 prose-ul:my-4 prose-ul:list-disc prose-ul:pl-6 prose-ol:my-4 prose-ol:list-decimal prose-ol:pl-6 prose-li:my-1 prose-li:pl-1 prose-li:text-sm prose-li:leading-6 prose-li:marker:text-muted-foreground prose-strong:text-foreground prose-code:rounded prose-code:bg-muted prose-code:px-1 prose-code:py-0.5 prose-code:text-[0.92em] prose-code:before:content-none prose-code:after:content-none prose-pre:overflow-x-auto prose-pre:rounded-xl prose-pre:border prose-pre:border-border prose-pre:bg-muted/70 prose-pre:px-4 prose-pre:py-3 prose-blockquote:my-5 prose-blockquote:border-l-2 prose-blockquote:border-primary/35 prose-blockquote:pl-4 prose-blockquote:text-muted-foreground prose-hr:my-8 prose-hr:border-border prose-a:text-primary prose-a:no-underline hover:prose-a:underline dark:prose-invert',
        className,
      )}
    >
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          a: MarkdownLink,
          h1: (props) => {
            const headingIndex = sectionHeadingIndex++
            return <SectionHeading level={1} separated={headingIndex > 0} {...props} />
          },
          h2: (props) => {
            const headingIndex = sectionHeadingIndex++
            return <SectionHeading level={2} separated={headingIndex > 0} {...props} />
          },
          h3: (props) => {
            const headingIndex = sectionHeadingIndex++
            return <SectionHeading level={3} separated={headingIndex > 0} {...props} />
          },
          code: InlineCode,
          pre: CodeBlock,
          ul: MarkdownUnorderedList,
          ol: MarkdownOrderedList,
          li: MarkdownListItem,
          table: MarkdownTable,
          thead: MarkdownTableHead,
          tbody: MarkdownTableBody,
          tr: MarkdownTableRow,
          th: MarkdownTableHeaderCell,
          td: MarkdownTableDataCell,
        }}
      >
        {content}
      </ReactMarkdown>
    </div>
  )
}

function MarkdownLink(props: AnchorHTMLAttributes<HTMLAnchorElement>) {
  const isExternal = !!props.href && /^https?:\/\//i.test(props.href)

  return (
    <a
      {...props}
      target={isExternal ? '_blank' : props.target}
      rel={isExternal ? 'noreferrer noopener' : props.rel}
    />
  )
}

function InlineCode(props: HTMLAttributes<HTMLElement> & { inline?: boolean }) {
  const { className, children, inline, ...rest } = props

  if (inline) {
    return (
      <code className={className} {...rest}>
        {children}
      </code>
    )
  }

  return (
    <code className={cn('block whitespace-pre-wrap font-mono text-sm', className)} {...rest}>
      {children}
    </code>
  )
}

function CodeBlock(props: HTMLAttributes<HTMLPreElement>) {
  return <pre {...props} />
}

function MarkdownUnorderedList(props: React.ComponentProps<'ul'>) {
  const { className, ...rest } = props
  return <ul className={cn('my-4 list-disc pl-6', className)} {...rest} />
}

function MarkdownOrderedList(props: React.ComponentProps<'ol'>) {
  const { className, ...rest } = props
  return <ol className={cn('my-4 list-decimal pl-6', className)} {...rest} />
}

function MarkdownListItem(props: React.ComponentProps<'li'>) {
  const { className, ...rest } = props
  return <li className={cn('my-1 pl-1 text-sm leading-6 marker:text-muted-foreground', className)} {...rest} />
}

function MarkdownTable(props: React.ComponentProps<'table'>) {
  const { className, ...rest } = props

  return (
    <div className="my-5 overflow-hidden rounded-2xl border border-border/70 bg-card/70">
      <div className="overflow-x-auto">
        <Table className={cn('min-w-full text-sm', className)} {...rest} />
      </div>
    </div>
  )
}

function MarkdownTableHead(props: React.ComponentProps<'thead'>) {
  const { className, ...rest } = props
  return <TableHeader className={cn('bg-background/50', className)} {...rest} />
}

function MarkdownTableBody(props: React.ComponentProps<'tbody'>) {
  const { className, ...rest } = props
  return <TableBody className={className} {...rest} />
}

function MarkdownTableRow(props: React.ComponentProps<'tr'>) {
  const { className, ...rest } = props
  return <TableRow className={cn('border-border/60', className)} {...rest} />
}

function MarkdownTableHeaderCell(props: ThHTMLAttributes<HTMLTableCellElement>) {
  const { className, ...rest } = props
  return (
    <TableHead
      className={cn('border-border/60 px-4 py-3 text-left text-xs font-semibold uppercase tracking-[0.12em]', className)}
      {...rest}
    />
  )
}

function MarkdownTableDataCell(props: TdHTMLAttributes<HTMLTableCellElement>) {
  const { className, ...rest } = props
  return (
    <TableCell
      className={cn('border-border/50 px-4 py-3 align-top text-sm leading-6', className)}
      {...rest}
    />
  )
}

function SectionHeading({
  level,
  separated,
  className,
  children,
  ...rest
}: HTMLAttributes<HTMLHeadingElement> & {
  level: 1 | 2 | 3
  separated: boolean
}) {
  const Tag = `h${level}` as const

  return (
        <Tag
      className={cn(
        separated && 'mt-10 border-t border-border/60 pt-6',
        level === 1 && 'text-2xl',
        level === 2 && 'text-xl',
        level === 3 && 'text-lg',
        className,
      )}
      {...rest}
    >
      {children}
    </Tag>
  )
}
