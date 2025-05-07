# Stage 1: Build Tailwind and WebAssembly App
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy only csproj first
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/Sagaway.ReservationDemo.ReservationUI.csproj Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/package.json Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/package-lock.json Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/tailwind.config.js Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/
RUN dotnet restore "Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/Sagaway.ReservationDemo.ReservationUI.csproj"

# Copy full source
COPY . .
WORKDIR /src/Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI

# Install Node and Tailwind
RUN apt-get update && \
    apt-get install -y --no-install-recommends nodejs npm gettext-base && \
    npm ci && \
    npx @tailwindcss/cli -i ./wwwroot/css/app.css -o ./wwwroot/css/app.output.css --minify && \
    rm -rf /var/lib/apt/lists/*

# Build the app
RUN dotnet publish "Sagaway.ReservationDemo.ReservationUI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Stage 2: Serve using nginx
FROM nginx:alpine AS final

# Install envsubst for build-time substitution
RUN apk add --no-cache gettext

# Remove default site and config
RUN rm -rf /usr/share/nginx/html/* && rm /etc/nginx/conf.d/default.conf

# Set environment variable for build-time replacement
ARG RESERVATION_MANAGER_URL
ENV RESERVATION_MANAGER_URL=$RESERVATION_MANAGER_URL

# Copy and inject env vars into nginx config at build-time
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/nginx.conf /etc/nginx/templates/default.conf.template
RUN envsubst '${RESERVATION_MANAGER_URL}' < /etc/nginx/templates/default.conf.template > /etc/nginx/conf.d/default.conf

# Copy published app
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
