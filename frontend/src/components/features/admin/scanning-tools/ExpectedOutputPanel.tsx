import { useMemo } from 'react'
import CodeMirror from '@uiw/react-codemirror'
import { json } from '@codemirror/lang-json'
import { EditorView } from '@codemirror/view'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'

const EXPECTED_OUTPUT = `{
  "software": [
    {
      "canonicalName": "nginx",
      "canonicalProductKey": "nginx:nginx",
      "detectedVersion": "1.24.0",
      "canonicalVendor": "F5 Networks",
      "category": "web-server",
      "primaryCpe23Uri": "cpe:2.3:a:f5:nginx:1.24.0:*:*:*:*:*:*:*"
    }
  ]
}`

const FIELD_DOCS: { name: string; required: boolean; description: string }[] = [
  { name: 'canonicalName', required: true, description: 'Human-readable software name' },
  { name: 'canonicalProductKey', required: true, description: 'Unique product key (vendor:product)' },
  { name: 'detectedVersion', required: false, description: 'Detected version string' },
  { name: 'canonicalVendor', required: false, description: 'Vendor / publisher name' },
  { name: 'category', required: false, description: 'Software category (e.g. web-server)' },
  { name: 'primaryCpe23Uri', required: false, description: 'CPE 2.3 URI for vulnerability matching' },
]

const theme = EditorView.theme({
  '&': { fontSize: '13px' },
  '.cm-gutters': {
    backgroundColor: 'hsl(var(--muted))',
    borderRight: '1px solid hsl(var(--border))',
  },
})

export function ExpectedOutputPanel() {
  const extensions = useMemo(() => [theme, json(), EditorView.lineWrapping], [])

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Expected Output — DetectedSoftware</CardTitle>
        <p className="text-xs text-muted-foreground">
          Your script must print a single JSON object to stdout matching this schema.
        </p>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="overflow-hidden rounded-lg border">
          <CodeMirror
            value={EXPECTED_OUTPUT}
            extensions={extensions}
            readOnly
            height="220px"
            basicSetup={{ lineNumbers: true, foldGutter: true }}
          />
        </div>
        <div className="space-y-1.5">
          {FIELD_DOCS.map((field) => (
            <div key={field.name} className="flex items-center gap-2 text-sm">
              <Badge
                variant={field.required ? 'default' : 'secondary'}
                className={field.required ? 'bg-green-600 text-[10px]' : 'text-[10px]'}
              >
                {field.required ? 'required' : 'optional'}
              </Badge>
              <code className="text-xs font-medium">{field.name}</code>
              <span className="text-muted-foreground">{field.description}</span>
            </div>
          ))}
        </div>
      </CardContent>
    </Card>
  )
}
