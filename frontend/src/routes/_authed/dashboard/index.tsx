import { useQuery } from '@tanstack/react-query'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useCallback, useState } from 'react'
import { z } from 'zod'
import { AlertTriangle, CheckCircle2, ShieldAlert, TimerReset } from 'lucide-react'
import {
  fetchDashboardBurndown,
  fetchDashboardSummary,
  fetchDashboardTrends,
} from "@/api/dashboard.functions";
import { Card, CardContent } from "@/components/ui/card";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { InsetPanel } from "@/components/ui/inset-panel";
import { Sparkline } from "@/components/features/dashboard/Sparkline";
import { CriticalVulnerabilities } from "@/components/features/dashboard/CriticalVulnerabilities";
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
import { AnalystTriageWorkbench } from '@/components/features/dashboard/AnalystTriageWorkbench'
import { useTenantScope } from '@/components/layout/tenant-scope'

const TAB_VALUES = ['risk', 'remediation', 'infrastructure'] as const
type DashboardTab = (typeof TAB_VALUES)[number]

const dashboardSearchSchema = z.object({
  tab: z.enum(TAB_VALUES).optional().catch(undefined),
  minAgeDays: z.string().optional().catch(undefined),
  platform: z.string().optional().catch(undefined),
  deviceGroup: z.string().optional().catch(undefined),
})

export const Route = createFileRoute('/_authed/dashboard/')({
  validateSearch: dashboardSearchSchema,
  component: DashboardPage,
})

function DashboardPage() {
  const { user } = Route.useRouteContext()
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const { selectedTenantId } = useTenantScope()
  const [selectedDeviceGroup, setSelectedDeviceGroup] = useState<string | null>(null)

  const activeTab: DashboardTab = search.tab ?? 'risk'
  const minAgeDays = search.minAgeDays ?? ''
  const platform = search.platform ?? ''
  const deviceGroup = search.deviceGroup ?? ''

  const filterParams = {
    minAgeDays: minAgeDays ? Number(minAgeDays) : undefined,
    platform: platform || undefined,
    deviceGroup: deviceGroup || undefined,
  }

  const summaryQuery = useQuery({
    queryKey: ['dashboard', 'summary', selectedTenantId, minAgeDays, platform, deviceGroup],
    queryFn: () => fetchDashboardSummary({ data: filterParams }),
    enabled: Boolean(selectedTenantId),
    staleTime: 30_000,
  })
  const trendsQuery = useQuery({
    queryKey: ['dashboard', 'trends', selectedTenantId, minAgeDays, platform, deviceGroup],
    queryFn: () => fetchDashboardTrends({ data: filterParams }),
    enabled: Boolean(selectedTenantId),
    staleTime: 30_000,
  })
  const burndownQuery = useQuery({
    queryKey: ['dashboard', 'burndown', selectedTenantId, minAgeDays, platform, deviceGroup],
    queryFn: () => fetchDashboardBurndown({ data: filterParams }),
    enabled: Boolean(selectedTenantId),
    staleTime: 30_000,
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

  const summary = summaryQuery.data
  const trends = trendsQuery.data

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
      {(user.activeRoles ?? []).includes('SecurityAnalyst') ? (
        <AnalystTriageWorkbench
          items={summary.latestUnhandledVulnerabilities}
          summary={summary}
          isLoading={summaryQuery.isFetching}
        />
      ) : null}

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

      {/*
        Unified tab surface: the strip sits above the pane and merges into it
        via negative margin. Both share one continuous glass/card surface.
      */}
      <Tabs
        value={activeTab}
        onValueChange={(value) =>
          void navigate({ search: (prev) => ({ ...prev, tab: value as DashboardTab }) })
        }
      >
        {/* Strip — top of unified surface (rounded top, square bottom) */}
        <TabsList className="relative z-10 mb-[-22px] h-auto w-full justify-start rounded-t-2xl rounded-b-none border border-b-0 border-border/70 bg-card/80 px-1.5 pt-1.5 pb-7 backdrop-blur-sm after:absolute after:inset-x-4 after:bottom-[18px] after:h-px after:bg-border/60 after:content-['']">
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

        {/* Pane — bottom of unified surface (square top, rounded bottom) */}
        <div className="relative z-0 overflow-hidden rounded-b-2xl border border-border/70 bg-card shadow-[inset_0_1px_0_rgba(255,255,255,0.03)]">
          <TabsContent value="risk" className="mt-0">
            <RiskScoreCard
              isLoading={summaryQuery.isFetching}
              filters={filterParams}
            />
          </TabsContent>

          <TabsContent value="remediation" className="mt-0">
            <BurndownChart
              data={burndownQuery.data}
              isLoading={burndownQuery.isFetching}
            />
          </TabsContent>

          <TabsContent value="infrastructure" className="mt-0">
            <DeviceGroupVulnerabilityChart
              data={summary.vulnerabilitiesByDeviceGroup}
              isLoading={summaryQuery.isFetching}
              onBarClick={setSelectedDeviceGroup}
            />
          </TabsContent>
        </div>
      </Tabs>

      {/* Below-pane content — gated by activeTab so it stays in sync */}
      {activeTab === 'risk' && (
        <div className="space-y-6">
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
        </div>
      )}

      {activeTab === 'remediation' && (
        <div className="space-y-6">
          <RemediationVelocity
            averageDays={summary.averageRemediationDays}
            vulnerabilitiesBySeverity={summary.vulnerabilitiesBySeverity}
            isLoading={summaryQuery.isFetching}
          />

          <RiskChangeBriefCard
            brief={summary.riskChangeBrief}
            isLoading={summaryQuery.isFetching}
          />
        </div>
      )}

      {activeTab === 'infrastructure' && (
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
      )}

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
