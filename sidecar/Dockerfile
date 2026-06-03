# syntax=docker/dockerfile:1.7

# ── Build ──────────────────────────────────────────────────────
FROM node:22-alpine AS builder
WORKDIR /build
# better-sqlite3 kompiliert ein natives Modul → Build-Tools nötig.
RUN apk add --no-cache python3 make g++
COPY package.json package-lock.json* ./
RUN npm install
COPY tsconfig.json ./
COPY src ./src
RUN npm run build && npm prune --omit=dev

# ── Runtime ────────────────────────────────────────────────────
FROM node:22-alpine AS runtime
RUN apk add --no-cache tini \
 && addgroup -g 10001 wa \
 && adduser  -D -u 10001 -G wa wa \
 && mkdir -p /data \
 && chown -R 10001:10001 /data

WORKDIR /app
COPY --from=builder --chown=10001:10001 /build/node_modules ./node_modules
COPY --from=builder --chown=10001:10001 /build/dist         ./dist
COPY --from=builder --chown=10001:10001 /build/package.json ./package.json

USER 10001:10001

ENV NODE_ENV=production \
    DATA_DIR=/data \
    PORT=3000

VOLUME ["/data"]
EXPOSE 3000

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://127.0.0.1:3000/health || exit 1

ENTRYPOINT ["/sbin/tini", "--"]
CMD ["node", "dist/index.js"]
