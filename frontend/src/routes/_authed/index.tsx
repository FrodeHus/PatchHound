import { useQuery } from '@tanstack/react-query'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useCallback, useState } from 'react'
import { z } from 'zod'
import { AlertTriangle, CheckCircle2, ShieldAlert, TimerReset } from 'lucide-react'
import { fetchDashboardBurndown, fetchDashboardFilterOptions, fetchDashboardSummary, fetchDashboardTrends } from '@/api/dashboard.functions'
import { Card, CardContent } from '@/components/ui/card'
import { InsetPanel } from '@/components/ui/inset-panel'
import { Sparkline } from '@/components/features/dashboard/Sparkline'
import { CriticalVulnerabilities } from '@/components/features/dashboard/CriticalVulnerabilities'
import { DashboardFilterBar } from '@/components/features/dashboard/DashboardFilterBar'
import { DeviceGroupVulnerabilityChart } from '@/components/features/dashboard/DeviceGroupVulnerabilityChart'
import { DeviceHealthCard } from '@/components/features/dashboard/DeviceHealthCard'
import { OnboardingStatusCard } from '@/components/features/dashboard/OnboardingStatusCard'
import { ExposureSlaCard } from '@/components/features/dashboard/ExposureSlaCard'
import { RemediationVelocity } from '@/components/features/dashboard/RemediationVelocity'
import { RiskChangeBriefCard } from '@/components/features/dashboard/RiskChangeBriefCard'
import { TrendChart } from '@/components/features/dashboard/TrendChart'
import { RiskHeatmap } from '@/components/features/dashboard/RiskHeatmap'
import { VulnerabilityAgeChart } from '@/components/features/dashboard/VulnerabilityAgeChart'
import { MttrCard } from '@/components/features/dashboard/MttrCard'
import { BurndownChart } from '@/components/features/dashboard/BurndownChart'
import { useTenantScope } from '@/components/layout/tenant-scope'

const dashboardSearchSchema = z.object({
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
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const { selectedTenantId } = useTenantScope()
  const [initialTenantId] = useState(selectedTenantId)
  const canUseInitialData = initialTenantId === selectedTenantId

  const minAgeDays = search.minAgeDays ?? ''
  const platform = search.platform ?? ''
  const deviceGroup = search.deviceGroup ?? ''

  const filterParams = {
    minAgeDays: minAgeDays ? Number(minAgeDays) : undefined,
    platform: platform || undefined,
    deviceGroup: deviceGroup || undefined,
  }

  const hasActiveFilters = Boolean(minAgeDays || platform || deviceGroup)

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
        search: { page: 1, pageSize: 25, search: '', severity: params.severity ?? '', status: '', source: '', presentOnly: params.presentOnly ?? true, recurrenceOnly: false },
      } as never)
    },
    [globalNavigate],
  )

  const drillToAssets = useCallback(
    (deviceGroup: string) => {
      void navigate({ search: (prev) => ({ ...prev, deviceGroup }) })
    },
    [navigate],
  )

  const summary = summaryQuery.data ?? (canUseInitialData && !hasActiveFilters ? initialData.summary : undefined)
  const trends = trendsQuery.data ?? (canUseInitialData && !hasActiveFilters ? initialData.trends : undefined)

  if (!summary || !trends) {
    return null
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

      <DeviceGroupVulnerabilityChart
        data={summary.vulnerabilitiesByDeviceGroup}
        isLoading={summaryQuery.isFetching}
        onBarClick={drillToAssets}
      />
      <RiskHeatmap
        data={summary.vulnerabilitiesByDeviceGroup}
        isLoading={summaryQuery.isFetching}
        onCellClick={(group, _severity) => {
          void navigate({ search: (prev) => ({ ...prev, deviceGroup: group }) })
        }}
      />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-2">
        <DeviceHealthCard
          healthBreakdown={summary.deviceHealthBreakdown}
          isLoading={summaryQuery.isFetching}
        />
        <OnboardingStatusCard
          onboardingBreakdown={summary.deviceOnboardingBreakdown}
          isLoading={summaryQuery.isFetching}
        />
      </div>
      <div className="grid gap-4 xl:grid-cols-3">
        <div className="xl:col-span-2">
          <ExposureSlaCard
            exposureScore={summary.exposureScore}
            slaCompliancePercent={summary.slaCompliancePercent}
            overdueCount={summary.overdueTaskCount}
            totalCount={summary.totalTaskCount}
            slaComplianceTrend={summary.slaComplianceTrend}
            isLoading={summaryQuery.isFetching}
          />
        </div>
      </div>

      {summary.mttrBySeverity ? (
        <MttrCard
          data={summary.mttrBySeverity}
          isLoading={summaryQuery.isFetching}
        />
      ) : null}

      {summary.vulnerabilityAgeBuckets ? (
        <div className="grid gap-4 xl:grid-cols-3">
          <div className="xl:col-span-2">
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
                  void navigate({ search: (prev) => ({ ...prev, minAgeDays: days }) })
                }
              }}
            />
          </div>
        </div>
      ) : null}

      <div className="grid gap-4 xl:grid-cols-3">
        <div className="xl:col-span-2">
          <RemediationVelocity
            averageDays={summary.averageRemediationDays}
            vulnerabilitiesBySeverity={summary.vulnerabilitiesBySeverity}
            isLoading={summaryQuery.isFetching}
          />
        </div>
      </div>

      <BurndownChart
        data={burndownQuery.data}
        isLoading={burndownQuery.isFetching}
      />

      <RiskChangeBriefCard
        brief={summary.riskChangeBrief}
        isLoading={summaryQuery.isFetching}
      />

      <div className="grid gap-4 xl:grid-cols-5">
        <div className="xl:col-span-5">
          <CriticalVulnerabilities
            items={summary.topCriticalVulnerabilities}
            summary={summary}
            isLoading={summaryQuery.isFetching}
          />
        </div>
      </div>
    </section>
  );
}
