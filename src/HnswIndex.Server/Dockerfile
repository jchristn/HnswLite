FROM mcr.microsoft.com/dotnet/sdk:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy source code
COPY . .

# Restore dependencies
RUN dotnet restore "HnswIndex.Server/HnswIndex.Server.csproj"

# Build the application
WORKDIR /src/HnswIndex.Server
RUN dotnet build HnswIndex.Server.csproj -c Release -o /app/build

FROM build AS publish
RUN dotnet publish HnswIndex.Server.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
RUN apt-get update && apt-get install -y iputils-ping traceroute net-tools curl wget dnsutils iproute2 vim file && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /app/publish .

# Create directories
RUN mkdir -p /app/data/indexes
RUN mkdir -p /app/logs

# Expose port
EXPOSE 8080

ENTRYPOINT ["dotnet", "HnswIndex.Server.dll"]