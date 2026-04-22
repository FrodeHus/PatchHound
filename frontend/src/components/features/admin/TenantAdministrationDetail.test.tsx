import type { ComponentPropsWithoutRef } from 'react'
import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { TenantDetail } from '@/api/settings.schemas'
import { TenantAdministrationDetail } from '@/components/features/admin/TenantAdministrationDetail'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...props }: ComponentPropsWithoutRef<'a'>) => <a {...props}>{children}</a>,
  useRouter: () => ({ invalidate: vi.fn(), navigate: vi.fn() }),
}))

vi.mock('@/components/features/admin/TenantSourceManagement', () => ({
  TenantSourceManagement: () => <div>Tenant sources</div>,
}))

vi.mock('@/api/settings.functions', () => ({
  deleteTenant: vi.fn(),
  fetchTenantDetail: vi.fn(),
  updateTenant: vi.fn(),
}))

vi.mock('@/components/features/admin/device-rules/TenantDeviceRulesPanel', () => ({
  TenantDeviceRulesPanel: ({ tenantName }: { tenantName: string }) => <div>Embedded device rules for {tenantName}</div>,
}))

vi.mock('@/components/features/audit/RecentAuditPanel', () => ({
  RecentAuditPanel: () => <div>Recent audit</div>,
}))

vi.mock('@/components/features/settings/TenantAiSettingsPage', () => ({
  TenantAiSettingsPage: () => <div>Tenant AI settings</div>,
}))

const tenantFixture: TenantDetail = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Contoso',
  entraTenantId: 'entra-tenant',
  isPrimary: false,
  assets: {
    totalCount: 0,
    deviceCount: 0,
    softwareCount: 0,
    cloudResourceCount: 0,
  },
  sla: {
    criticalDays: 7,
    highDays: 14,
    mediumDays: 30,
    lowDays: 60,
  },
  ingestionSources: [],
}

describe('TenantAdministrationDetail device rules tab', () => {
  it('renders the embedded asset rules panel instead of linking out', () => {
    render(
      <TenantAdministrationDetail
        tenant={tenantFixture}
        activeTab="device-rules"
      />,
    )

    expect(screen.getByText('Embedded device rules for Contoso')).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /asset rules/i })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /open device rules/i })).not.toBeInTheDocument()
  })
})
