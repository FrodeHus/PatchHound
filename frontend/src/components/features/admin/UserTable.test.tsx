import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { UserListItem } from '@/api/users.schemas'
import { UserTable } from '@/components/features/admin/UserTable'

const users: UserListItem[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    email: 'frode@example.com',
    displayName: 'Frode Hus',
    company: 'Reothor Labs',
    isEnabled: true,
    accessScope: 'Internal',
    roles: ['GlobalAdmin', 'Auditor'],
    teams: [{ teamId: 'team-1', teamName: 'SOC', isDefault: false }],
    tenantNames: ['Reothor Labs', 'Pepperprove'],
  },
]

const defaultProps = {
  users,
  totalCount: 1,
  page: 1,
  pageSize: 25,
  totalPages: 1,
  selectedUserId: users[0].id,
  filters: {
    search: 'fro',
    role: 'GlobalAdmin',
    status: '',
    teamId: '',
  },
  teams: [{ id: 'team-1', name: 'SOC' }],
  onFilterChange: vi.fn(),
  onPageChange: vi.fn(),
  onPageSizeChange: vi.fn(),
  onSelectUser: vi.fn(),
}

describe('UserTable', () => {
  it('renders the access directory as a dense table', () => {
    render(<UserTable {...defaultProps} />)

    expect(screen.getByRole('table', { name: /access directory/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /display name/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /email \/ upn/i })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /tenant access/i })).toBeInTheDocument()
    expect(screen.getByRole('row', { name: /frode hus.*frode@example.com.*internal.*enabled.*2 tenants/i })).toBeInTheDocument()
  })

  it('surfaces active filters as removable context chips', () => {
    const onFilterChange = vi.fn()

    render(<UserTable {...defaultProps} onFilterChange={onFilterChange} />)

    expect(screen.getByText('Search: fro')).toBeInTheDocument()
    expect(screen.getByText('Role: Global Admin')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /clear all/i }))

    expect(onFilterChange).toHaveBeenCalledWith({
      search: '',
      role: '',
      status: '',
      teamId: '',
    })
  })
})
