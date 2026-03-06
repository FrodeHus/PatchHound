export type RoleName =
  | 'GlobalAdmin'
  | 'SecurityManager'
  | 'SecurityAnalyst'
  | 'AssetOwner'
  | 'Stakeholder'
  | 'Auditor'

export type TenantAccess = {
  id: string
  name: string
}

export type CurrentUser = {
  id: string
  email: string
  displayName: string
  roles: RoleName[]
  tenants: TenantAccess[]
  isCrossTenant: boolean
}

export type ProblemDetails = {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  [key: string]: unknown
}

export type PagedResponse<T> = {
  items: T[]
  totalCount: number
}
