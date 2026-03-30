# Landing Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a bold minimal landing page at `/` with sign-up/login buttons, feature highlights, how-it-works flow, product preview, bottom CTA, and footer.

**Architecture:** Single public route file with self-contained sections. Uses existing Button component, Lucide icons, and Tailwind with PatchHound design tokens. The current `/login` route is deleted — its auth link moves into the landing page nav.

**Tech Stack:** React, TanStack Router, Tailwind CSS v4, Lucide React, existing Button component from `frontend/src/components/ui/button.tsx`

**Spec:** `docs/superpowers/specs/2026-03-26-landing-page-design.md`

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `frontend/src/routes/index.tsx` | Public landing page route (all sections in one file) |
| Delete | `frontend/src/routes/login.tsx` | Remove old login page (replaced by landing page) |

---

### Task 1: Create the landing page route with nav and hero

**Files:**
- Create: `frontend/src/routes/index.tsx`

- [ ] **Step 1: Create the route file with nav bar and hero section**

Create `frontend/src/routes/index.tsx` with:

```tsx
import { createFileRoute } from '@tanstack/react-router'
import { Button } from '@/components/ui/button'
import { Shield, Clock, Users } from 'lucide-react'

export const Route = createFileRoute('/')({
  component: LandingPage,
})

function LandingPage() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      {/* Nav */}
      <nav className="sticky top-0 z-50 flex items-center justify-between px-6 py-4 backdrop-blur-sm bg-background/80 border-b border-border/50">
        <span className="text-xl font-bold text-primary">PatchHound</span>
        <div className="flex items-center gap-3">
          <Button variant="ghost" render={<a href="/auth/login" />}>Log in</Button>
          <Button render={<a href="/auth/login" />}>Sign up</Button>
        </div>
      </nav>

      {/* Hero */}
      <section className="px-6 py-24 md:py-32 max-w-4xl mx-auto">
        <h1 className="text-5xl md:text-7xl font-black leading-[1.1] tracking-tight">
          Track.<br />
          <span className="text-primary">Prioritize.</span><br />
          Remediate.
        </h1>
        <p className="mt-6 text-lg text-muted-foreground max-w-md">
          Vulnerability management that keeps pace with your infrastructure.
        </p>
        <div className="mt-8 flex gap-4">
          <Button size="lg" render={<a href="/auth/login" />}>Get Started</Button>
          <Button variant="outline" size="lg" render={<a href="#features" />}>Learn More</Button>
        </div>
        <div className="mt-8 flex flex-wrap gap-x-6 gap-y-2 text-sm text-muted-foreground">
          <span className="flex items-center gap-1.5"><Shield className="size-4 text-primary" /> Defender Integration</span>
          <span className="flex items-center gap-1.5"><Clock className="size-4 text-primary" /> SLA Tracking</span>
          <span className="flex items-center gap-1.5"><Users className="size-4 text-primary" /> Role-based Access</span>
        </div>
      </section>
    </div>
  )
}
```

- [ ] **Step 2: Verify the page renders**

Run: `cd frontend && npm run typecheck`
Expected: No type errors

- [ ] **Step 3: Commit**

```bash
git add frontend/src/routes/index.tsx
git commit -m "feat: add landing page route with nav and hero"
```

---

### Task 2: Add feature highlights section

**Files:**
- Modify: `frontend/src/routes/index.tsx`

- [ ] **Step 1: Add the features section after the hero**

Add this section inside `LandingPage`, after the hero `</section>` closing tag but before the closing `</div>`:

```tsx
{/* Features */}
<section id="features" className="px-6 py-20 max-w-5xl mx-auto">
  <h2 className="text-3xl font-bold mb-12 text-center">Built for security teams</h2>
  <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
    {[
      {
        icon: Shield,
        title: 'Defender Integration',
        description: 'Ingest vulnerabilities directly from Microsoft Defender with automated, scheduled syncs.',
      },
      {
        icon: Clock,
        title: 'SLA-Driven Remediation',
        description: 'Track remediation against deadlines with automated escalation and approval workflows.',
      },
      {
        icon: LayoutDashboard,
        title: 'Role-Based Dashboards',
        description: 'Executive, operations, and technical views tailored to each role in your organization.',
      },
      {
        icon: Workflow,
        title: 'Workflow Automation',
        description: 'Approval chains, auto-deny on expiry, and full audit trails for every decision.',
      },
    ].map((feature) => (
      <div key={feature.title} className="rounded-xl border border-border bg-card p-6">
        <feature.icon className="size-8 text-primary mb-3" />
        <h3 className="text-lg font-semibold mb-1">{feature.title}</h3>
        <p className="text-sm text-muted-foreground">{feature.description}</p>
      </div>
    ))}
  </div>
</section>
```

Also add `LayoutDashboard` and `Workflow` to the lucide-react import:

```tsx
import { Shield, Clock, Users, LayoutDashboard, Workflow } from 'lucide-react'
```

- [ ] **Step 2: Verify it compiles**

Run: `cd frontend && npm run typecheck`
Expected: No type errors

- [ ] **Step 3: Commit**

```bash
git add frontend/src/routes/index.tsx
git commit -m "feat: add feature highlights section to landing page"
```

---

### Task 3: Add how-it-works section

**Files:**
- Modify: `frontend/src/routes/index.tsx`

- [ ] **Step 1: Add how-it-works section after features**

Add after the features `</section>`:

```tsx
{/* How It Works */}
<section className="px-6 py-20 max-w-4xl mx-auto">
  <h2 className="text-3xl font-bold mb-12 text-center">How it works</h2>
  <div className="grid grid-cols-1 md:grid-cols-3 gap-8">
    {[
      { step: '1', title: 'Connect', description: 'Link your Microsoft Defender environment in minutes.' },
      { step: '2', title: 'Prioritize', description: 'Vulnerabilities are ingested, scored, and ranked by risk.' },
      { step: '3', title: 'Remediate', description: 'Track fixes through SLA-driven workflows to resolution.' },
    ].map((item) => (
      <div key={item.step} className="text-center">
        <div className="inline-flex items-center justify-center size-12 rounded-full bg-primary text-primary-foreground text-lg font-bold mb-4">
          {item.step}
        </div>
        <h3 className="text-lg font-semibold mb-2">{item.title}</h3>
        <p className="text-sm text-muted-foreground">{item.description}</p>
      </div>
    ))}
  </div>
</section>
```

- [ ] **Step 2: Verify it compiles**

Run: `cd frontend && npm run typecheck`
Expected: No type errors

- [ ] **Step 3: Commit**

```bash
git add frontend/src/routes/index.tsx
git commit -m "feat: add how-it-works section to landing page"
```

---

### Task 4: Add product preview section

**Files:**
- Modify: `frontend/src/routes/index.tsx`

- [ ] **Step 1: Add the dashboard preview placeholder after how-it-works**

Add after the how-it-works `</section>`:

```tsx
{/* Product Preview */}
<section className="px-6 py-20 max-w-5xl mx-auto">
  <div className="rounded-2xl border border-border bg-card p-4 shadow-lg">
    <div className="rounded-xl bg-background p-6">
      {/* Mock dashboard wireframe */}
      <div className="flex items-center gap-2 mb-6">
        <div className="size-3 rounded-full bg-destructive/60" />
        <div className="size-3 rounded-full bg-primary/40" />
        <div className="size-3 rounded-full bg-tone-success/40" />
        <span className="ml-2 text-xs text-muted-foreground">PatchHound Console</span>
      </div>
      <div className="grid grid-cols-4 gap-3 mb-4">
        {['Critical', 'High', 'Medium', 'Low'].map((label) => (
          <div key={label} className="rounded-lg bg-card p-3 text-center">
            <div className="text-xs text-muted-foreground mb-1">{label}</div>
            <div className="text-lg font-bold text-foreground">--</div>
          </div>
        ))}
      </div>
      <div className="space-y-2">
        {[75, 60, 45, 30].map((w) => (
          <div key={w} className="h-3 rounded bg-card" style={{ width: `${w}%` }} />
        ))}
      </div>
    </div>
  </div>
</section>
```

- [ ] **Step 2: Verify it compiles**

Run: `cd frontend && npm run typecheck`
Expected: No type errors

- [ ] **Step 3: Commit**

```bash
git add frontend/src/routes/index.tsx
git commit -m "feat: add product preview section to landing page"
```

---

### Task 5: Add bottom CTA and footer

**Files:**
- Modify: `frontend/src/routes/index.tsx`

- [ ] **Step 1: Add bottom CTA and footer after the product preview**

Add after the product preview `</section>`:

```tsx
{/* Bottom CTA */}
<section className="px-6 py-20 text-center max-w-3xl mx-auto">
  <h2 className="text-3xl font-bold mb-4">Ready to take control of your vulnerabilities?</h2>
  <p className="text-muted-foreground mb-8">Get started with PatchHound today.</p>
  <div className="flex justify-center gap-4">
    <Button size="lg" render={<a href="/auth/login" />}>Sign Up</Button>
    <Button variant="outline" size="lg" render={<a href="/auth/login" />}>Log In</Button>
  </div>
</section>

{/* Footer */}
<footer className="border-t border-border px-6 py-8">
  <div className="max-w-5xl mx-auto flex flex-col md:flex-row items-center justify-between gap-4 text-sm text-muted-foreground">
    <span>&copy; {new Date().getFullYear()} PatchHound</span>
    <div className="flex gap-6">
      <a href="#" className="hover:text-foreground transition-colors">Docs</a>
      <a href="#" className="hover:text-foreground transition-colors">GitHub</a>
      <a href="#" className="hover:text-foreground transition-colors">Contact</a>
    </div>
  </div>
</footer>
```

- [ ] **Step 2: Verify it compiles**

Run: `cd frontend && npm run typecheck`
Expected: No type errors

- [ ] **Step 3: Commit**

```bash
git add frontend/src/routes/index.tsx
git commit -m "feat: add bottom CTA and footer to landing page"
```

---

### Task 6: Add smooth scroll for Learn More and delete old login page

**Files:**
- Modify: `frontend/src/routes/index.tsx`
- Delete: `frontend/src/routes/login.tsx`

- [ ] **Step 1: Add smooth scroll behavior**

In `frontend/src/routes/index.tsx`, change the "Learn More" button's `<a>` tag to use an onClick handler instead of a plain anchor:

Replace the Learn More button:
```tsx
<Button variant="outline" size="lg" render={<a href="#features" />}>Learn More</Button>
```

With:
```tsx
<Button
  variant="outline"
  size="lg"
  onClick={() => document.getElementById('features')?.scrollIntoView({ behavior: 'smooth' })}
>
  Learn More
</Button>
```

- [ ] **Step 2: Delete the old login page**

Delete `frontend/src/routes/login.tsx`.

- [ ] **Step 3: Verify everything compiles and lint passes**

Run: `cd frontend && npm run typecheck && npm run lint`
Expected: No errors

- [ ] **Step 4: Commit**

```bash
git rm frontend/src/routes/login.tsx
git add frontend/src/routes/index.tsx
git commit -m "feat: add smooth scroll, remove old login page"
```

---

### Task 7: Final verification

- [ ] **Step 1: Run full frontend checks**

Run: `cd frontend && npm run typecheck && npm run lint && npm test`
Expected: All pass

- [ ] **Step 2: Start dev server and visually verify**

Run: `cd frontend && npm run dev`

Verify in browser:
- Landing page renders at `/`
- Nav has Log in and Sign up buttons
- Hero displays "Track. Prioritize. Remediate."
- Learn More scrolls to features
- Feature cards render with icons
- How-it-works shows 3 steps
- Dashboard preview placeholder renders
- Bottom CTA and footer display
- Log in / Sign up buttons link to `/auth/login`
- Authenticated users still reach dashboard at `/_authed/`

- [ ] **Step 3: Final commit if any fixes needed**

```bash
git add -u
git commit -m "fix: landing page adjustments from manual review"
```
