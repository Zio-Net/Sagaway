#!/bin/sh

echo "[INFO] Container starting up..."
echo "[INFO] RESERVATION_MANAGER_FQDN: $RESERVATION_MANAGER_FQDN"

CONFIG_FILE="/etc/nginx/conf.d/default.conf"

if [ ! -f "$CONFIG_FILE" ]; then
  echo "[ERROR] $CONFIG_FILE not found!"
  ls -al /etc/nginx/conf.d/
  exit 1
fi

echo "[INFO] Replacing '__BACKEND_FQDN__' in NGINX config with: $RESERVATION_MANAGER_FQDN"
sed -i "s|__BACKEND_FQDN__|$RESERVATION_MANAGER_FQDN|g" "$CONFIG_FILE"

echo "[INFO] Final NGINX config:"
cat "$CONFIG_FILE"

echo "[INFO] Launching NGINX..."
nginx -g "daemon off;"
