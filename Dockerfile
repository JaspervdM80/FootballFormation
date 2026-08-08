FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/FootballFormation.Web/FootballFormation.Web.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

# The commit this image was built from, reported by /health. Re-declared here because an ARG is
# scoped to the stage that declares it. Defaults to "unknown" so a local `docker build` still
# works; CI passes the real value with --build-arg (see .github/workflows/fly-deploy.yml).
ARG GIT_SHA=unknown
ENV APP_GIT_SHA=${GIT_SHA}

# Persistent volume mount point (DB, logs, data-protection keys)
ENV APP_DATA_DIR=/data
# Trust the proxy's X-Forwarded-Proto so the app knows requests arrived over HTTPS
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

EXPOSE 8080
ENTRYPOINT ["dotnet", "FootballFormation.Web.dll"]
