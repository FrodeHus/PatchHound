import { useState } from 'react'
import type { CommentItem } from '@/api/vulnerabilities.schemas'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'

type CommentsTabProps = {
  comments: CommentItem[]
  isSubmitting: boolean
  onSubmit: (content: string) => void
}

export function CommentsTab({ comments, isSubmitting, onSubmit }: CommentsTabProps) {
  const [content, setContent] = useState('')

  return (
    <section className="space-y-3 rounded-lg border border-border bg-card p-4">
      <h3 className="text-lg font-semibold">Comments</h3>

      <div className="space-y-2">
        {comments.length === 0 ? <p className="text-sm text-muted-foreground">No comments yet.</p> : null}
        {comments.map((comment) => (
          <article key={comment.id} className="rounded-md border border-border/70 p-3">
            <p className="whitespace-pre-wrap text-sm">{comment.content}</p>
            <p className="mt-1 text-xs text-muted-foreground">{new Date(comment.createdAt).toLocaleString()}</p>
          </article>
        ))}
      </div>

      <label className="block space-y-1 text-sm">
        <span>Add comment</span>
        <Textarea
          className="min-h-24 rounded-md bg-background"
          value={content}
          onChange={(event) => {
            setContent(event.target.value)
          }}
        />
      </label>
      <Button
        type="button"
        className="w-fit rounded-md"
        disabled={isSubmitting || content.trim().length === 0}
        onClick={() => {
          const trimmed = content.trim()
          onSubmit(trimmed)
          setContent('')
        }}
      >
        {isSubmitting ? 'Saving...' : 'Post comment'}
      </Button>
    </section>
  )
}
