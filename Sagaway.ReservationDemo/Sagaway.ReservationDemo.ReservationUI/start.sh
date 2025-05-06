#!/bin/sh

# Replace the placeholder with the actual FQDN provided via env variable
sed -i "s|__BACKEND_FQDN__|$RESERVATION_MANAGER_FQDN|g" /etc/nginx/conf.d/default.conf

# Launch nginx
nginx -g "daemon off;"
