variable "container_registry" {
  description = "The URL of the container registry."
  type        = string
}

variable "container_registry_username" {
  description = "The admin username for the Azure Container Registry"
  type        = string
  sensitive   = true
}

variable "container_registry_password" {
  description = "The admin password for the Azure Container Registry"
  type        = string
  sensitive   = true
}

variable "cosmos_db_key" {
  description = "The key for the Cosmos DB."
  type        = string
  sensitive   = true
}

variable "service_bus_connection_string" {
  description = "The connection string for the Azure Service Bus."
  type        = string
  sensitive   = true
}

variable "location" {
  description = "Azure region for deployment"
  type        = string
  default     = "WestEurope"
}