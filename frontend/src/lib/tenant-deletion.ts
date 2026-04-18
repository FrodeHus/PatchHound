export function isTenantPendingDeletion(error: unknown): boolean {
  if (!error || typeof error !== 'object') return false
  const err = error as Record<string, unknown>
  return err['name'] === 'TenantPendingDeletionError' || err['message'] === 'TENANT_PENDING_DELETION'
}
