# Dev-only image: keeps the SDK (not just the runtime) so `dotnet watch` can rebuild in place.
# No publish step — your live source is bind-mounted in by compose.dev.yaml, this image just
# restores dependencies once at build time to warm the NuGet cache.
FROM mcr.microsoft.com/dotnet/sdk:10.0

# git is required by IGitService (starter-repo seeding), curl backs the healthcheck — same as prod.
RUN apt-get update && apt-get install -y --no-install-recommends git curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY . .
RUN dotnet restore LeoClassroom/LeoClassroom.csproj

RUN mkdir -p /var/log/leo-classroom

# No ENTRYPOINT here on purpose — compose.dev.yaml supplies the `dotnet watch` command,
# since it needs to reference the bind-mounted path, not whatever COPY put in the image.
