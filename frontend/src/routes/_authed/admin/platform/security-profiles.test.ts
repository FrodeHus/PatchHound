import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

describe('security profile route wiring', () => {
  it('renders child routes from the platform security profiles parent route', () => {
    const routePath = join(process.cwd(), 'src/routes/_authed/admin/platform/security-profiles.tsx')
    const routeSource = readFileSync(routePath, 'utf8')

    expect(routeSource).toContain('Outlet')
    expect(routeSource).toContain('<Outlet />')
  })
})
