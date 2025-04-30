<#
.SYNOPSIS
Builds, tags, and pushes Docker images for the Sagaway demo applications to Azure Container Registry.

.DESCRIPTION
This script automates the following steps:
1. Logs in to the specified Azure Container Registry using Azure CLI.
2. Builds the Docker image for each service using its Dockerfile.
3. Tags the built image with the ACR login server name.
4. Pushes the tagged image to the ACR.

.PARAMETER AcrName
The name of the Azure Container Registry (e.g., "sagawayregistry").

.EXAMPLE
.\push_images.ps1 -AcrName "sagawayregistrynew"

.NOTES
- Ensure you are logged in to Azure CLI (`az login`) before running.
- Ensure Docker Desktop is running.
- Run this script from the root directory of the Sagaway project.
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$AcrName
)

# Construct ACR login server name
$acrLoginServer = "${AcrName}.azurecr.io"

# Define services and their paths/names
$services = @(
    @{ Name = "sagawayreservationdemoreservationmanager"; Path = "Sagaway.ReservationDemo/Sagaway.ReservationDemo.ReservationManager/Dockerfile" },
    @{ Name = "sagawayreservationdemobillingmanagement"; Path = "Sagaway.ReservationDemo/Sagaway.ReservationDemo.BillingManagement/Dockerfile" },
    @{ Name = "sagawayreservationdemoinventorymanagement"; Path = "Sagaway.ReservationDemo/Sagaway.ReservationDemo.InventoryManagement/Dockerfile" },
    @{ Name = "sagawayreservationdemobookingmanagement"; Path = "Sagaway.ReservationDemo/Sagaway.ReservationDemo.BookingManagement/Dockerfile" }
)

# # Add Redis image to pull and push
# $externalImages = @(
#     @{ Name = "redis-stack-server"; SourceImage = "redis/redis-stack-server:latest" }
# )

# --- Step 1: Login to ACR ---
Write-Host "Logging in to ACR: $acrLoginServer..."
az acr login --name $AcrName
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to login to ACR. Please check Azure CLI login status and ACR name."
    exit 1
}
Write-Host "Successfully logged in to ACR."

# --- Step 2 & 3: Build, Tag, and Push Images ---
foreach ($service in $services) {
    $imageName = $service.Name
    $dockerfilePath = $service.Path
    $localTag = "${imageName}:latest"
    $acrTag = "${acrLoginServer}/${imageName}:latest"

    Write-Host "Processing service: $imageName"

    # Build
    Write-Host "Building $localTag from $dockerfilePath..."
    docker build -t $localTag -f $dockerfilePath . # Assuming script is run from project root
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to build $localTag."
        continue # Skip to next service
    }

    # Tag
    Write-Host "Tagging $localTag as $acrTag..."
    docker tag $localTag $acrTag
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to tag $localTag."
        continue # Skip to next service
    }

    # Push
    Write-Host "Pushing $acrTag..."
    docker push $acrTag
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to push $acrTag."
        continue # Skip to next service
    }

    Write-Host "Successfully pushed $acrTag."
    Write-Host "---"
}

# --- Step 4: Pull and Push External Images ---
# foreach ($externalImage in $externalImages) {
#     $imageName = $externalImage.Name
#     $sourceImage = $externalImage.SourceImage
#     $acrTag = "${acrLoginServer}/${imageName}:latest"

#     Write-Host "Processing external image: $imageName"

#     # Pull
#     Write-Host "Pulling $sourceImage..."
#     docker pull $sourceImage
#     if ($LASTEXITCODE -ne 0) {
#         Write-Error "Failed to pull $sourceImage."
#         continue # Skip to next external image
#     }

#     # Tag
#     Write-Host "Tagging $sourceImage as $acrTag..."
#     docker tag $sourceImage $acrTag
#     if ($LASTEXITCODE -ne 0) {
#         Write-Error "Failed to tag $sourceImage."
#         continue # Skip to next external image
#     }

#     # Push
#     Write-Host "Pushing $acrTag..."
#     docker push $acrTag
#     if ($LASTEXITCODE -ne 0) {
#         Write-Error "Failed to push $acrTag."
#         continue # Skip to next external image
#     }

#     Write-Host "Successfully pushed $acrTag."
#     Write-Host "---"
# }

Write-Host "Image push process completed."