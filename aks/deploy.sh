#!/bin/bash

set -e

# Load .env file
if [ -f .env ]; then
  set -a
  source .env
  set +a
else
  echo ".env file not found. Aborting."
  exit 1
fi

# Validate required environment variables
missing_vars=()

for var in AZURE_CLIENT_ID AZURE_CLIENT_SECRET AZURE_TENANT_ID AZURE_SUBSCRIPTION_ID COSMOSDB_MASTERKEY SERVICEBUS_CONNECTION_STRING SIGNALR_CONNECTION_STRING; do
  if [ -z "${!var}" ]; then
    missing_vars+=("$var")
  fi
done

if [ ${#missing_vars[@]} -ne 0 ]; then
  echo "Missing required environment variables: ${missing_vars[*]}"
  exit 1
fi

# CONFIG
RESOURCE_GROUP="SagawayDemoAKS"
LOCATION="westus2"
CLUSTER_NAME="SagawayAKSCluster"
ACR_NAME="SagawayDemoACR"
ACR_URL="$ACR_NAME.azurecr.io"
NAMESPACE="sagaway"
EMAIL="Benny902@gmail.com"
export RESOURCE_GROUP LOCATION CLUSTER_NAME ACR_NAME ACR_URL NAMESPACE EMAIL

# 1. Login using Service Principal
echo "Logging into Azure..."
az login --service-principal \
  --username "$AZURE_CLIENT_ID" \
  --password "$AZURE_CLIENT_SECRET" \
  --tenant "$AZURE_TENANT_ID"

# 1.5 Set subscription explicitly (fix MissingSubscription error)
echo "Setting subscription..."
az account set --subscription "$AZURE_SUBSCRIPTION_ID"

# 2. Create resource group if needed
echo "Creating/verifying resource group..."
az group create --name $RESOURCE_GROUP --location $LOCATION

# 3. Deploy infrastructure (ACR + AKS)
echo "Deploying infrastructure..."
az deployment group create \
  --resource-group $RESOURCE_GROUP \
  --template-file ./IaC/main.bicep  \
  --parameters containerRegistryName="$ACR_NAME"

# 4. Get AKS credentials
echo "Getting AKS credentials..."
az aks get-credentials --resource-group $RESOURCE_GROUP --name $CLUSTER_NAME --overwrite-existing

# 5. Init Dapr on the cluster (only if not already installed)
if ! kubectl get namespace dapr-system > /dev/null 2>&1; then
  echo "Dapr not found, initializing..."
  dapr init -k
else
  echo "Dapr already installed. Skipping dapr init."
fi

# 6. Create namespace if not exists
echo "Setting up Kubernetes namespace..."
kubectl get ns $NAMESPACE || kubectl create ns $NAMESPACE
kubectl config set-context --current --namespace=$NAMESPACE

# 6.5 Create ACR pull secret manually (first delete old if exists)
echo "Creating ACR pull secret..."
kubectl delete secret acr-secret --ignore-not-found

# 6.5 ACR docker-registry pull secret
echo "Creating ACR pull secret..."
kubectl delete secret acr-secret --ignore-not-found
kubectl create secret docker-registry acr-secret \
  --docker-server="${ACR_URL}" \
  --docker-username="${AZURE_CLIENT_ID}" \
  --docker-password="${AZURE_CLIENT_SECRET}" \
  --docker-email="${EMAIL}"

# 6.6 Cloud secrets (CosmosDB)
echo "Creating cosmosdb-secret..."
kubectl delete secret cosmosdb-secret --ignore-not-found
kubectl create secret generic cosmosdb-secret \
  --from-literal=masterKey="$COSMOSDB_MASTERKEY" \

# 6.7 Cloud secrets (Service Bus)
echo "Creating azure-servicebus-secret..."
kubectl delete secret azure-servicebus-secret --ignore-not-found
kubectl create secret generic azure-servicebus-secret \
  --from-literal=connectionString="$SERVICEBUS_CONNECTION_STRING"

  # 6.8 Cloud secrets (SignalR)
echo "Creating signalr-connection-string..."
kubectl delete secret signalr-connection-string --ignore-not-found
kubectl create secret generic signalr-connection-string \
  --from-literal=signalr-connection-string="$SIGNALR_CONNECTION_STRING"

# 7. Move to project root to build docker images
cd ..

# Method 1: Using az acr login with subscription ## removed for now.
# echo "Trying az acr login method..."
# az acr login --name $ACR_NAME --subscription "$AZURE_SUBSCRIPTION_ID"

# Method 2: Enable admin access and use those credentials as fallback
echo "Enabling admin access on ACR as fallback..."
az acr update --name $ACR_NAME --resource-group $RESOURCE_GROUP --subscription "$AZURE_SUBSCRIPTION_ID" --admin-enabled true

# Get admin credentials and use them for Docker login
echo "Getting ACR admin credentials..."
ACR_USERNAME=$(az acr credential show --name $ACR_NAME --resource-group $RESOURCE_GROUP --subscription "$AZURE_SUBSCRIPTION_ID" --query username -o tsv)
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --resource-group $RESOURCE_GROUP --subscription "$AZURE_SUBSCRIPTION_ID" --query "passwords[0].value" -o tsv)

# Log into Docker with admin credentials
echo "Logging into Docker with ACR admin credentials..."
echo "$ACR_PASSWORD" | docker login $ACR_URL -u $ACR_USERNAME --password-stdin


# 8. Build images
echo "Building Docker images..."
docker build -t $ACR_URL/sagawayreservationdemoreservationmanager:latest -f Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationManager/Dockerfile .
docker build -t $ACR_URL/sagawayreservationdemobillingmanagement:latest -f Sagaway.ReservationDemo/Sagaway.ReservationDemo.BillingManagement/Dockerfile .
docker build -t $ACR_URL/sagawayreservationdemoinventorymanagement:latest -f Sagaway.ReservationDemo/Sagaway.ReservationDemo.InventoryManagement/Dockerfile .
docker build -t $ACR_URL/sagawayreservationdemobookingmanagement:latest -f Sagaway.ReservationDemo/Sagaway.ReservationDemo.BookingManagement/Dockerfile .
docker build -t $ACR_URL/sagawayintegrationtestsorchestrationservice:latest -f Sagaway.IntegrationTests/Sagaway.IntegrationTests.OrchestrationService/Dockerfile .
docker build -t $ACR_URL/sagawayintegrationteststestservice:latest -f Sagaway.IntegrationTests/Sagaway.IntegrationTests.TestService/Dockerfile .
docker build -t $ACR_URL/sagawayintegrationteststestsubsagacommunicationservice:latest -f Sagaway.IntegrationTests/Sagaway.IntegrationTests.TestSubSagaCommunicationService/Dockerfile .
docker build -t $ACR_URL/sagawayintegrationtestssteprecordertestservice:latest -f Sagaway.IntegrationTests/Sagaway.IntegrationTests.StepRecorderTestService/Dockerfile .
docker build -t $ACR_URL/sagawayreservationdemoreservationui:latest -f Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationUI/Dockerfile .

# 9 Push images
echo "Pushing Docker images to ACR..."
docker push $ACR_URL/sagawayreservationdemoreservationmanager:latest
docker push $ACR_URL/sagawayreservationdemobillingmanagement:latest
docker push $ACR_URL/sagawayreservationdemoinventorymanagement:latest
docker push $ACR_URL/sagawayreservationdemobookingmanagement:latest
docker push $ACR_URL/sagawayintegrationtestsorchestrationservice:latest
docker push $ACR_URL/sagawayintegrationteststestservice:latest
docker push $ACR_URL/sagawayintegrationteststestsubsagacommunicationservice:latest
docker push $ACR_URL/sagawayintegrationtestssteprecordertestservice:latest
docker push $ACR_URL/sagawayreservationdemoreservationui:latest

# 10. Apply ACR to the YAML Files and Deploy them to AKS
echo "Deploying Kubernetes resources..."
for file in ./aks/dapr/yamls/*.yaml ./aks/dapr/*.yaml ./aks/dapr/components/*.yaml; do
  envsubst < "$file" | kubectl apply -f -
done

echo "Deployment completed successfully!"

# 11. get ip of external ip of reservation manager
./aks/get_ip.sh