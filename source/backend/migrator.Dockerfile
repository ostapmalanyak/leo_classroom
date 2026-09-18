# Builds the EF Core migration bundle and applies it to completion, then exits 0.
# Run before the backend boots (compose: backend depends_on migrator service_completed_successfully).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet tool install --global dotnet-ef --version 10.*
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet restore LeoClassroom/LeoClassroom.csproj
# Self-contained bundle that applies the LeoClassroom.Persistence migrations against a given connection.
RUN dotnet ef migrations bundle \
    --project LeoClassroom.Persistence/LeoClassroom.Persistence.csproj \
    --startup-project LeoClassroom/LeoClassroom.csproj \
    --configuration Release --self-contained -r linux-x64 -o /efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
WORKDIR /app
COPY --from=build /efbundle ./efbundle
COPY migrator-entrypoint.sh ./entrypoint.sh
RUN chmod +x ./entrypoint.sh
ENTRYPOINT ["./entrypoint.sh"]
