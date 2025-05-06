# Stage 1: Build Tailwind and WebAssembly App
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy only csproj first to optimize Docker caching
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/Sagaway.ReservationDemo.ReservationUI.csproj Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/package.json Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/package-lock.json Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/tailwind.config.js Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/

RUN dotnet restore "Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/Sagaway.ReservationDemo.ReservationUI.csproj"

# Copy full source
COPY . .
WORKDIR /src/Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI

# Install Node.js and Tailwind dependencies
RUN apt-get update && \
    apt-get install -y --no-install-recommends nodejs npm && \
    npm ci && \
    npx @tailwindcss/cli -i ./wwwroot/css/app.css -o ./wwwroot/css/app.output.css --minify && \
    rm -rf /var/lib/apt/lists/*

# Build the Blazor WebAssembly app
RUN dotnet publish "Sagaway.ReservationDemo.ReservationUI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Stage 2: Serve using nginx
FROM nginx:alpine AS final

# Set ARG to receive backend FQDN if needed later (optional)
ARG BACKEND_HOST_FQDN
ENV BACKEND_HOST_FQDN=${BACKEND_HOST_FQDN}

# Remove default nginx static files and config
RUN rm -rf /usr/share/nginx/html/* && rm /etc/nginx/conf.d/default.conf

# Copy dynamic startup script
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/start.sh /start.sh
RUN chmod +x /start.sh

# Copy nginx config with placeholder
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/nginx.conf /etc/nginx/conf.d/default.conf

# Copy built Blazor app
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

EXPOSE 80

# Start nginx via our script
CMD ["/start.sh"]
