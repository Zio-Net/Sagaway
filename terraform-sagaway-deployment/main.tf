resource "azurerm_resource_group" "main" {
  name     = "sagaway-rg"
  location = var.location
}

resource "azurerm_container_registry" "main" {
  name                = "sagawayregistry"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "Basic"
  admin_enabled       = true
}

resource "azurerm_servicebus_namespace" "main" {
  name                = "sagaway-sb-demo"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "Basic"
}

resource "azurerm_servicebus_queue" "billing" {
  name         = "billing-queue"
  namespace_id = azurerm_servicebus_namespace.main.id
}

resource "azurerm_servicebus_queue" "booking" {
  name         = "booking-queue"
  namespace_id = azurerm_servicebus_namespace.main.id
}

resource "azurerm_servicebus_queue" "inventory" {
  name         = "inventory-queue"
  namespace_id = azurerm_servicebus_namespace.main.id
}

resource "azurerm_servicebus_queue" "reservation_response" {
  name         = "reservation-response-queue"
  namespace_id = azurerm_servicebus_namespace.main.id
}

resource "azurerm_signalr_service" "main" {
  name                = "sagaway-signalr" # Choose a globally unique name
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name

  sku {
    name     = "Free_F1" # Or Standard_S1, etc.
    capacity = 1
  }

  # Optional: Configure CORS if your client app is hosted on a different domain
  # cors {
  #   allowed_origins = ["http://localhost:5173", "https://your-frontend-domain.com"] # Add your frontend origins
  # }

  # Optional: Configure network ACLs if needed
  # public_network_access_enabled = true
  # network_acl {
  #   default_action = "Deny"
  #   public_network {
  #     allowed_request_types = ["ClientConnection", "ServerConnection", "Trace"]
  #   }
  #   private_endpoint {
  #     # Configuration for private endpoints if used
  #   }
  # }
}

resource "azurerm_cosmosdb_account" "main" {
  name                = "sagaway-cosmos"
  resource_group_name = azurerm_resource_group.main.name
  # Override location specifically for Cosmos DB
  location            = "israelcentral"
  offer_type         = "Standard"
  kind               = "GlobalDocumentDB"

  consistency_policy {
    consistency_level = "Session"
  }
  automatic_failover_enabled = false

  geo_location {
    # Ensure the primary geo_location matches the overridden location
    location          = "israelcentral"
    failover_priority = 0
  }

  # Keep the backup block as Israel Central requires it
  backup {
    type                = "Periodic"
    interval_in_minutes = 240
    retention_in_hours  = 8
    storage_redundancy  = "Local"
  }
}

resource "azurerm_cosmosdb_sql_database" "main" {
  name                = "sagaway-db"
  resource_group_name = azurerm_resource_group.main.name
  account_name        = azurerm_cosmosdb_account.main.name
  throughput          = 1000
}

resource "azurerm_cosmosdb_sql_container" "dapr_state" {
  name                  = "dapr-state"
  resource_group_name   = azurerm_resource_group.main.name
  account_name          = azurerm_cosmosdb_account.main.name
  database_name         = azurerm_cosmosdb_sql_database.main.name
  partition_key_paths   = ["/partitionKey"]
}

resource "azurerm_container_app_environment" "main" {
  name                = "sagaway-environment"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location

  # Keep identity block commented out
  # identity {
  #   type = "SystemAssigned"
  # }

  workload_profile {
    name                 = "Consumption"
    workload_profile_type = "Consumption"
  }
}

resource "azurerm_container_app" "reservation_manager" {
  name                         = "reservation-manager"
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  revision_mode                = "Single"

  # Add these blocks back
  secret {
    name  = "registry-password"
    value = var.container_registry_password
  }
  registry {
    server               = azurerm_container_registry.main.login_server
    username             = var.container_registry_username
    password_secret_name = "registry-password"
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"
    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }
  dapr {
    app_id   = "reservation-manager"
    app_port = 8080
  }

  template {
    container {
      name   = "reservation-manager"
      image  = "${azurerm_container_registry.main.login_server}/sagawayreservationdemoreservationmanager:latest"
      cpu    = 0.25
      memory = "0.5Gi"
    }
    min_replicas = 1
    max_replicas = 1
  }
}

resource "azurerm_container_app" "billing_management" {
  name                         = "billing-management"
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  revision_mode                = "Single"

  # Add these blocks back
  secret {
    name  = "registry-password"
    value = var.container_registry_password
  }
  registry {
    server               = azurerm_container_registry.main.login_server
    username             = var.container_registry_username
    password_secret_name = "registry-password"
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"
    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }
  dapr {
    app_id   = "billing-management"
    app_port = 8080
  }

  template {
    container {
      name   = "billing-management"
      image  = "${azurerm_container_registry.main.login_server}/sagawayreservationdemobillingmanagement:latest"
      cpu    = 0.25
      memory = "0.5Gi"
    }
    min_replicas = 1
    max_replicas = 1
  }
}

resource "azurerm_container_app" "inventory_management" {
  name                         = "inventory-management"
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  revision_mode                = "Single"

  # Add these blocks back
  secret {
    name  = "registry-password"
    value = var.container_registry_password
  }
  registry {
    server               = azurerm_container_registry.main.login_server
    username             = var.container_registry_username
    password_secret_name = "registry-password"
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"
    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }
  dapr {
    app_id   = "inventory-management"
    app_port = 8080
  }

  template {
    container {
      name   = "inventory-management"
      image  = "${azurerm_container_registry.main.login_server}/sagawayreservationdemoinventorymanagement:latest"
      cpu    = 0.25
      memory = "0.5Gi"
    }
    min_replicas = 1
    max_replicas = 1
  }
}

resource "azurerm_container_app" "booking_management" {
  name                         = "booking-management"
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id
  revision_mode                = "Single"

  # Add these blocks back
  secret {
    name  = "registry-password"
    value = var.container_registry_password
  }
  registry {
    server               = azurerm_container_registry.main.login_server
    username             = var.container_registry_username
    password_secret_name = "registry-password"
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"
    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }
  dapr {
    app_id   = "booking-management"
    app_port = 8080
  }

  template {
    container {
      name   = "booking-management"
      image  = "${azurerm_container_registry.main.login_server}/sagawayreservationdemobookingmanagement:latest"
      cpu    = 0.25
      memory = "0.5Gi"
    }
    min_replicas = 1
    max_replicas = 1
  }
}

# Dapr State Store Component (Cosmos DB)
resource "azurerm_container_app_environment_dapr_component" "statestore" {
  name                         = "statestore"
  container_app_environment_id = azurerm_container_app_environment.main.id
  component_type               = "state.azure.cosmosdb"
  version                      = "v1"

  metadata {
    name  = "url"
    value = azurerm_cosmosdb_account.main.endpoint
  }
  metadata {
    name  = "database"
    value = azurerm_cosmosdb_sql_database.main.name
  }
  metadata {
    name  = "collection"
    value = azurerm_cosmosdb_sql_container.dapr_state.name
  }
  metadata {
    name = "masterkey"
    secret_name = "cosmos-master-key"
  }
   metadata {
    name  = "actorStateStore"
    value = "true"
  }

  secret {
    name  = "cosmos-master-key"
    value = azurerm_cosmosdb_account.main.primary_key
  }

  scopes = [
    "reservation-manager",
    "billing-management",
    "inventory-management",
    "booking-management"
  ]
}

# Dapr Binding Component (Billing Queue)
resource "azurerm_container_app_environment_dapr_component" "billing_queue_binding" {
  name                         = "billing-queue"
  container_app_environment_id = azurerm_container_app_environment.main.id
  component_type               = "bindings.azure.servicebusqueues"
  version                      = "v1"

  metadata {
    name = "connectionString"
    secret_name = "sb-conn-string"
  }
  metadata {
    name  = "queueName"
    value = azurerm_servicebus_queue.billing.name
  }

  secret {
    name  = "sb-conn-string"
    value = azurerm_servicebus_namespace.main.default_primary_connection_string
  }

  scopes = [
    "billing-management",
    "reservation-manager"
  ]
}

# Dapr Binding Component (Booking Queue)
resource "azurerm_container_app_environment_dapr_component" "booking_queue_binding" {
  name                         = "booking-queue"
  container_app_environment_id = azurerm_container_app_environment.main.id
  component_type               = "bindings.azure.servicebusqueues"
  version                      = "v1"

  metadata {
    name = "connectionString"
    secret_name = "sb-conn-string"
  }
  metadata {
    name  = "queueName"
    value = azurerm_servicebus_queue.booking.name
  }

  secret {
    name  = "sb-conn-string"
    value = azurerm_servicebus_namespace.main.default_primary_connection_string
  }

  scopes = [
    "booking-management",
    "reservation-manager"
  ]
}

# Dapr Binding Component (Inventory Queue)
resource "azurerm_container_app_environment_dapr_component" "inventory_queue_binding" {
  name                         = "inventory-queue"
  container_app_environment_id = azurerm_container_app_environment.main.id
  component_type               = "bindings.azure.servicebusqueues"
  version                      = "v1"

  metadata {
    name = "connectionString"
    secret_name = "sb-conn-string"
  }
  metadata {
    name  = "queueName"
    value = azurerm_servicebus_queue.inventory.name
  }

  secret {
    name  = "sb-conn-string"
    value = azurerm_servicebus_namespace.main.default_primary_connection_string
  }

  scopes = [
    "inventory-management",
    "reservation-manager"
  ]
}

# Dapr Binding Component (Reservation Response Queue)
resource "azurerm_container_app_environment_dapr_component" "reservation_response_queue_binding" {
  name                         = "reservation-response-queue"
  container_app_environment_id = azurerm_container_app_environment.main.id
  component_type               = "bindings.azure.servicebusqueues"
  version                      = "v1"

  metadata {
    name = "connectionString"
    secret_name = "sb-conn-string"
  }
  metadata {
    name  = "queueName"
    value = azurerm_servicebus_queue.reservation_response.name
  }

  secret {
    name  = "sb-conn-string"
    value = azurerm_servicebus_namespace.main.default_primary_connection_string
  }

  scopes = [
    "reservation-manager"
  ]
}


