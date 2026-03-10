import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TaskStatusUpdate } from '@/components/features/tasks/TaskStatusUpdate'

describe('TaskStatusUpdate', () => {
  it('submits immediately when justification is not required', () => {
    const onSubmit = vi.fn()

    render(<TaskStatusUpdate currentStatus="Completed" isSubmitting={false} onSubmit={onSubmit} />)

    fireEvent.click(screen.getByRole('button', { name: 'Update status' }))

    expect(onSubmit).toHaveBeenCalledWith('Completed', undefined)
  })

  it('requires justification before submitting risk acceptance', () => {
    const onSubmit = vi.fn()

    render(<TaskStatusUpdate currentStatus="RiskAccepted" isSubmitting={false} onSubmit={onSubmit} />)

    const button = screen.getByRole('button', { name: 'Update status' })
    expect(button).toBeDisabled()

    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Business approved exception' } })
    expect(button).not.toBeDisabled()

    fireEvent.click(button)

    expect(onSubmit).toHaveBeenCalledWith('RiskAccepted', 'Business approved exception')
  })
})
