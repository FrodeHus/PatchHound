import { useMemo } from 'react'
import CodeMirror from '@uiw/react-codemirror'
import { python } from '@codemirror/lang-python'
import { StreamLanguage } from '@codemirror/language'
import { shell } from '@codemirror/legacy-modes/mode/shell'
import { powerShell } from '@codemirror/legacy-modes/mode/powershell'
import { EditorView } from '@codemirror/view'
import { cn } from '@/lib/utils'

type Props = {
  value: string
  onChange?: (value: string) => void
  language: 'python' | 'bash' | 'powershell'
  readOnly?: boolean
  height?: string
  className?: string
}

const baseTheme = EditorView.theme({
  '&': { fontSize: '13px' },
  '.cm-gutters': {
    backgroundColor: 'hsl(var(--muted))',
    borderRight: '1px solid hsl(var(--border))',
  },
  '.cm-activeLineGutter': { backgroundColor: 'hsl(var(--accent))' },
  '.cm-activeLine': { backgroundColor: 'hsl(var(--accent) / 0.3)' },
})

function getLanguageExtension(lang: Props['language']) {
  switch (lang) {
    case 'python':
      return python()
    case 'bash':
      return StreamLanguage.define(shell)
    case 'powershell':
      return StreamLanguage.define(powerShell)
  }
}

export function ScriptEditor({ value, onChange, language, readOnly, height = '400px', className }: Props) {
  const extensions = useMemo(
    () => [baseTheme, getLanguageExtension(language)],
    [language],
  )

  return (
    <div className={cn('overflow-hidden rounded-lg border', className)}>
      <CodeMirror
        value={value}
        onChange={onChange}
        extensions={extensions}
        readOnly={readOnly}
        height={height}
        basicSetup={{
          lineNumbers: true,
          foldGutter: true,
          highlightActiveLine: true,
          bracketMatching: true,
          indentOnInput: true,
        }}
      />
    </div>
  )
}
