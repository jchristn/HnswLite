# Build stage — .NET 10 SDK can compile both net8.0 and net10.0 targets.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy source code
COPY . .

# Restore + publish against net8.0 so the runtime image stays compact.
# The library and server are multi-targeted (net8.0;net10.0) for NuGet consumers,
# but the container only needs one runtime.
RUN dotnet restore "HnswIndex.Server/HnswIndex.Server.csproj"

WORKDIR /src/HnswIndex.Server
RUN dotnet publish HnswIndex.Server.csproj \
    -c Release \
    -f net8.0 \
    -o /app/publish \
    /p:UseAppHost=false

# Runtime stage — ASP.NET Core runtime is what Watson needs; smaller than SDK.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        curl \
        ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

RUN mkdir -p /app/data/indexes /app/logs

EXPOSE 8080

ENTRYPOINT ["dotnet", "HnswIndex.Server.dll"]
