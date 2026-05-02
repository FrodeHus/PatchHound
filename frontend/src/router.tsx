import { createRouter as createTanStackRouter } from '@tanstack/react-router'
import { routeTree } from './routeTree.gen'

export function createRouter() {
  return createTanStackRouter({
    context: {
      user: null,
    },
    routeTree,
    scrollRestoration: true,
    // Skip loader re-runs on back-navigation when the route's data is < 1 min old.
    // Routes whose loaders depend on search params still re-run when those deps
    // change (loaderDeps controls invalidation), but identical-URL revisits hit cache.
    defaultStaleTime: 60_000,
    defaultPreloadStaleTime: 60_000,
  })
}

export function getRouter() {
  return createRouter()
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof createRouter>
  }
}
