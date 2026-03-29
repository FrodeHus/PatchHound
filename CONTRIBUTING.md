# Contributing

PatchHound is still evolving, so small, focused contributions are easier to review and merge than broad refactors.

## Development Setup

1. Copy `.env.example` to `.env`.
2. Fill in the required local settings.
3. Start dependencies with `docker compose up -d`.
4. Build and test the backend:

```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx -v minimal
```

5. Start the frontend:

```bash
cd frontend
npm install
npm run dev
```

## Working Style

- Keep changes scoped to one concern.
- Add or update tests for behavior changes.
- Prefer explicit, readable code over clever abstractions.
- Avoid unrelated cleanup in the same pull request.

## Pull Requests

- Use a concise summary that explains the change and why it exists.
- Include test evidence for backend and frontend changes when relevant.
- Add screenshots or short recordings for UI work.
- Call out migrations, config changes, or operational impact clearly.

## Commit Messages

Use conventional commits where possible, for example:

- `feat: add remediation due date reminders`
- `fix: skip duplicate audit writes for bookkeeping updates`
- `docs: simplify local development guide`
