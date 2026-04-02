import CodeMirror from '@uiw/react-codemirror'
import { autocompletion, completeFromList, type Completion } from '@codemirror/autocomplete'
import { HighlightStyle, LanguageSupport, StreamLanguage, syntaxHighlighting } from '@codemirror/language'
import { tags } from '@lezer/highlight'
import { EditorView } from '@codemirror/view'
import { useMemo } from 'react'
import { cn } from '@/lib/utils'

const kqlKeywords = [
  'where',
  'project',
  'extend',
  'summarize',
  'join',
  'on',
  'kind',
  'take',
  'limit',
  'order',
  'by',
  'desc',
  'asc',
  'distinct',
  'count',
  'contains',
  'has',
  'in',
  'startswith',
  'endswith',
  'matches',
  'parse_json',
  'tolower',
  'toupper',
  'ago',
  'datetime',
  'let',
]

const kqlTables = [
  'DeviceInfo',
  'DeviceTvmSoftwareInventory',
  'DeviceTvmSoftwareVulnerabilities',
  'DeviceTvmSoftwareEvidenceBeta',
  'DeviceProcessEvents',
  'DeviceFileEvents',
  'DeviceRegistryEvents',
  'DeviceNetworkEvents',
  'AlertInfo',
]

const parameterDetails: Record<string, string> = {
  deviceName: 'Device name',
  deviceId: 'Defender device id',
  'vuln.name': 'Vulnerability external id / CVE',
  'vuln.vendor': 'Software vendor from vulnerability context',
  'vuln.product': 'Software product from vulnerability context',
  'vuln.version': 'Software version from vulnerability context',
}

const kqlHighlightStyle = HighlightStyle.define([
  { tag: tags.keyword, color: 'var(--primary)', fontWeight: '600' },
  { tag: [tags.string, tags.special(tags.string)], color: 'oklch(0.82 0.08 155)' },
  { tag: tags.number, color: 'oklch(0.82 0.1 85)' },
  { tag: tags.comment, color: 'var(--muted-foreground)', fontStyle: 'italic' },
  { tag: [tags.operator, tags.punctuation], color: 'var(--foreground)' },
  { tag: [tags.variableName, tags.name], color: 'var(--foreground)' },
  { tag: [tags.special(tags.variableName), tags.atom], color: 'oklch(0.8 0.1 210)', fontWeight: '600' },
])

const kqlLanguage = StreamLanguage.define({
  startState: () => ({}),
  token: (stream) => {
    if (stream.match('//')) {
      stream.skipToEnd()
      return 'comment'
    }
    if (stream.eatSpace()) {
      return null
    }
    if (stream.match(/"([^"\\]|\\.)*"/)) {
      return 'string'
    }
    if (stream.match(/'([^'\\]|\\.)*'/)) {
      return 'string'
    }
    if (stream.match(/\{\{\s*[a-zA-Z0-9._-]+\s*\}\}/)) {
      return 'atom'
    }
    if (stream.match(/\d+(\.\d+)?/)) {
      return 'number'
    }
    if (stream.match(/[|(),.=<>]/)) {
      return 'operator'
    }
    if (stream.match(/[A-Za-z_][A-Za-z0-9_]*/)) {
      const value = stream.current().toLowerCase()
      return kqlKeywords.includes(value) ? 'keyword' : 'variableName'
    }
    stream.next()
    return null
  },
})

type KqlEditorProps = {
  value: string
  onChange: (value: string) => void
  parameters?: string[]
  className?: string
  minHeight?: number
  readOnly?: boolean
}

export function KqlEditor({
  value,
  onChange,
  parameters = [],
  className,
  minHeight = 220,
  readOnly = false,
}: KqlEditorProps) {
  const editorTheme = useMemo(
    () =>
      EditorView.theme(
        {
          '&': {
            fontSize: '0.925rem',
            minHeight: `${minHeight}px`,
            color: 'var(--foreground)',
            backgroundColor: 'transparent',
          },
          '.cm-editor': {
            minHeight: `${minHeight}px`,
            backgroundColor: 'transparent',
          },
          '.cm-scroller': {
            minHeight: `${minHeight}px`,
            fontFamily: 'var(--font-mono, ui-monospace, SFMono-Regular, monospace)',
            backgroundColor: 'transparent',
          },
          '.cm-sizer, .cm-content, .cm-line': {
            backgroundColor: 'transparent',
          },
          '.cm-focused': {
            outline: 'none',
          },
          '.cm-gutters': {
            color: 'var(--muted-foreground)',
            backgroundColor: 'color-mix(in oklab, var(--muted) 45%, transparent)',
            borderRight: '1px solid color-mix(in oklab, var(--border) 70%, transparent)',
          },
          '.cm-lineNumbers .cm-gutterElement': {
            padding: '0 0.5rem 0 0.25rem',
          },
          '.cm-activeLineGutter, .cm-activeLine': {
            backgroundColor: 'color-mix(in oklab, var(--accent) 24%, transparent)',
          },
          '.cm-selectionBackground, .cm-content ::selection': {
            backgroundColor: 'color-mix(in oklab, var(--primary) 24%, transparent) !important',
          },
          '.cm-cursor, .cm-dropCursor': {
            borderLeftColor: 'var(--primary)',
          },
          '.cm-placeholder': {
            color: 'var(--muted-foreground)',
          },
          '.cm-tooltip': {
            backgroundColor: 'var(--popover)',
            color: 'var(--popover-foreground)',
            border: '1px solid color-mix(in oklab, var(--border) 80%, transparent)',
            borderRadius: '1rem',
            boxShadow: '0 18px 45px color-mix(in oklab, var(--background) 72%, transparent)',
          },
          '.cm-tooltip-autocomplete > ul': {
            fontFamily: 'var(--font-sans, sans-serif)',
            padding: '0.35rem',
          },
          '.cm-tooltip-autocomplete ul li': {
            borderRadius: '0.75rem',
            padding: '0.4rem 0.65rem',
          },
          '.cm-tooltip-autocomplete ul li[aria-selected]': {
            backgroundColor: 'color-mix(in oklab, var(--accent) 55%, transparent)',
            color: 'var(--accent-foreground)',
          },
          '.cm-tooltip-autocomplete ul li .cm-completionDetail': {
            color: 'var(--muted-foreground)',
          },
          '.cm-content': {
            padding: '0.75rem',
          },
          '.cm-panels': {
            backgroundColor: 'color-mix(in oklab, var(--muted) 45%, transparent)',
            color: 'var(--foreground)',
            borderBottom: '1px solid color-mix(in oklab, var(--border) 70%, transparent)',
          },
        },
        { dark: true },
      ),
    [minHeight],
  )

  const completions = useMemo<Completion[]>(() => {
    const parameterCompletions = parameters.map((parameter) => ({
      label: `{{${parameter}}}`,
      type: 'variable' as const,
      detail: parameterDetails[parameter] ?? 'Asset parameter',
    }))
    const keywordCompletions = kqlKeywords.map((keyword) => ({
      label: keyword,
      type: 'keyword' as const,
    }))
    const tableCompletions = kqlTables.map((table) => ({
      label: table,
      type: 'class' as const,
      detail: 'Defender table',
    }))
    return [...parameterCompletions, ...tableCompletions, ...keywordCompletions]
  }, [parameters])

  const extensions = useMemo(
    () => [
      new LanguageSupport(kqlLanguage),
      autocompletion({
        override: [completeFromList(completions)],
        activateOnTyping: true,
      }),
      syntaxHighlighting(kqlHighlightStyle),
      EditorView.lineWrapping,
    ],
    [completions],
  )

  return (
    <div className={cn('overflow-hidden rounded-2xl border border-border/70 bg-card/65 shadow-[inset_0_1px_0_color-mix(in_oklab,var(--foreground)_4%,transparent)]', className)}>
      <CodeMirror
        value={value}
        onChange={onChange}
        theme={editorTheme}
        editable={!readOnly}
        basicSetup={{
          lineNumbers: true,
          foldGutter: false,
          highlightActiveLine: true,
          autocompletion: true,
        }}
        extensions={extensions}
      />
    </div>
  )
}
