import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchEnrichmentChanges } from '@/api/enrichment-changes.functions'
import { EnrichmentChangesSheet } from '@/components/features/enrichment/EnrichmentChangesSheet'

vi.mock('@/api/enrichment-changes.functions', () => ({
  fetchEnrichmentChanges: vi.fn(),
}))

const fetchEnrichmentChangesMock = vi.mocked(fetchEnrichmentChanges)

afterEach(() => {
  vi.clearAllMocks()
})

describe('EnrichmentChangesSheet', () => {
  it('loads and renders enrichment field changes when opened', async () => {
    fetchEnrichmentChangesMock.mockResolvedValue({
      items: [
        {
          id: '11111111-1111-1111-1111-111111111111',
          scope: 'Global',
          tenantId: null,
          entityType: 'Vulnerability',
          entityId: '22222222-2222-2222-2222-222222222222',
          sourceKey: 'microsoft-defender',
          sourceDisplayName: 'Microsoft Defender',
          fieldPath: 'cvssScore',
          displayName: 'CVSS score',
          oldValue: 7.5,
          newValue: 8.8,
          valueKind: 'Number',
          changedAt: '2026-05-20T08:00:00Z',
          enrichmentRunId: null,
          enrichmentJobId: '33333333-3333-3333-3333-333333333333',
          changeReason: 'Defender enrichment updated the vulnerability score.',
          confidence: null,
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 30,
      totalPages: 1,
    })

    renderSheet()

    fireEvent.click(screen.getByRole('button', { name: /Enrichment changes/i }))

    expect(await screen.findByText('CVSS score')).toBeInTheDocument()
    expect(screen.getByText(/Microsoft Defender/i)).toBeInTheDocument()
    expect(screen.getByText('7.5')).toBeInTheDocument()
    expect(screen.getByText('8.8')).toBeInTheDocument()
    expect(fetchEnrichmentChangesMock).toHaveBeenCalledWith({
      data: {
        entityType: 'Vulnerability',
        entityId: '22222222-2222-2222-2222-222222222222',
        page: 1,
        pageSize: 30,
      },
    })
  })

  it('shows an empty state when no enrichment changes exist', async () => {
    fetchEnrichmentChangesMock.mockResolvedValue({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 30,
      totalPages: 0,
    })

    renderSheet()

    fireEvent.click(screen.getByRole('button', { name: /Enrichment changes/i }))

    expect(await screen.findByText(/No enrichment changes have been recorded/i)).toBeInTheDocument()
  })
})

function renderSheet() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  })

  return render(
    <QueryClientProvider client={queryClient}>
      <EnrichmentChangesSheet
        entityType="Vulnerability"
        entityId="22222222-2222-2222-2222-222222222222"
        entityLabel="CVE-2026-1234"
      />
    </QueryClientProvider>,
  )
}
