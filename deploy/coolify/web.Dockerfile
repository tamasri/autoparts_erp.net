# The public entry point under Coolify: the built SPA (at /erp/) and the reverse proxy to the API, on plain HTTP port 80.
# Coolify's own proxy (Traefik) terminates HTTPS for the domain and forwards here.
FROM node:20-alpine AS build
WORKDIR /app
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

FROM nginx:1.27-alpine
COPY deploy/coolify/nginx.conf /etc/nginx/nginx.conf
COPY --from=build /app/dist /usr/share/nginx/html/erp
EXPOSE 80
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD wget -qO /dev/null http://127.0.0.1/erp/ || exit 1
