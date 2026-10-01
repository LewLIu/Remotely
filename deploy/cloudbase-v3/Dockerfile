FROM mcr.microsoft.com/dotnet/aspnet:8.0

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=5000

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/AppData

COPY publish/ /app/

EXPOSE 5000

ENTRYPOINT ["dotnet", "Remotely_Server.dll"]

# Liveness only: any HTTP response proves the ASP.NET process is serving requests.
# Do not use -f here because authenticated endpoints may legitimately return 401.
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -sS -o /dev/null http://127.0.0.1:${ASPNETCORE_HTTP_PORTS}/ || exit 1
