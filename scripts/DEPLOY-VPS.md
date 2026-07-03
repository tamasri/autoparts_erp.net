# VPS Bootstrap (Single Script)

## 1) Upload project to VPS

Clone or copy the repository to your VPS.

## 2) Make the script executable

```bash
chmod +x scripts/deploy-vps.sh
```

## 3) Run one command

```bash
./scripts/deploy-vps.sh
```

The script performs these detailed steps:

1. Validates Docker + Compose + OpenSSL.
2. Creates `.env.vps` from `.env.vps.template` if missing.
3. Validates required env values.
4. Generates JWT RSA keys automatically if missing.
5. Verifies external PostgreSQL connectivity.
6. Creates TLS certs under `nginx/certs` if missing (self-signed fallback).
7. Builds `frontend/dist` in a Node container.
8. Runs `docker compose -f docker-compose.vps.yml up -d --build`.
9. Verifies API health and prints logs on failure.

## 4) Configure production values first

Edit `.env.vps` and set:

- `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`
- `REDIS_PASSWORD`
- `ALLOWED_ORIGINS`

If you already have your own JWT keys, set:

- `JWT_PRIVATE_KEY`
- `JWT_PUBLIC_KEY`

Otherwise, leave placeholders and script will generate them.

## 5) Commands after deployment

```bash
docker compose --env-file .env.vps -f docker-compose.vps.yml ps
docker compose --env-file .env.vps -f docker-compose.vps.yml logs -f api
docker compose --env-file .env.vps -f docker-compose.vps.yml logs -f nginx
```
