# Builds the Angular SPA and serves the static bundle behind Caddy on the internal network.
FROM docker.io/library/node:24-alpine AS build
WORKDIR /src
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM docker.io/library/caddy:2-alpine AS runtime
COPY --from=build /src/dist/leo-classroom/browser /srv
COPY frontend.Caddyfile /etc/caddy/Caddyfile
# Writes /srv/config.json from the environment on boot, then hands over to Caddy.
COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh
EXPOSE 80
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
