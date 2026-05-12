import type { ComponentPropsWithoutRef, PropsWithChildren } from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { describe, expect, it, vi } from 'vitest'
import type { EnrichmentSource } from '@/server/system.functions'
import { GlobalEnrichmentSourceManagement } from './GlobalEnrichmentSourceManagement'
import {
  triggerNvdFullSync,
  triggerNvdModifiedSync,
} from '@/server/system.functions'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, ...props }: ComponentPropsWithoutRef<'a'>) => <a {...props}>{children}</a>,
}))

vi.mock('@/components/layout/tenant-scope', () => ({
  useTenantScope: () => ({ selectedTenantId: '11111111-1111-1111-1111-111111111111' }),
}))

vi.mock('@/api/stored-credentials.functions', () => ({
  fetchStoredCredentials: vi.fn(async () => []),
}))

vi.mock('@/server/system.functions', () => ({
  fetchEnrichmentRuns: vi.fn(),
  triggerEndOfLifeEnrichment: vi.fn(),
  triggerNvdModifiedSync: vi.fn(async () => undefined),
  triggerNvdFullSync: vi.fn(async () => undefined),
  updateEnrichmentSources: vi.fn(),
}))

vi.mock('@/components/ui/dialog', () => ({
  Dialog: ({ open, children }: PropsWithChildren<{ open?: boolean }>) => (open ? <div>{children}</div> : null),
  DialogContent: ({ children }: PropsWithChildren) => <div role="dialog">{children}</div>,
  DialogDescription: ({ children }: PropsWithChildren) => <p>{children}</p>,
  DialogFooter: ({ children }: PropsWithChildren) => <div>{children}</div>,
  DialogHeader: ({ children }: PropsWithChildren) => <div>{children}</div>,
  DialogTitle: ({ children }: PropsWithChildren) => <h2>{children}</h2>,
}))

const nvdSource: EnrichmentSource = {
  key: 'nvd',
  displayName: 'NVD API',
  enabled: true,
  credentialMode: 'no-credential',
  refreshTtlHours: null,
  credentials: {
    storedCredentialId: null,
    acceptedCredentialTypes: ['api-key'],
    hasSecret: false,
    apiBaseUrl: 'https://services.nvd.nist.gov/rest/json/cves/2.0',
  },
  runtime: {
    lastStartedAt: null,
    lastCompletedAt: null,
    lastSucceededAt: null,
    lastStatus: '',
    lastError: '',
  },
  queue: {
    pendingCount: 0,
    retryScheduledCount: 0,
    runningCount: 0,
    failedCount: 0,
    oldestPendingAt: null,
  },
  recentRuns: [],
}

function renderGlobalEnrichment() {
  const queryClient = new QueryClient()
  const onSaved = vi.fn(async () => undefined)
  render(
    <QueryClientProvider client={queryClient}>
      <GlobalEnrichmentSourceManagement sources={[nvdSource]} onSaved={onSaved} />
    </QueryClientProvider>,
  )
  return { onSaved }
}

describe('GlobalEnrichmentSourceManagement NVD sync actions', () => {
  it('triggers modified NVD sync from the Sync button', async () => {
    const { onSaved } = renderGlobalEnrichment()

    fireEvent.click(screen.getByRole('button', { name: /^Sync$/i }))

    await waitFor(() => {
      expect(triggerNvdModifiedSync).toHaveBeenCalledWith({ data: {} })
    })
    await waitFor(() => {
      expect(onSaved).toHaveBeenCalled()
    })
  })

  it('prompts for a year range before triggering full NVD sync', async () => {
    const { onSaved } = renderGlobalEnrichment()

    fireEvent.click(screen.getByRole('button', { name: /Full Sync/i }))
    fireEvent.change(screen.getByLabelText(/From year/i), { target: { value: '2024' } })
    fireEvent.change(screen.getByLabelText(/To year/i), { target: { value: '2026' } })
    fireEvent.click(screen.getByRole('button', { name: /Start full sync/i }))

    await waitFor(() => {
      expect(triggerNvdFullSync).toHaveBeenCalledWith({ data: { fromYear: 2024, toYear: 2026 } })
    })
    await waitFor(() => {
      expect(onSaved).toHaveBeenCalled()
    })
  })
})
