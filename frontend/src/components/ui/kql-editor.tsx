import CodeMirror from '@uiw/react-codemirror'
import { autocompletion, completeFromList, type Completion } from '@codemirror/autocomplete'
import { LanguageSupport, StreamLanguage } from '@codemirror/language'
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
      return 'special'
    }
    if (stream.match(/\d+(\.\d+)?/)) {
      return 'number'
    }
    if (stream.match(/[|(),.=<>]/)) {
      return 'operator'
    }
    if (stream.match(/[A-Za-z_][A-Za-z0-9_]*/)) {
      return 'variableName'
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
  const completions = useMemo<Completion[]>(() => {
    const parameterCompletions = parameters.map((parameter) => ({
      label: `{{${parameter}}}`,
      type: 'variable' as const,
      detail: 'Asset parameter',
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
      EditorView.lineWrapping,
      EditorView.theme({
        '&': {
          fontSize: '0.925rem',
          minHeight: `${minHeight}px`,
          backgroundColor: 'transparent',
        },
        '.cm-editor': {
          minHeight: `${minHeight}px`,
        },
        '.cm-scroller': {
          minHeight: `${minHeight}px`,
          fontFamily: 'var(--font-mono, ui-monospace, SFMono-Regular, monospace)',
        },
        '.cm-gutters': {
          backgroundColor: 'transparent',
          borderRight: '1px solid color-mix(in oklab, var(--border) 70%, transparent)',
        },
        '.cm-activeLineGutter, .cm-activeLine': {
          backgroundColor: 'color-mix(in oklab, var(--accent) 24%, transparent)',
        },
        '.cm-tooltip': {
          backgroundColor: 'var(--popover)',
          color: 'var(--popover-foreground)',
          border: '1px solid color-mix(in oklab, var(--border) 80%, transparent)',
        },
        '.cm-content': {
          padding: '0.75rem',
        },
      }),
    ],
    [completions, minHeight],
  )

  return (
    <div className={cn('overflow-hidden rounded-2xl border border-border/70 bg-background/70', className)}>
      <CodeMirror
        value={value}
        onChange={onChange}
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
