import { useQuery } from "@tanstack/react-query";
import { Link, createFileRoute, redirect } from "@tanstack/react-router";
import {
  AlertTriangle,
  Bot,
  Building2,
  CircleCheckBig,
  Mail,
} from "lucide-react";
import { fetchTenantAiProfiles } from "@/api/ai-settings.functions";
import { useTenantScope } from "@/components/layout/tenant-scope";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export const Route = createFileRoute("/_authed/settings/")({
  beforeLoad: ({ context }) => {
    const activeRoles = context.user?.activeRoles ?? []
    if (!activeRoles.includes("GlobalAdmin") && !activeRoles.includes("SecurityManager")) {
      throw redirect({ to: "/dashboard" })
    }
  },
  component: SettingsPage,
});

function SettingsPage() {
  const { user } = Route.useRouteContext();
  const { selectedTenantId } = useTenantScope();
  const profilesQuery = useQuery({
    queryKey: ["tenant-ai-profiles", selectedTenantId],
    queryFn: () => fetchTenantAiProfiles(),
    enabled: !!selectedTenantId,
  });

  const defaultAiProfile =
    (profilesQuery.data ?? []).find((profile) => profile.isDefault) ?? null;
  const aiState = !defaultAiProfile
    ? "missing"
    : !defaultAiProfile.isEnabled ||
        defaultAiProfile.lastValidationStatus !== "Valid"
      ? "blocked"
      : "healthy";

  return (
    <section className="space-y-4 pb-4">
      <h1 className="text-2xl font-semibold">Settings</h1>
      <div className="grid gap-4 xl:grid-cols-2">
        <Link
          to="/admin/tenants"
          search={{ page: 1, pageSize: 25 }}
          className="group"
        >
          <Card className="rounded-2xl border-border/70 bg-card/85 transition-colors group-hover:border-primary/35">
            <CardHeader>
              <div className="flex items-center justify-between">
                <CardTitle>Tenant Administration</CardTitle>
                <Building2 className="size-5 text-primary" />
              </div>
            </CardHeader>
            <CardContent className="text-sm leading-6 text-muted-foreground">
              Review configured tenants, rename them, and maintain ingestion
              credentials and sync schedules per source.
            </CardContent>
          </Card>
        </Link>

        <Link to="/settings/ai" className="group">
          <Card className="rounded-2xl border-border/70 bg-card/85 transition-colors group-hover:border-primary/35">
            <CardHeader>
              <div className="flex items-center justify-between">
                <div className="space-y-2">
                  <CardTitle>AI Configuration</CardTitle>
                  <div className="flex flex-wrap items-center gap-2">
                    {aiState === "healthy" ? (
                      <Badge variant="secondary">
                        <CircleCheckBig className="size-3" />
                        Ready
                      </Badge>
                    ) : null}
                    {aiState === "blocked" ? (
                      <Badge variant="destructive">
                        <AlertTriangle className="size-3" />
                        Blocked
                      </Badge>
                    ) : null}
                    {aiState === "missing" ? (
                      <Badge variant="outline">Not configured</Badge>
                    ) : null}
                  </div>
                </div>
                <Bot className="size-5 text-primary" />
              </div>
            </CardHeader>
            <CardContent className="text-sm leading-6 text-muted-foreground">
              {aiState === "healthy"
                ? `Default profile ${defaultAiProfile?.name} is validated and ready to generate tenant AI reports.`
                : aiState === "blocked"
                  ? `Default profile ${defaultAiProfile?.name ?? "for this tenant"} is currently blocking AI report generation. Revalidate or fix the provider configuration.`
                  : "Configure Ollama, Azure OpenAI, or OpenAI profiles for the active tenant, including prompts and runtime settings."}
            </CardContent>
          </Card>
        </Link>

        {(user.activeRoles ?? []).includes("GlobalAdmin") ? (
          <Link to="/settings/notifications" className="group">
            <Card className="rounded-2xl border-border/70 bg-card/85 transition-colors group-hover:border-primary/35">
              <CardHeader>
                <div className="flex items-center justify-between">
                  <CardTitle>Notification Delivery</CardTitle>
                  <Mail className="size-5 text-primary" />
                </div>
              </CardHeader>
              <CardContent className="text-sm leading-6 text-muted-foreground">
                Configure outbound notification providers in one place, including SMTP and Mailgun, and validate delivery before relying on it.
              </CardContent>
            </Card>
          </Link>
        ) : null}

      </div>
    </section>
  );
}
