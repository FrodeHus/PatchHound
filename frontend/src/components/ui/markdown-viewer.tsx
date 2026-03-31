import type { AnchorHTMLAttributes, HTMLAttributes } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
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
        'prose prose-sm max-w-none text-foreground prose-headings:mb-3 prose-headings:tracking-tight prose-p:my-4 prose-p:text-sm prose-p:leading-6 prose-ul:my-4 prose-ul:list-disc prose-ul:pl-6 prose-ol:my-4 prose-ol:list-decimal prose-ol:pl-6 prose-li:my-1 prose-li:pl-1 prose-li:text-sm prose-li:leading-6 prose-li:marker:text-muted-foreground prose-strong:text-foreground prose-code:rounded prose-code:bg-muted prose-code:px-1 prose-code:py-0.5 prose-code:text-[0.92em] prose-code:before:content-none prose-code:after:content-none prose-pre:overflow-x-auto prose-pre:rounded-xl prose-pre:border prose-pre:border-border prose-pre:bg-muted/70 prose-pre:px-4 prose-pre:py-3 prose-blockquote:my-5 prose-blockquote:border-l-2 prose-blockquote:border-primary/35 prose-blockquote:pl-4 prose-blockquote:text-muted-foreground prose-hr:my-8 prose-hr:border-border prose-a:text-primary prose-a:no-underline hover:prose-a:underline prose-table:my-5 prose-table:w-full prose-table:border-collapse prose-th:border prose-th:border-border prose-th:bg-muted/50 prose-th:px-3 prose-th:py-2 prose-th:text-left prose-th:text-xs prose-td:border prose-td:border-border prose-td:px-3 prose-td:py-2 dark:prose-invert',
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
