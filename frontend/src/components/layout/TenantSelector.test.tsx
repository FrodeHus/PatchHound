import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { TenantSelector } from './TenantSelector'

const tenants = [
  { id: 'tenant-1', name: 'Reothor Labs' },
  { id: 'tenant-2', name: 'Pepperprove' },
]

describe('TenantSelector', () => {
  it('renders a single dropdown chevron for multi-tenant selection', () => {
    const { container } = render(
      <TenantSelector
        tenants={tenants}
        selectedTenantId="tenant-1"
        onSelectTenant={() => {}}
      />,
    )

    expect(screen.getByText('Reothor Labs')).toBeInTheDocument()
    expect(container.querySelectorAll('button svg')).toHaveLength(1)
  })
})
