
echo "Waiting for external IP for reservation-ui service..."

for i in {1..30}; do
  EXTERNAL_IP=$(kubectl get svc reservation-ui -o jsonpath='{.status.loadBalancer.ingress[0].ip}')
  
  if [[ -n "$EXTERNAL_IP" ]]; then
    echo "External IP is ready: $EXTERNAL_IP"
    break
  fi

  echo "Attempt $i: External IP not yet assigned. Waiting 10s..."
  sleep 10
done

if [[ -z "$EXTERNAL_IP" ]]; then
  echo "Failed to get external IP after waiting. Check 'kubectl get svc reservation-ui'"
else
  echo "You can now access the app at: http://$EXTERNAL_IP:8080"
fi