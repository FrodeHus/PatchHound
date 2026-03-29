# Getting Started

This tutorial starts PatchHound locally with Docker so you can explore the full stack quickly.

## Prerequisites

- Docker Desktop or a compatible Docker engine
- A copy of `.env.example` saved as `.env`

## Steps

1. Create the local environment file:

```bash
cp .env.example .env
```

2. Fill in the required values in `.env`.

3. Start the stack:

```bash
docker compose up -d --build
```

4. Open the application:

- Frontend: `http://localhost:3000`
- API: `http://localhost:8080`

## What To Check

- The API container starts without configuration errors.
- The frontend loads and can authenticate.
- Worker services stay healthy after startup.
