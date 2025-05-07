#!/bin/sh
set -e

# Print the environment variable to confirm it's passed
echo "RESERVATION_MANAGER_URL is: ${RESERVATION_MANAGER_URL}"

# Replace environment variables in NGINX configuration
envsubst '${RESERVATION_MANAGER_URL}' < /etc/nginx/templates/default.conf.template > /etc/nginx/conf.d/default.conf

# Print the substituted Nginx config for debugging
echo "--- Substituted Nginx Config (/etc/nginx/conf.d/default.conf) ---"
cat /etc/nginx/conf.d/default.conf
echo "--- End of Substituted Nginx Config ---"

# Execute the CMD from the Dockerfile
exec "$@"