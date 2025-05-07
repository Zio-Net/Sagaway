#!/bin/bash
set -e

# Replace environment variables in NGINX configuration
envsubst '${RESERVATION_MANAGER_URL}' < /etc/nginx/templates/default.conf.template > /etc/nginx/conf.d/default.conf

# Execute the CMD from the Dockerfile
exec "$@"