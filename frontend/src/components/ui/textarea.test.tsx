import { render } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { Textarea } from './textarea'

const editorMocks = vi.hoisted(() => ({
  linkDialogPlugin: vi.fn(() => ({ name: 'link-dialog-plugin' })),
}))

vi.mock('@mdxeditor/editor', async () => {
  const React = await import('react')

  return {
    MDXEditor: React.forwardRef((props: { plugins?: unknown[] }, ref) => {
      React.useImperativeHandle(ref, () => ({
        getMarkdown: () => '',
        setMarkdown: vi.fn(),
      }))

      return <div data-plugin-count={props.plugins?.length ?? 0} />
    }),
    BoldItalicUnderlineToggles: () => null,
    CreateLink: () => null,
    ListsToggle: () => null,
    Separator: () => null,
    UndoRedo: () => null,
    headingsPlugin: vi.fn(() => ({ name: 'headings-plugin' })),
    linkDialogPlugin: editorMocks.linkDialogPlugin,
    linkPlugin: vi.fn(() => ({ name: 'link-plugin' })),
    listsPlugin: vi.fn(() => ({ name: 'lists-plugin' })),
    markdownShortcutPlugin: vi.fn(() => ({ name: 'markdown-shortcut-plugin' })),
    quotePlugin: vi.fn(() => ({ name: 'quote-plugin' })),
    toolbarPlugin: vi.fn(() => ({ name: 'toolbar-plugin' })),
  }
})

describe('Textarea', () => {
  it('registers the link dialog plugin required by the create-link toolbar control', () => {
    render(<Textarea value="" onChange={() => {}} />)

    expect(editorMocks.linkDialogPlugin).toHaveBeenCalled()
  })
})
