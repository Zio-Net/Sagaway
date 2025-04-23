#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 808080
EXPOSE 808081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Sagaway.IntegrationTests/Sagaway.IntegrationTests.OrchestrationService/Sagaway.IntegrationTests.OrchestrationService.csproj", "Sagaway.IntegrationTests/Sagaway.IntegrationTests.OrchestrationService/"]
COPY ["Sagaway.Hosts.DaprActorHost/Sagaway.Hosts.DaprActorHost.csproj", "Sagaway.Hosts.DaprActorHost/"]
COPY ["Sagaway.Callback.Router/Sagaway.Callback.Router.csproj", "Sagaway.Callback.Router/"]
COPY ["Sagaway/Sagaway.csproj", "Sagaway/"]
RUN dotnet restore "./Sagaway.IntegrationTests/Sagaway.IntegrationTests.OrchestrationService/Sagaway.IntegrationTests.OrchestrationService.csproj"
COPY . .
WORKDIR "/src/Sagaway.IntegrationTests/Sagaway.IntegrationTests.OrchestrationService"
RUN dotnet build "./Sagaway.IntegrationTests.OrchestrationService.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Sagaway.IntegrationTests.OrchestrationService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Sagaway.IntegrationTests.OrchestrationService.dll"]