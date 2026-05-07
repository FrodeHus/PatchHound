import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { createSecurityProfileDraft } from './security-profile-workbench-model'
import { SecurityProfileWorkbench } from './SecurityProfileWorkbench'

describe('SecurityProfileWorkbench', () => {
  it('renders the environmental score workbench structure from the design', () => {
    const draft = {
      ...createSecurityProfileDraft(),
      name: 'Production Payments - Internet-facing',
      confidentialityRequirement: 'High' as const,
      availabilityRequirement: 'High' as const,
    }

    render(
      <SecurityProfileWorkbench
        mode="edit"
        tenantName="Payments Platform"
        draft={draft}
        isSaving={false}
        onDraftChange={vi.fn()}
        onCancel={vi.fn()}
        onSave={vi.fn()}
      />,
    )

    expect(screen.getByRole('heading', { name: 'Production Payments - Internet-facing' })).toBeInTheDocument()
    expect(screen.getAllByText('Temporal')).toHaveLength(2)
    expect(screen.getByText('Mod. impact (C/I/A)')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save as draft' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Apply profile' })).toBeInTheDocument()
  })
})
