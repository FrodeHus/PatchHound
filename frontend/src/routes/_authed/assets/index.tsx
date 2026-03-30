import { createFileRoute, redirect } from '@tanstack/react-router'

export const Route = createFileRoute('/_authed/assets/')({
  beforeLoad: () => {
    throw redirect({
      to: '/devices',
      search: {
        page: 1,
        pageSize: 25,
        search: '',
        criticality: '',
        businessLabelId: '',
        ownerType: '',
        deviceGroup: '',
        healthStatus: '',
        onboardingStatus: '',
        riskScore: '',
        exposureLevel: '',
        tag: '',
        unassignedOnly: false,
      },
    })
  },
})
