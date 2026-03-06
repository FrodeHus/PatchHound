import {
  MutationCache,
  QueryCache,
  QueryClient,
} from '@tanstack/react-query'
import { ApiError } from '@/lib/api-client'
import { login } from '@/lib/auth'

async function handleAuthError(error: unknown): Promise<void> {
  if (error instanceof ApiError && error.status === 401) {
    await login()
  }
}

export const queryClient = new QueryClient({
  queryCache: new QueryCache({
    onError: (error) => {
      void handleAuthError(error)
    },
  }),
  mutationCache: new MutationCache({
    onError: (error) => {
      void handleAuthError(error)
    },
  }),
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 30_000,
      refetchOnWindowFocus: false,
    },
  },
})
