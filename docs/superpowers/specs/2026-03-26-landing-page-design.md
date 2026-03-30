# PatchHound Landing Page Design

## Overview

Public-facing landing page for PatchHound that serves as the unauthenticated entry point at `/`. Provides a brief value proposition with sign-up and login access. Replaces the current minimal `/login` page as the front door.

## Design Direction

**Bold Minimal** — large typographic hero, punchy tagline, single primary CTA, subtle feature checklist. High-impact, minimal clutter.

## Route & Structure

- Landing page becomes the public route at `/` (`frontend/src/routes/index.tsx`)
- The existing authenticated dashboard already lives at `_authed/index.tsx` — no move needed
- Current `/login` route (`frontend/src/routes/login.tsx`) is replaced by the landing page; auth flow still uses `/auth/login`
- Built as a single-file React component using existing Tailwind + design tokens from `app.css`
- No new dependencies required

## Page Sections

### 1. Sticky Nav Bar

- **Left:** PatchHound wordmark (text, styled in primary color)
- **Right:** "Log in" (ghost/outline button linking to `/auth/login`) + "Sign up" (primary/lime button linking to `/auth/login` or future `/signup`)
- Sticky on scroll with subtle background blur/opacity change

### 2. Hero (Bold Minimal)

- Large typographic headline with line breaks:
  ```
  Track.
  Prioritize.   ← highlighted in primary/lime color
  Remediate.
  ```
- Subtitle: "Vulnerability management that keeps pace with your infrastructure."
- Two CTAs:
  - "Get Started" — primary button, links to `/auth/login`
  - "Learn More" — outline button, smooth-scrolls to features section
- Subtle feature checklist row below CTAs:
  - ✓ Defender Integration
  - ✓ SLA Tracking
  - ✓ Role-based Access

### 3. Feature Highlights

Responsive grid of 3-4 feature cards. Each card has an icon, title, and one-line description.

| Feature | Description |
|---------|-------------|
| Defender Integration | Ingest vulnerabilities directly from Microsoft Defender |
| SLA-Driven Remediation | Track remediation against deadlines with automated escalation |
| Role-Based Dashboards | Executive, operations, and technical views tailored to each role |
| Workflow Automation | Approval chains, auto-deny on expiry, and full audit trails |

Cards use existing `card` design tokens. Icons from Lucide React (already a dependency).

### 4. How It Works

Horizontal 3-step flow with connecting visual elements (lines or arrows):

1. **Connect** — Link your Microsoft Defender environment
2. **Prioritize** — Vulnerabilities are ingested, scored, and ranked by risk
3. **Remediate** — Track fixes through SLA-driven workflows to resolution

Each step: numbered circle or icon + title + one-line description. Stacks vertically on mobile.

### 5. Product Screenshot / Preview

- Stylized mockup or placeholder of the PatchHound dashboard
- Wrapped in a card-like container with slight shadow for depth
- Use a styled placeholder div (wireframe-style with CSS) initially — no image asset needed
- Full-width with max-width constraint

### 6. Bottom CTA

- Headline: "Ready to take control of your vulnerabilities?"
- Two buttons: "Sign Up" (primary) + "Log In" (outline)
- Both link to `/auth/login` (sign-up flow TBD — currently uses Microsoft Entra, so both go to same auth endpoint)

### 7. Minimal Footer

- Copyright: "© 2026 PatchHound"
- Links: Docs, GitHub, Contact (all using `#` placeholder hrefs)
- Simple single-row layout

## Technical Details

### Styling
- Uses existing design tokens from `frontend/src/styles/app.css`
- Dark theme (PatchHound default) — `background`, `foreground`, `primary`, `card`, `muted-foreground` tokens
- Geist Variable font (already loaded)
- Tailwind utility classes, consistent with rest of the app
- Responsive breakpoints: mobile-first, stacks sections vertically on small screens

### Components
- Self-contained in the route file — no new shared components needed
- Uses existing `Button` component from `frontend/src/components/ui/button.tsx` for CTAs
- Lucide React icons for feature cards (Shield, Clock, LayoutDashboard, Workflow or similar)

### Interactions
- Smooth scroll for "Learn More" → features section
- Sticky nav with background transition on scroll (CSS or minimal JS)
- No animations beyond standard hover states on buttons/cards

### Auth Flow
- "Log in" and "Sign up" both currently route to `/auth/login` (Microsoft Entra OAuth)
- Future: "Sign up" could link to a dedicated registration flow when available
- No changes to existing auth infrastructure needed

## Out of Scope

- Pricing page
- Documentation site
- Testimonials / customer logos
- Blog or changelog
- Theme toggle on landing page (inherits dark theme)
