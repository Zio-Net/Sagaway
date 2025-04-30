terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
  }

  required_version = ">= 1.0"
}

provider "azurerm" {
  features {}

  # Authentication can be done using environment variables or managed identity
  # Uncomment the following lines if you want to specify client_id, client_secret, and tenant_id
  # client_id     = var.client_id
  # client_secret = var.client_secret
  # tenant_id     = var.tenant_id
}