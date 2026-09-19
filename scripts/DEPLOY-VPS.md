# VPS Deployment

The authoritative procedure (topology, env keys, troubleshooting, hardening) is in **SETUP_HARDENING.md** §3.

Quick reference, run on the server:

```bash
cd /erp
git pull origin main
bash scripts/deploy-vps.sh
```

The script validates prerequisites, creates/validates `.env.vps`, ensures JWT keys and the seed admin password,
checks PostgreSQL reachability from a container, prepares TLS files, **builds `frontend/dist`**, recreates the
compose stack with `--env-file .env.vps`, and verifies API health and the HTTPS edge.

Do not run `docker compose up` by hand: it skips the frontend rebuild and, without `--env-file`, starts the api with
blank configuration (nginx then answers 502).
