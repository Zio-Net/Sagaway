# Stage 1: Build Tailwind and WebAssembly App
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy only csproj first to optimize Docker caching
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/Sagaway.ReservationDemo.ReservationUI.csproj Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/
# Consider copying package.json and tailwind config earlier too if they don't change often
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/package.json Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/package-lock.json Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/tailwind.config.js Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/
RUN dotnet restore "Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/Sagaway.ReservationDemo.ReservationUI.csproj"

# Copy full source
COPY . .
WORKDIR /src/Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI

# Install Node.js and Tailwind dependencies
# Combine RUN commands where possible
RUN apt-get update && \
    apt-get install -y --no-install-recommends nodejs npm && \
    npm ci && \
    npx @tailwindcss/cli -i ./wwwroot/css/app.css -o ./wwwroot/css/app.output.css --minify && \
    # Clean up apt cache
    rm -rf /var/lib/apt/lists/*

# Build the Blazor WebAssembly app
RUN dotnet publish "Sagaway.ReservationDemo.ReservationUI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Stage 2: Serve using nginx
FROM nginx:alpine AS final

# Remove the default nginx static website and config
RUN rm -rf /usr/share/nginx/html/* && rm /etc/nginx/conf.d/default.conf

# Copy custom nginx configuration template
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/nginx.conf /etc/nginx/templates/default.conf.template

# Copy published Blazor WebAssembly app to nginx public directory
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

# Add entrypoint script to substitute environment variables in the nginx configuration
RUN apk add --no-cache bash
COPY Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/docker-entrypoint.sh /
RUN chmod +x /docker-entrypoint.sh

EXPOSE 80

# Set environment variable with a default value (will be overridden in ACA)
ENV RESERVATION_MANAGER_URL=http://reservation-manager:8080

# Use custom entrypoint script
ENTRYPOINT ["/docker-entrypoint.sh"]
CMD ["nginx", "-g", "daemon off;"]