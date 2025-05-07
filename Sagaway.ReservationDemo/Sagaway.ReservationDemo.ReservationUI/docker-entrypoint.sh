#!/bin/sh
set -e

# Print the environment variable to confirm it's passed
echo "RESERVATION_MANAGER_URL is: ${RESERVATION_MANAGER_URL}"

# Extract FQDN for nslookup and curl
RESERVATION_MANAGER_FQDN_WITH_SCHEMA=$(echo "${RESERVATION_MANAGER_URL}")
RESERVATION_MANAGER_FQDN_NO_SCHEMA=$(echo "${RESERVATION_MANAGER_URL}" | sed -e 's|^[^/]*//||' -e 's|/.*$||')

echo "Attempting to resolve FQDN: ${RESERVATION_MANAGER_FQDN_NO_SCHEMA}"
nslookup "${RESERVATION_MANAGER_FQDN_NO_SCHEMA}" || echo "nslookup failed for ${RESERVATION_MANAGER_FQDN_NO_SCHEMA}"

echo "Attempting curl -v to ${RESERVATION_MANAGER_URL}/car-inventory (example path)"
curl -v "${RESERVATION_MANAGER_URL}/car-inventory" || echo "curl -v failed for ${RESERVATION_MANAGER_URL}/car-inventory"

# Replace environment variables in NGINX configuration
envsubst '${RESERVATION_MANAGER_URL}' < /etc/nginx/templates/default.conf.template > /etc/nginx/conf.d/default.conf

# Print the substituted Nginx config for debugging
echo "--- Substituted Nginx Config (/etc/nginx/conf.d/default.conf) ---"
cat /etc/nginx/conf.d/default.conf
echo "--- End of Substituted Nginx Config ---"

# Execute the CMD from the Dockerfile
exec "$@"