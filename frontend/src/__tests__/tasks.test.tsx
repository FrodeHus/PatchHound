import { fireEvent, render, screen } from '@testing-library/react'
import { expect, test, vi } from 'vitest'
import { TaskStatusUpdate } from '@/components/features/tasks/TaskStatusUpdate'

test('requires justification for CannotPatch status', () => {
  const onSubmit = vi.fn()

  render(<TaskStatusUpdate currentStatus="Pending" isSubmitting={false} onSubmit={onSubmit} />)

  fireEvent.change(screen.getByRole('combobox'), { target: { value: 'CannotPatch' } })

  const updateButton = screen.getByRole('button', { name: 'Update status' })
  expect(updateButton).toBeDisabled()

  fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Patch conflicts with legacy dependency' } })
  expect(updateButton).not.toBeDisabled()

  fireEvent.click(updateButton)
  expect(onSubmit).toHaveBeenCalledWith('CannotPatch', 'Patch conflicts with legacy dependency')
})
