# Terraform Sagaway Deployment

This project provides a Terraform configuration for deploying the Sagaway application infrastructure on Azure. It includes resources such as Azure Container Apps, Service Bus, and Cosmos DB, similar to the Bicep deployment.

## Project Structure

- `main.tf`: Contains the main configuration for the Terraform deployment, defining the necessary Azure resources.
- `variables.tf`: Defines the input variables for the Terraform configuration, including parameters like `container_registry`, `container_registry_username`, `container_registry_password`, `cosmos_db_key`, `service_bus_connection_string`, and `location`.
- `provider.tf`: Specifies the provider configuration for Azure, including the required provider version and authentication details.

## Prerequisites

- Terraform installed on your local machine.
- An Azure account with the necessary permissions to create resources.

## Setup Instructions

1. **Clone the repository**:
   ```
   git clone <repository-url>
   cd terraform-sagaway-deployment
   ```

2. **Configure your Azure credentials**:
   Ensure you have the Azure CLI installed and are logged in:
   ```
   az login
   ```

3. **Update the variables**:
   Edit the `variables.tf` file to set your Azure resource parameters, including your container registry details, Cosmos DB key, and Service Bus connection string.

4. **Initialize Terraform**:
   Run the following command to initialize the Terraform configuration:
   ```
   terraform init
   ```

5. **Plan the deployment**:
   Generate an execution plan to see what resources will be created:
   ```
   terraform plan
   ```

6. **Apply the configuration**:
   Deploy the infrastructure by running:
   ```
   terraform apply
   ```

7. **Verify the deployment**:
   Check the Azure portal to ensure all resources have been created successfully.

## Cleanup

To remove all resources created by this Terraform configuration, run:
```
terraform destroy
```

## License

This project is licensed under the MIT License.