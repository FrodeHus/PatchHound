import { useQuery } from '@tanstack/react-query'
import { getCurrentUser, login, logout } from '@/lib/auth'

const currentUserQueryKey = ['auth', 'current-user'] as const

export function useCurrentUser() {
  return useQuery({
    queryKey: currentUserQueryKey,
    queryFn: getCurrentUser,
    staleTime: 60_000,
  })
}

export function useAuthActions() {
  return {
    login,
    logout,
  }
}
