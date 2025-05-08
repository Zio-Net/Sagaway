#!/bin/sh
set -e

# Set default value for RESERVATION_MANAGER_URL if not provided
export RESERVATION_MANAGER_URL=${RESERVATION_MANAGER_URL:-http://reservation-manager:80}

# Replace environment variables in NGINX configuration
envsubst '${RESERVATION_MANAGER_URL}' < /etc/nginx/templates/default.conf.template > /etc/nginx/conf.d/default.conf

# Execute the CMD from the Dockerfile
exec "$@"