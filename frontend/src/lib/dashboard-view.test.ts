import { describe, expect, it } from 'vitest'
import { resolveDashboardViewMode, resolveDefaultDashboardView } from './dashboard-view'

describe('resolveDefaultDashboardView', () => {
  it('routes stakeholder users to the executive view', () => {
    expect(resolveDefaultDashboardView(['Stakeholder'])).toBe('executive')
  })

  it('routes security managers to the security manager view', () => {
    expect(resolveDefaultDashboardView(['SecurityManager'])).toBe('security-manager')
  })

  it('routes technical managers to the technical manager view', () => {
    expect(resolveDefaultDashboardView(['TechnicalManager'])).toBe('technical-manager')
  })

  it('routes security analysts to operations', () => {
    expect(resolveDefaultDashboardView(['SecurityAnalyst'])).toBe('operations')
  })

  it('routes asset owners to the owner view', () => {
    expect(resolveDefaultDashboardView(['AssetOwner'])).toBe('owner')
  })
})

describe('resolveDashboardViewMode', () => {
  it('honors a global admin portal preference when no explicit mode is requested', () => {
    expect(
      resolveDashboardViewMode({
        roles: ['GlobalAdmin'],
        preferredMode: 'technical-manager',
      }),
    ).toBe('technical-manager')
  })

  it('honors an explicit requested mode for a global admin over the saved preference', () => {
    expect(
      resolveDashboardViewMode({
        roles: ['GlobalAdmin'],
        requestedMode: 'security-manager',
        preferredMode: 'executive',
      }),
    ).toBe('security-manager')
  })

  it('ignores requested modes for non-switching roles and keeps the role default', () => {
    expect(
      resolveDashboardViewMode({
        roles: ['SecurityManager'],
        requestedMode: 'owner',
      }),
    ).toBe('security-manager')
  })
})
