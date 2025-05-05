#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Sagaway.IntegrationTests/Sagaway.IntegrationTests.TestService/Sagaway.IntegrationTests.TestService.csproj", "Sagaway.IntegrationTests/Sagaway.IntegrationTests.TestService/"]
COPY ["Sagaway.Callback.Propagator/Sagaway.Callback.Propagator.csproj", "Sagaway.Callback.Propagator/"]
RUN dotnet restore "./Sagaway.IntegrationTests/Sagaway.IntegrationTests.TestService/Sagaway.IntegrationTests.TestService.csproj"
COPY . .
WORKDIR "/src/Sagaway.IntegrationTests/Sagaway.IntegrationTests.TestService"
RUN dotnet build "./Sagaway.IntegrationTests.TestService.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Sagaway.IntegrationTests.TestService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Sagaway.IntegrationTests.TestService.dll"]