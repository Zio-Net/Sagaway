#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Sagaway.ReservationDemo/Sagaway.ReservationDemo.BillingManagement/Sagaway.ReservationDemo.BillingManagement.csproj", "Sagaway.ReservationDemo/Sagaway.ReservationDemo.BillingManagement/"]
RUN dotnet restore "./Sagaway.ReservationDemo/Sagaway.ReservationDemo.BillingManagement/./Sagaway.ReservationDemo.BillingManagement.csproj"
COPY . .
WORKDIR "/src/Sagaway.ReservationDemo/Sagaway.ReservationDemo.BillingManagement"
RUN dotnet build "./Sagaway.ReservationDemo.BillingManagement.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Sagaway.ReservationDemo.BillingManagement.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Sagaway.ReservationDemo.BillingManagement.dll"]