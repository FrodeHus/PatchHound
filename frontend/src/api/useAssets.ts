import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { apiClient } from '@/lib/api-client'

const assetSchema = z.object({
  id: z.string().uuid(),
  externalId: z.string(),
  name: z.string(),
  assetType: z.string(),
  criticality: z.string(),
  ownerType: z.string(),
  vulnerabilityCount: z.number(),
})

const pagedAssetsSchema = z.object({
  items: z.array(assetSchema),
  totalCount: z.number(),
})

export type Asset = z.infer<typeof assetSchema>

export type AssetFilters = {
  assetType?: string
  ownerType?: string
  search?: string
  page?: number
  pageSize?: number
}

export const assetKeys = {
  all: ['assets'] as const,
  list: (filters: AssetFilters) => [...assetKeys.all, 'list', filters] as const,
}

function buildAssetQuery(filters: AssetFilters): string {
  const params = new URLSearchParams()
  if (filters.assetType) params.set('assetType', filters.assetType)
  if (filters.ownerType) params.set('ownerType', filters.ownerType)
  if (filters.search) params.set('search', filters.search)
  params.set('page', String(filters.page ?? 1))
  params.set('pageSize', String(filters.pageSize ?? 50))
  return `/assets?${params.toString()}`
}

export function useAssets(filters: AssetFilters) {
  return useQuery({
    queryKey: assetKeys.list(filters),
    queryFn: async () => {
      const response = await apiClient.get<unknown>(buildAssetQuery(filters))
      return pagedAssetsSchema.parse(response)
    },
  })
}

export function useAssignAssetOwner() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({
      assetId,
      ownerType,
      ownerId,
    }: {
      assetId: string
      ownerType: 'User' | 'Team'
      ownerId: string
    }) => {
      await apiClient.put<null>(`/assets/${assetId}/owner`, {
        ownerType,
        ownerId,
      })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: assetKeys.all })
    },
  })
}

export function useSetAssetCriticality() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async ({ assetId, criticality }: { assetId: string; criticality: string }) => {
      await apiClient.put<null>(`/assets/${assetId}/criticality`, { criticality })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: assetKeys.all })
    },
  })
}
