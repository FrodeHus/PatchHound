import { useQuery } from '@tanstack/react-query'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useCallback, useEffect, useState } from 'react'
import { z } from 'zod'
import { AlertTriangle, CheckCircle2, ShieldAlert, TimerReset } from 'lucide-react'
import { fetchDashboardBurndown, fetchDashboardFilterOptions, fetchDashboardSummary, fetchDashboardTrends } from '@/api/dashboard.functions'
import { Card, CardContent } from '@/components/ui/card'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Sparkline } from '@/components/features/dashboard/Sparkline'
import { CriticalVulnerabilities } from '@/components/features/dashboard/CriticalVulnerabilities'
import { DashboardFilterBar } from '@/components/features/dashboard/DashboardFilterBar'
import { DeviceGroupVulnerabilityChart } from '@/components/features/dashboard/DeviceGroupVulnerabilityChart'
import { DeviceGroupRiskDetailDialog } from '@/components/features/dashboard/DeviceGroupRiskDetailDialog'
import { DeviceHealthCard } from '@/components/features/dashboard/DeviceHealthCard'
import { OnboardingStatusCard } from '@/components/features/dashboard/OnboardingStatusCard'
import { RiskScoreCard } from '@/components/features/dashboard/RiskScoreCard'
import { RemediationVelocity } from '@/components/features/dashboard/RemediationVelocity'
import { RiskChangeBriefCard } from '@/components/features/dashboard/RiskChangeBriefCard'
import { TrendChart } from '@/components/features/dashboard/TrendChart'
import { RiskHeatmap } from '@/components/features/dashboard/RiskHeatmap'
import { VulnerabilityAgeChart } from '@/components/features/dashboard/VulnerabilityAgeChart'
import { MttrCard } from '@/components/features/dashboard/MttrCard'
import { BurndownChart } from '@/components/features/dashboard/BurndownChart'
import { CisoExecutiveOverview } from '@/components/features/dashboard/CisoExecutiveOverview'
import { AnalystTriageWorkbench } from '@/components/features/dashboard/AnalystTriageWorkbench'
import { useTenantScope } from '@/components/layout/tenant-scope'
import { readDashboardViewPreference } from '@/lib/dashboard-view'

const TAB_VALUES = ['risk', 'remediation', 'infrastructure'] as const
type DashboardTab = (typeof TAB_VALUES)[number]
const DASHBOARD_MODE_VALUES = ['executive', 'operations'] as const
type DashboardMode = (typeof DASHBOARD_MODE_VALUES)[number]

const dashboardSearchSchema = z.object({
  tab: z.enum(TAB_VALUES).optional().catch(undefined),
  mode: z.enum(DASHBOARD_MODE_VALUES).optional().catch(undefined),
  minAgeDays: z.string().optional().catch(undefined),
  platform: z.string().optional().catch(undefined),
  deviceGroup: z.string().optional().catch(undefined),
})

export const Route = createFileRoute('/_authed/')({
  validateSearch: dashboardSearchSchema,
  loader: async () => {
    const [summary, trends] = await Promise.all([
      fetchDashboardSummary({ data: {} }),
      fetchDashboardTrends({ data: {} }),
    ])
    return { summary, trends }
  },
  component: DashboardPage,
})

function DashboardPage() {
  const initialData = Route.useLoaderData()
  const { user } = Route.useRouteContext()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const [selectedDeviceGroup, setSelectedDeviceGroup] = useState<string | null>(null)
  const canUseInitialData = initialTenantId === selectedTenantId
  const isExecutiveViewer = user.roles.includes('Stakeholder') || user.roles.includes('GlobalAdmin')
  const canSwitchModes = user.roles.includes('GlobalAdmin')
  const defaultMode: DashboardMode = isExecutiveViewer ? 'executive' : 'operations'
  const requestedMode = search.mode
  const dashboardMode: DashboardMode = canSwitchModes
    ? requestedMode ?? defaultMode
    : defaultMode

  const activeTab: DashboardTab = search.tab ?? 'risk'
  const minAgeDays = search.minAgeDays ?? ''
  const platform = search.platform ?? ''
  const deviceGroup = search.deviceGroup ?? ''

  const filterParams = {
    minAgeDays: minAgeDays ? Number(minAgeDays) : undefined,
    platform: platform || undefined,
    deviceGroup: deviceGroup || undefined,
  }

  const hasActiveFilters = Boolean(minAgeDays || platform || deviceGroup)

  useEffect(() => {
    if (!canSwitchModes || requestedMode) {
      return
    }

    const preferredMode = readDashboardViewPreference()
    if (preferredMode && preferredMode !== dashboardMode) {
      void navigate({
        search: (prev) => ({ ...prev, mode: preferredMode }),
      })
    }
  }, [canSwitchModes, dashboardMode, navigate, requestedMode])

  const summaryQuery = useQuery({
    queryKey: ['dashboard', 'summary', selectedTenantId, minAgeDays, platform, deviceGroup],
    queryFn: () => fetchDashboardSummary({ data: filterParams }),
    initialData: canUseInitialData && !hasActiveFilters ? initialData.summary : undefined,
    staleTime: 30_000,
  })
  const trendsQuery = useQuery({
    queryKey: ['dashboard', 'trends', selectedTenantId, minAgeDays, platform, deviceGroup],
    queryFn: () => fetchDashboardTrends({ data: filterParams }),
    initialData: canUseInitialData && !hasActiveFilters ? initialData.trends : undefined,
    staleTime: 30_000,
  })
  const burndownQuery = useQuery({
    queryKey: ['dashboard', 'burndown', selectedTenantId, minAgeDays, platform, deviceGroup],
    queryFn: () => fetchDashboardBurndown({ data: filterParams }),
    staleTime: 30_000,
  })
  const filterOptionsQuery = useQuery({
    queryKey: ['dashboard', 'filter-options', selectedTenantId],
    queryFn: () => fetchDashboardFilterOptions(),
    staleTime: 60_000,
  })

  const globalNavigate = useNavigate()

  const drillToVulnerabilities = useCallback(
    (params: { severity?: string; presentOnly?: boolean }) => {
      void globalNavigate({
        to: '/vulnerabilities',
        search: { page: 1, pageSize: 25, search: '', severity: params.severity ?? '', status: '', source: '', presentOnly: params.presentOnly ?? true, recurrenceOnly: false, minAgeDays: '' },
      } as never)
    },
    [globalNavigate],
  )

  const drillToAssets = useCallback(
    (deviceGroup: string) => {
      void globalNavigate({
        to: '/assets',
        search: { page: 1, pageSize: 25, search: '', assetType: '', criticality: '', ownerType: '', deviceGroup, healthStatus: '', onboardingStatus: '', riskScore: '', exposureLevel: '', tag: '', unassignedOnly: false },
      } as never)
    },
    [globalNavigate],
  )

  const summary = summaryQuery.data ?? (canUseInitialData && !hasActiveFilters ? initialData.summary : undefined)
  const trends = trendsQuery.data ?? (canUseInitialData && !hasActiveFilters ? initialData.trends : undefined)

  if (!summary || !trends) {
    return null
  }

  if (dashboardMode === 'executive') {
    return (
      <CisoExecutiveOverview
        summary={summary}
        trends={trends}
        isLoading={summaryQuery.isFetching || trendsQuery.isFetching}
        filters={filterParams}
        canSwitchToOperations={canSwitchModes}
        onShowOperations={
          canSwitchModes
            ? () => {
                void navigate({
                  search: (prev) => ({ ...prev, mode: 'operations' }),
                })
              }
            : undefined
        }
      />
    )
  }
  const sparklines = summary.metricSparklines
  const statCards = [
    {
      label: 'Critical backlog',
      value: summary.vulnerabilitiesBySeverity.Critical ?? 0,
      icon: ShieldAlert,
      tone: 'text-destructive',
      sparkline: sparklines?.criticalBacklog,
      sparkColor: 'var(--color-destructive)',
    },
    {
      label: 'Overdue actions',
      value: summary.overdueTaskCount,
      icon: TimerReset,
      tone: 'text-primary',
      sparkline: sparklines?.overdueActions,
      sparkColor: 'var(--color-primary)',
    },
    {
      label: 'Healthy tasks',
      value: Math.max(summary.totalTaskCount - summary.overdueTaskCount, 0),
      icon: CheckCircle2,
      tone: 'text-chart-3',
      sparkline: sparklines?.healthyTasks,
      sparkColor: 'var(--color-chart-3)',
    },
    {
      label: 'Open statuses',
      value: Object.values(summary.vulnerabilitiesByStatus).reduce((total, count) => total + count, 0),
      icon: AlertTriangle,
      tone: 'text-chart-2',
      sparkline: sparklines?.openStatuses,
      sparkColor: 'var(--color-chart-2)',
    },
  ]

  return (
    <section className="space-y-6 pb-4">
      {user.roles.includes('SecurityAnalyst') ? (
        <AnalystTriageWorkbench
          items={summary.latestUnhandledVulnerabilities}
          summary={summary}
          isLoading={summaryQuery.isFetching}
        />
      ) : null}

      <DashboardFilterBar
        minAgeDays={minAgeDays}
        platform={platform}
        deviceGroup={deviceGroup}
        filterOptions={filterOptionsQuery.data}
        onMinAgeDaysChange={(value) =>
          void navigate({ search: (prev) => ({ ...prev, minAgeDays: value }) })
        }
        onPlatformChange={(value) =>
          void navigate({ search: (prev) => ({ ...prev, platform: value }) })
        }
        onDeviceGroupChange={(value) =>
          void navigate({ search: (prev) => ({ ...prev, deviceGroup: value }) })
        }
      />

      <Card className="overflow-hidden rounded-2xl border-border/70 bg-[linear-gradient(135deg,color-mix(in_oklab,var(--primary)_18%,transparent),transparent_42%),linear-gradient(180deg,color-mix(in_oklab,var(--card)_92%,black),var(--card))] shadow-[inset_0_1px_0_rgba(255,255,255,0.04)]">
        <CardContent className="p-6 sm:p-7">
          <div className="grid gap-6 xl:grid-cols-[minmax(0,1.8fr)_minmax(22rem,1fr)]">
            <InsetPanel emphasis="subtle" className="p-5 backdrop-blur-sm">
              <TrendChart
                data={trends}
                embedded
                isLoading={trendsQuery.isFetching}
                onSeverityClick={(severity) => drillToVulnerabilities({ severity })}
              />
            </InsetPanel>
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-2">
              {statCards.map((item) => {
                const Icon = item.icon;
                return (
                  <InsetPanel key={item.label} className="p-4 backdrop-blur-sm">
                    <div className="flex items-center justify-between">
                      <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                        {item.label}
                      </p>
                      <Icon className={`size-4 ${item.tone}`} />
                    </div>
                    <p className="mt-3 text-3xl font-semibold tracking-[-0.04em]">
                      {item.value}
                    </p>
                    {item.sparkline && item.sparkline.length >= 2 ? (
                      <Sparkline
                        data={item.sparkline}
                        width={80}
                        height={20}
                        strokeColor={item.sparkColor}
                        fillColor={item.sparkColor}
                        className="mt-2"
                      />
                    ) : null}
                  </InsetPanel>
                );
              })}
            </div>
          </div>
        </CardContent>
      </Card>

      <Tabs
        value={activeTab}
        onValueChange={(value) =>
          void navigate({ search: (prev) => ({ ...prev, tab: value as DashboardTab }) })
        }
      >
        <TabsList className="h-10 w-full justify-start rounded-xl bg-muted/60 p-1">
          <TabsTrigger value="risk" className="rounded-lg px-4 text-sm">
            Risk Overview
          </TabsTrigger>
          <TabsTrigger value="remediation" className="rounded-lg px-4 text-sm">
            Remediation
          </TabsTrigger>
          <TabsTrigger value="infrastructure" className="rounded-lg px-4 text-sm">
            Infrastructure
          </TabsTrigger>
        </TabsList>

        <TabsContent value="risk" className="space-y-6 pt-2">
          <RiskScoreCard
            isLoading={summaryQuery.isFetching}
            filters={filterParams}
          />

          <RiskHeatmap
            filters={filterParams}
            onCellClick={(_group, severity) => {
              drillToVulnerabilities({ severity, presentOnly: true })
            }}
          />

          <div className="grid gap-4 xl:grid-cols-2">
            {summary.vulnerabilityAgeBuckets ? (
              <VulnerabilityAgeChart
                data={summary.vulnerabilityAgeBuckets}
                isLoading={summaryQuery.isFetching}
                onBucketClick={(bucket) => {
                  const ageMap: Record<string, string> = {
                    '0-7 days': '',
                    '8-30 days': '8',
                    '31-90 days': '31',
                    '91-180 days': '91',
                    '180+ days': '181',
                  }
                  const days = ageMap[bucket]
                  if (days !== undefined) {
                    void globalNavigate({
                      to: '/vulnerabilities',
                      search: { page: 1, pageSize: 25, search: '', severity: '', status: '', source: '', presentOnly: true, recurrenceOnly: false, minAgeDays: days },
                    } as never)
                  }
                }}
              />
            ) : null}
            {summary.mttrBySeverity ? (
              <MttrCard
                data={summary.mttrBySeverity}
                isLoading={summaryQuery.isFetching}
              />
            ) : null}
          </div>

          <CriticalVulnerabilities
            items={summary.topCriticalVulnerabilities}
            summary={summary}
            isLoading={summaryQuery.isFetching}
          />
        </TabsContent>

        <TabsContent value="remediation" className="space-y-6 pt-2">
          <BurndownChart
            data={burndownQuery.data}
            isLoading={burndownQuery.isFetching}
          />

          <RemediationVelocity
            averageDays={summary.averageRemediationDays}
            vulnerabilitiesBySeverity={summary.vulnerabilitiesBySeverity}
            isLoading={summaryQuery.isFetching}
          />

          <RiskChangeBriefCard
            brief={summary.riskChangeBrief}
            isLoading={summaryQuery.isFetching}
          />
        </TabsContent>

        <TabsContent value="infrastructure" className="space-y-6 pt-2">
          <DeviceGroupVulnerabilityChart
            data={summary.vulnerabilitiesByDeviceGroup}
            isLoading={summaryQuery.isFetching}
            onBarClick={setSelectedDeviceGroup}
          />

          <div className="grid gap-4 sm:grid-cols-2">
            <DeviceHealthCard
              healthBreakdown={summary.deviceHealthBreakdown}
              isLoading={summaryQuery.isFetching}
            />
            <OnboardingStatusCard
              onboardingBreakdown={summary.deviceOnboardingBreakdown}
              isLoading={summaryQuery.isFetching}
            />
          </div>
        </TabsContent>
      </Tabs>

      <DeviceGroupRiskDetailDialog
        deviceGroupName={selectedDeviceGroup}
        open={selectedDeviceGroup !== null}
        onOpenChange={(open) => {
          if (!open) {
            setSelectedDeviceGroup(null)
          }
        }}
      />
    </section>
  );
}
