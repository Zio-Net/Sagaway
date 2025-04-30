# filepath: c:\Users\97252\Documents\GitHub\Sagaway\terraform-sagaway-deployment\outputs.tf

output "signalr_connection_string" {
  description = "The primary connection string for the Azure SignalR Service"
  value       = azurerm_signalr_service.main.primary_connection_string
  sensitive   = true
}

# ... any other outputs you might have ...