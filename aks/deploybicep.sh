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
LOCATION="eastus"
CLUSTER_NAME="SagawayAKSCluster"
ACR_NAME="sagawayshared"
ACR_URL="$ACR_NAME.azurecr.io"
NAMESPACE="sagaway"
EMAIL="veregant@live.com"
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