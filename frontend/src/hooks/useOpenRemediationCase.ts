import { useRouter } from '@tanstack/react-router'
import { createServerFn } from '@tanstack/react-start'
import { z } from 'zod'
import { authMiddleware } from '@/server/middleware'
import { apiPost } from '@/server/api'

const getOrCreateRemediationCase = createServerFn({ method: 'POST' })
  .middleware([authMiddleware])
  .inputValidator(z.object({ softwareProductId: z.string().uuid() }))
  .handler(async ({ context, data: { softwareProductId } }) => {
    const data = await apiPost('/remediation/cases', context, { softwareProductId })
    return z.object({ id: z.string().uuid() }).parse(data)
  })

export function useOpenRemediationCase() {
  const router = useRouter()

  return async function openRemediationCase(softwareProductId: string) {
    const remediationCase = await getOrCreateRemediationCase({ data: { softwareProductId } })
    await router.navigate({
      to: '/remediation/cases/$caseId',
      params: { caseId: remediationCase.id },
    })
  }
}
