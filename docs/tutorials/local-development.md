# Local Development

This setup is intended for day-to-day backend and frontend work.

## Backend

Build and test the backend from the repository root:

```bash
dotnet build PatchHound.slnx
dotnet test PatchHound.slnx -v minimal
```

Run the API and worker in separate terminals:

```bash
dotnet run --project src/PatchHound.Api
dotnet run --project src/PatchHound.Worker
```

## Frontend

Start the frontend from `frontend/`:

```bash
npm install
npm run lint
npm run typecheck
npm test
npm run dev
```

## Notes

- Keep `.env` out of version control.
- Use Docker when you need the full dependency stack locally.
- Update tests with any behavior change that affects API contracts, persistence, or UI flows.
