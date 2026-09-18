FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore LeoClassroom/LeoClassroom.csproj
RUN dotnet publish LeoClassroom/LeoClassroom.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# git is required by the server-side IGitService (starter-repo seeding); curl backs the container healthcheck.
RUN apt-get update && apt-get install -y --no-install-recommends git curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
# Serilog's file sink writes here (see appsettings.json). Created in the image so the sink still works when
# nothing is mounted - a missing directory would make Serilog drop the sink silently, with no log to say so.
# compose bind-mounts ./logs over it so the files are readable from the host and survive a recreate.
RUN mkdir -p /var/log/leo-classroom
ENTRYPOINT ["dotnet", "LeoClassroom.dll"]
