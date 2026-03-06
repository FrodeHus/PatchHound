import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { ReactNode } from 'react'
import { afterAll, afterEach, beforeAll, expect, test, vi } from 'vitest'
import { DashboardPage } from '@/routes/index'

vi.mock('recharts', () => ({
  ResponsiveContainer: ({ children }: { children: ReactNode }) => children,
  LineChart: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  BarChart: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  CartesianGrid: () => null,
  XAxis: () => null,
  YAxis: () => null,
  Tooltip: () => null,
  Legend: () => null,
  Line: () => null,
  Bar: () => null,
}))

const server = setupServer(
  http.get('/api/dashboard/summary', () => {
    return HttpResponse.json({
      exposureScore: 72.4,
      vulnerabilitiesBySeverity: { Critical: 4, High: 8, Medium: 12, Low: 6 },
      vulnerabilitiesByStatus: { Open: 18, InRemediation: 9, Resolved: 3, RiskAccepted: 0 },
      slaCompliancePercent: 83.2,
      overdueTaskCount: 4,
      totalTaskCount: 24,
      averageRemediationDays: 6.8,
      topCriticalVulnerabilities: [
        {
          id: '7f0e6d5a-4f84-4762-9ec4-b541750486d7',
          externalId: 'CVE-2026-1234',
          title: 'Critical RCE in edge gateway',
          severity: 'Critical',
          cvssScore: 9.8,
          affectedAssetCount: 11,
          daysSincePublished: 17,
        },
      ],
    })
  }),
  http.get('/api/dashboard/trends', () => {
    return HttpResponse.json({
      items: [
        { date: '2026-01-01', severity: 'High', count: 7 },
        { date: '2026-02-01', severity: 'High', count: 5 },
      ],
    })
  }),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  })

  return render(
    <QueryClientProvider client={queryClient}>
      <DashboardPage />
    </QueryClientProvider>,
  )
}

test('shows dashboard metrics when API responds', async () => {
  renderPage()

  expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
  expect(await screen.findByText('Exposure Score')).toBeInTheDocument()
  expect(await screen.findByText('72.4')).toBeInTheDocument()
  expect(await screen.findByText('Top Critical Vulnerabilities')).toBeInTheDocument()
  expect(await screen.findByText('CVE-2026-1234')).toBeInTheDocument()
})
