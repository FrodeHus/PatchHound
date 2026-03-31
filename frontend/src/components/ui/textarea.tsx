import * as React from 'react'
import {
  MDXEditor,
  type MDXEditorMethods,
  BoldItalicUnderlineToggles,
  CreateLink,
  ListsToggle,
  Separator,
  UndoRedo,
  headingsPlugin,
  linkPlugin,
  listsPlugin,
  markdownShortcutPlugin,
  quotePlugin,
  toolbarPlugin,
} from '@mdxeditor/editor'
import '@mdxeditor/editor/style.css'

import { cn } from '@/lib/utils'

type TextareaProps = React.ComponentProps<'textarea'>

function Textarea({ className, value, defaultValue, onChange, onBlur, placeholder, disabled, rows, ...props }: TextareaProps) {
  const [mounted, setMounted] = React.useState(false)
  const editorRef = React.useRef<MDXEditorMethods | null>(null)
  const markdown = typeof value === 'string'
    ? value
    : typeof defaultValue === 'string'
      ? defaultValue
      : ''

  React.useEffect(() => {
    setMounted(true)
  }, [])

  React.useEffect(() => {
    if (!mounted || !editorRef.current) {
      return
    }

    const current = editorRef.current.getMarkdown()
    if (current !== markdown) {
      editorRef.current.setMarkdown(markdown)
    }
  }, [markdown, mounted])

  if (!mounted) {
    return (
      <textarea
        data-slot="textarea"
        className={cn(
          'flex min-h-20 w-full min-w-0 rounded-lg border border-input bg-transparent px-2.5 py-2 text-base transition-colors outline-none placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:pointer-events-none disabled:cursor-not-allowed disabled:bg-input/50 disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 md:text-sm dark:bg-input/30 dark:disabled:bg-input/80 dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40',
          className,
        )}
        value={markdown}
        onChange={onChange}
        onBlur={onBlur}
        placeholder={placeholder}
        disabled={disabled}
        rows={rows}
        {...props}
      />
    )
  }

  return (
    <div
      data-slot="textarea"
      className={cn(
        'rounded-lg border border-input bg-transparent transition-colors focus-within:border-ring focus-within:ring-3 focus-within:ring-ring/50 disabled:pointer-events-none disabled:cursor-not-allowed disabled:bg-input/50 disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 dark:bg-input/30 dark:disabled:bg-input/80 dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40',
        className,
      )}
      aria-disabled={disabled}
    >
      <MDXEditor
        ref={editorRef}
        markdown={markdown}
        readOnly={disabled}
        placeholder={placeholder}
        onBlur={(event) => onBlur?.(event as unknown as React.FocusEvent<HTMLTextAreaElement>)}
        onChange={(next) => {
          onChange?.({
            target: { value: next },
            currentTarget: { value: next },
          } as React.ChangeEvent<HTMLTextAreaElement>)
        }}
        className="ph-markdown-editor text-sm"
        contentEditableClassName={cn(
          'prose prose-sm dark:prose-invert max-w-none min-h-20 px-3 py-2 text-foreground outline-none prose-p:my-2 prose-p:leading-6 prose-ul:my-2 prose-ul:list-disc prose-ul:pl-6 prose-ol:my-2 prose-ol:list-decimal prose-ol:pl-6 prose-li:my-1 prose-li:pl-1 prose-li:marker:text-muted-foreground prose-blockquote:border-l-2 prose-blockquote:border-border prose-blockquote:pl-3 prose-code:rounded prose-code:bg-muted prose-code:px-1 prose-code:py-0.5 prose-pre:rounded-lg prose-pre:border prose-pre:border-border prose-pre:bg-muted/60',
          rows && rows > 5 ? 'min-h-44' : rows && rows > 3 ? 'min-h-32' : 'min-h-24',
        )}
        plugins={[
          headingsPlugin({ allowedHeadingLevels: [1, 2, 3] }),
          listsPlugin(),
          quotePlugin(),
          linkPlugin(),
          markdownShortcutPlugin(),
          toolbarPlugin({
            toolbarContents: () => (
              <>
                <UndoRedo />
                <Separator />
                <BoldItalicUnderlineToggles />
                <Separator />
                <ListsToggle />
                <Separator />
                <CreateLink />
              </>
            ),
          }),
        ]}
      />
      <textarea
        {...props}
        value={markdown}
        readOnly
        tabIndex={-1}
        className="sr-only"
      />
    </div>
  )
}

export { Textarea }
