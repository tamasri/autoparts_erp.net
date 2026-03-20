# Cloudflare Configuration Guide

## DNS Settings
- Type: A, Name: `@`, Value: `<hetzner-vps-ip>`, Proxy: ON
- Type: A, Name: `www`, Value: `<hetzner-vps-ip>`, Proxy: ON
- Type: CNAME, Name: `grafana`, Value: `@`, Proxy: ON

## SSL/TLS Settings
- Mode: Full (Strict)
- Minimum TLS Version: 1.2
- TLS 1.3: Enabled
- Automatic HTTPS Rewrites: ON
- Always Use HTTPS: ON

## Security Settings
- WAF: ON (free plan managed rules)
- Bot Fight Mode: ON
- Security Level: Medium
- Challenge Passage: 30 minutes

## CORS Guidance
- CORS is configured in Nginx/API, not Cloudflare.
- Do not add Cloudflare transform rules for CORS headers.

## Rate Limiting
- Use Nginx limits:
- General API: 30 requests/sec.
- Login route: 5 requests/min.

## Cloudflare Access (Grafana)
- Protect `https://grafana.your-domain.com`.
- Allow only approved team email accounts.

## Cache Rules
- `/api/*`: Bypass cache
- `/assets/*`: Cache Everything, TTL 1 year
