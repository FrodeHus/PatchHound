import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { MessageSquareMore, Pencil, Trash2 } from 'lucide-react'
import { toast } from 'sonner'
import { createWorkNote, deleteWorkNote, fetchWorkNotes, updateWorkNote } from '@/api/work-notes.functions'
import type { WorkNote } from '@/api/work-notes.schemas'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { Button } from '@/components/ui/button'
import { MarkdownViewer } from '@/components/ui/markdown-viewer'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Textarea } from '@/components/ui/textarea'
import { formatDateTime } from '@/lib/formatting'

type WorkNoteEntityType = 'vulnerabilities' | 'software' | 'remediations' | 'assets' | 'devices'

type WorkNotesSheetProps = {
  entityType: WorkNoteEntityType
  entityId: string
  title: string
  description: string
  triggerLabel?: string
}

export function WorkNotesSheet({
  entityType,
  entityId,
  title,
  description,
  triggerLabel = 'Work notes',
}: WorkNotesSheetProps) {
  const { selectedTenantId } = useTenantScope()
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [content, setContent] = useState('')
  const [editingNoteId, setEditingNoteId] = useState<string | null>(null)
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null)

  const queryKey = useMemo(
    () => ['work-notes', selectedTenantId, entityType, entityId] as const,
    [selectedTenantId, entityType, entityId],
  )

  const notesQuery = useQuery({
    queryKey,
    queryFn: () => fetchWorkNotes({ data: { entityType, entityId } }),
    enabled: open,
  })

  const createMutation = useMutation({
    mutationFn: (markdown: string) => createWorkNote({ data: { entityType, entityId, content: markdown } }),
    onSuccess: async () => {
      setContent('')
      toast.success('Work note added')
      await queryClient.invalidateQueries({ queryKey })
    },
    onError: () => toast.error('Failed to add work note'),
  })

  const updateMutation = useMutation({
    mutationFn: ({ noteId, markdown }: { noteId: string; markdown: string }) =>
      updateWorkNote({ data: { noteId, content: markdown } }),
    onSuccess: async () => {
      setContent('')
      setEditingNoteId(null)
      toast.success('Work note updated')
      await queryClient.invalidateQueries({ queryKey })
    },
    onError: () => toast.error('Failed to update work note'),
  })

  const deleteMutation = useMutation({
    mutationFn: (noteId: string) => deleteWorkNote({ data: { noteId } }),
    onSuccess: async () => {
      setConfirmDeleteId(null)
      if (editingNoteId && editingNoteId === confirmDeleteId) {
        setEditingNoteId(null)
        setContent('')
      }
      toast.success('Work note deleted')
      await queryClient.invalidateQueries({ queryKey })
    },
    onError: () => toast.error('Failed to delete work note'),
  })

  const notes = notesQuery.data ?? []
  const isEditing = editingNoteId !== null
  const isSubmitting = createMutation.isPending || updateMutation.isPending

  function beginEdit(note: WorkNote) {
    setEditingNoteId(note.id)
    setContent(note.content)
    setConfirmDeleteId(null)
  }

  function resetComposer() {
    setEditingNoteId(null)
    setContent('')
    setConfirmDeleteId(null)
  }

  function handleSubmit() {
    const trimmed = content.trim()
    if (!trimmed) {
      return
    }

    if (editingNoteId) {
      updateMutation.mutate({ noteId: editingNoteId, markdown: trimmed })
      return
    }

    createMutation.mutate(trimmed)
  }

  return (
    <>
      <Button type="button" variant="ghost" size="sm" className="gap-1.5" onClick={() => setOpen(true)}>
        <MessageSquareMore className="size-4" />
        {triggerLabel}
      </Button>

      <Sheet open={open} onOpenChange={(nextOpen) => {
        setOpen(nextOpen)
        if (!nextOpen) {
          resetComposer()
        }
      }}>
        <SheetContent side="right" className="w-full overflow-y-auto border-l border-border/80 bg-card p-0 sm:max-w-2xl">
          <SheetHeader className="border-b border-border/70 bg-[linear-gradient(180deg,color-mix(in_oklab,var(--card)_96%,black),var(--card))]">
            <SheetTitle>{title}</SheetTitle>
            <SheetDescription>{description}</SheetDescription>
          </SheetHeader>

          <div className="space-y-5 p-5">
            <section className="space-y-3">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="text-sm font-medium text-foreground">Work notes</p>
                  <p className="text-xs text-muted-foreground">
                    Tenant-local notes visible to everyone with access.
                  </p>
                </div>
                {notesQuery.isFetching ? (
                  <span className="text-xs text-muted-foreground">Loading…</span>
                ) : (
                  <span className="text-xs text-muted-foreground">{notes.length} notes</span>
                )}
              </div>

              {notes.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-border/60 bg-background/35 p-4 text-sm text-muted-foreground">
                  No work notes yet.
                </div>
              ) : (
                <div className="space-y-3">
                  {notes.map((note) => (
                    <article key={note.id} className="rounded-[1.15rem] border border-border/55 bg-background/40 p-4">
                      <div className="flex items-start justify-between gap-3">
                        <div>
                          <p className="text-sm font-medium text-foreground">{note.authorDisplayName}</p>
                          <p className="text-xs text-muted-foreground">
                            {formatDateTime(note.updatedAt ?? note.createdAt)}
                            {note.updatedAt ? ' · edited' : ''}
                          </p>
                        </div>
                        {note.canEdit || note.canDelete ? (
                          <div className="flex items-center gap-1">
                            {note.canEdit ? (
                              <Button type="button" variant="ghost" size="icon" className="size-8" onClick={() => beginEdit(note)}>
                                <Pencil className="size-4" />
                              </Button>
                            ) : null}
                            {note.canDelete ? (
                              confirmDeleteId === note.id ? (
                                <Button
                                  type="button"
                                  variant="destructive"
                                  size="sm"
                                  onClick={() => deleteMutation.mutate(note.id)}
                                  disabled={deleteMutation.isPending}
                                >
                                  Confirm delete
                                </Button>
                              ) : (
                                <Button type="button" variant="ghost" size="icon" className="size-8" onClick={() => setConfirmDeleteId(note.id)}>
                                  <Trash2 className="size-4" />
                                </Button>
                              )
                            ) : null}
                          </div>
                        ) : null}
                      </div>
                      <MarkdownViewer content={note.content} className="mt-3" />
                    </article>
                  ))}
                </div>
              )}
            </section>

            <section className="space-y-3 rounded-[1.25rem] border border-border/60 bg-background/30 p-4">
              <div className="space-y-1">
                <p className="text-sm font-medium text-foreground">
                  {isEditing ? 'Edit work note' : 'Add work note'}
                </p>
                <p className="text-xs text-muted-foreground">
                  Markdown is supported. Notes are visible to all tenant users with access.
                </p>
              </div>
              <Textarea
                className="min-h-40 bg-background/40"
                value={content}
                placeholder="Capture useful context, progress, blockers, or handover notes…"
                onChange={(event) => setContent(event.target.value)}
              />
              <div className="flex flex-wrap gap-2">
                <Button type="button" onClick={handleSubmit} disabled={isSubmitting || content.trim().length === 0}>
                  {isSubmitting ? 'Saving…' : isEditing ? 'Update work note' : 'Add work note'}
                </Button>
                {isEditing ? (
                  <Button type="button" variant="outline" onClick={resetComposer}>
                    Cancel edit
                  </Button>
                ) : null}
              </div>
            </section>
          </div>
        </SheetContent>
      </Sheet>
    </>
  )
}
