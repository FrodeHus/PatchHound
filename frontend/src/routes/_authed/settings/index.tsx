import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, createFileRoute } from "@tanstack/react-router";
import { toast } from "sonner";
import {
  AlertTriangle,
  Bot,
  Building2,
  CircleCheckBig,
  ShieldCheck,
  Check,
} from "lucide-react";
import { useState } from "react";
import { fetchTenantAiProfiles } from "@/api/ai-settings.functions";
import {
  fetchSecureScoreTarget,
  updateSecureScoreTarget,
} from "@/api/secure-score.functions";
import { useTenantScope } from "@/components/layout/tenant-scope";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";

export const Route = createFileRoute("/_authed/settings/")({
  component: SettingsPage,
});

function SettingsPage() {
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

        <SecureScoreTargetCard />
      </div>
    </section>
  );
}

function SecureScoreTargetCard() {
  const queryClient = useQueryClient();
  const { data: currentTarget, isFetching } = useQuery({
    queryKey: ["secure-score", "target"],
    queryFn: () => fetchSecureScoreTarget(),
    staleTime: 30_000,
  });

  const [draft, setDraft] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  // Use the user's draft if they've typed, otherwise show server value
  const displayValue =
    draft ?? (currentTarget != null ? String(currentTarget) : "");

  const mutation = useMutation({
    mutationFn: (targetScore: number) =>
      updateSecureScoreTarget({ data: { targetScore } }),
    onSuccess: async () => {
      toast.success("Target score saved");
      await queryClient.invalidateQueries({ queryKey: ["secure-score"] });
      setDraft(null);
      setSaved(true);
      const id = window.setTimeout(() => setSaved(false), 2000);
      return () => window.clearTimeout(id);
    },
    onError: () => {
      toast.error("Failed to save target score");
    },
  });

  const parsed = Number(displayValue);
  const isValid =
    displayValue !== "" &&
    !Number.isNaN(parsed) &&
    parsed >= 0 &&
    parsed <= 100;
  const hasChanged =
    isValid && currentTarget != null && parsed !== currentTarget;

  return (
    <Card className="rounded-2xl border-border/70 bg-card/85">
      <CardHeader>
        <div className="flex items-center justify-between">
          <CardTitle>Security Posture Settings</CardTitle>
          <ShieldCheck className="size-5 text-primary" />
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-sm leading-6 text-muted-foreground">
          Set the target secure score for this tenant. Assets scoring at or
          below the target are considered stable. Scores up to ⅓ above the
          target show as elevated, and anything higher is critical.
        </p>
        <div className="flex items-end gap-3">
          <div className="space-y-1.5">
            <label
              htmlFor="target-score"
              className="text-xs font-medium text-muted-foreground"
            >
              Target score (0–100)
            </label>
            <Input
              id="target-score"
              type="number"
              min={0}
              max={100}
              step={1}
              value={displayValue}
              onChange={(e) => {
                setDraft(e.target.value);
                setSaved(false);
              }}
              disabled={isFetching}
              className="h-9 w-28 text-sm tabular-nums"
            />
          </div>
          <button
            type="button"
            disabled={!hasChanged || mutation.isPending}
            onClick={() => {
              if (isValid) mutation.mutate(parsed);
            }}
            className="inline-flex h-9 items-center gap-1.5 rounded-xl border border-primary/30 bg-primary/10 px-4 text-sm font-medium text-primary transition-colors hover:bg-primary/20 disabled:pointer-events-none disabled:opacity-40"
          >
            {mutation.isPending ? "Saving…" : "Save"}
          </button>
          {saved && (
            <span className="flex items-center gap-1 text-xs text-tone-success-foreground">
              <Check className="size-3.5" />
              Saved
            </span>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
