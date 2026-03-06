import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider, createRootRoute, createRoute, createRouter } from '@tanstack/react-router'
import { render, screen } from '@testing-library/react'
import { expect, test } from 'vitest'
import { VulnerabilityTable } from '@/components/features/vulnerabilities/VulnerabilityTable'

function renderWithRouter() {
  const rootRoute = createRootRoute({
    component: () => (
      <VulnerabilityTable
        totalCount={1}
        items={[
          {
            id: '7f0e6d5a-4f84-4762-9ec4-b541750486d7',
            externalId: 'CVE-2026-4321',
            title: 'Test vulnerability',
            vendorSeverity: 'High',
            status: 'Open',
            source: 'Defender',
            cvssScore: 8.1,
            publishedDate: '2026-02-20T00:00:00Z',
            affectedAssetCount: 3,
            adjustedSeverity: null,
          },
        ]}
      />
    ),
  })

  const vulnerabilitiesRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/vulnerabilities/$id',
    component: () => null,
  })

  const indexRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/',
    component: () => null,
  })

  const routeTree = rootRoute.addChildren([indexRoute, vulnerabilitiesRoute])
  const router = createRouter({ routeTree, history: undefined })
  const queryClient = new QueryClient()

  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

test('renders vulnerability row with detail link', async () => {
  renderWithRouter()

  const link = await screen.findByRole('link', { name: 'CVE-2026-4321' })
  expect(link).toBeInTheDocument()
  expect(link.getAttribute('href')).toContain('/vulnerabilities/7f0e6d5a-4f84-4762-9ec4-b541750486d7')
  expect(screen.getByText('Test vulnerability')).toBeInTheDocument()
})
