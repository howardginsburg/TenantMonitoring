#Random value to make resources unique.
rand=$((100 + $RANDOM % 1000))

#Variables for resources.
location="eastus"
resourceGroupName="TenantMonitor$rand"
logAnalyticsWorkspace="TenantMonitorWorkspace$rand"
functionAppInsights="TenantMonitorWorkspace$rand"
functionStorageAccountName="tenanttonitorfunc$rand"
functionContainerName="logs"
functionAppName="TenantMonitorFunc$rand"
eventGridName="TenantMonitorEventGrid$rand"
eventGridSystemTopic="TenantEventGridSubscription"
cosmosAccountName="tenantmonitorcosmos$rand"
cosmosDatabaseName="TenantMonitoring"
cosmosCollection="EventLog"
synapseWorkspace="tenantmonitorsynapse$rand"
synapseStorageAccountName="tenantmonitorsynapse$rand"
synapseContainerName="lake"

##Synapse User Name and Password.
synapseSQLAdmin="sqladminuser"
synapseSQLPassword="Password123!"

#Allow the CLI to install any missing extensions
az config set extension.use_dynamic_install=yes_without_prompt

#Enable azure resource providers.
az provider register --namespace 'Microsoft.EventGrid' --wait
az provider register --namespace 'Microsoft.Insights' --wait
az provider register --namespace 'Microsoft.DocumentDB' --wait
az provider register --namespace 'Microsoft.Web' --wait
az provider register --namespace 'Microsoft.Storage' --wait

#Get the current subscription id
subscription=$(az account show --query "id" -o tsv)

# Create a Resource Group
az group create --location $location --resource-group $resourceGroupName

# Create a log analytics workspace.
az monitor log-analytics workspace create -g $resourceGroupName -n $logAnalyticsWorkspace
logAnalyticsId=$(az monitor log-analytics workspace show -g $resourceGroupName -n $logAnalyticsWorkspace -o tsv --query id)

# Create an app insights instance within the log analytics workspace.
az monitor app-insights component create --app $functionAppInsights --location $location --kind web -g $resourceGroupName --application-type web --workspace $logAnalyticsId

# Create a storage account and log container for the function app.
az storage account create -n $functionStorageAccountName -g $resourceGroupName -l $location --sku "Standard_LRS"
functionStorageAccountKey=$(az storage account keys list -g $resourceGroupName -n $functionStorageAccountName --query "[0].value" -o tsv )
az storage container create -n $functionContainerName -g $resourceGroupName --account-name $functionStorageAccountName --account-key $functionStorageAccountKey

# Create a cosmos db account, database, and container and with ttl enabled on both and the analytical store enabled.
az cosmosdb create --name $cosmosAccountName --resource-group $resourceGroupName --enable-analytical-storage true
az cosmosdb sql database create --account-name $cosmosAccountName --name $cosmosDatabaseName --resource-group $resourceGroupName
az cosmosdb sql container create --name $cosmosCollection --database-name $cosmosDatabaseName --account-name $cosmosAccountName --resource-group $resourceGroupName --partition-key-path '/id' --throughput 400 --ttl -1 --analytical-storage-ttl -1

# Get the cosmos account key and connection string.
cosmosAccountKey=$(az cosmosdb keys list --name $cosmosAccountName --resource-group $resourceGroupName --type keys --query "primaryMasterKey" -o tsv)
cosmosConnectionString=$(az cosmosdb keys list --name $cosmosAccountName --resource-group $resourceGroupName --type connection-strings --query "connectionStrings[0].connectionString" -o tsv)

# Create the function app, set the linux function version to 7.0 since our functions are written for .Net 7, and set the config values.
az functionapp create --consumption-plan-location $location --name $functionAppName --os-type Linux --resource-group $resourceGroupName --runtime dotnet-isolated --runtime-version 7 --storage-account $functionStorageAccountName --app-insights $functionAppInsights --functions-version 4
az functionapp config set --name $functionAppName --resource-group $resourceGroupName --linux-fx-version "DOTNET-ISOLATED|7.0"
az functionapp config appsettings set --name $functionAppName --resource-group $resourceGroupName --settings "CosmosConnection=$cosmosConnectionString" "CosmosDatabase=$cosmosDatabaseName" "CosmosContainer=$cosmosCollection"

wget https://raw.githubusercontent.com/howardginsburg/TenantMonitoring/main/tenantmonitoringfunctions.zip
az functionapp deployment source config-zip --resource-group $resourceGroupName -n $functionAppName --src tenantmonitoringfunctions.zip
rm tenantmonitoringfunctions.zip

# Get the resource ID of the function.
functionId=$(az functionapp show --name $functionAppName --resource-group $resourceGroupName -o tsv --query id)

# Create an event grid system topic to the subscription and then a event subscription to the topic to trigger the EventListener function.
az eventgrid system-topic create --name $eventGridName --resource-group $resourceGroupName --source /subscriptions/$subscription --topic-type Microsoft.Resources.Subscriptions --location global
az eventgrid system-topic event-subscription create --name $eventGridSystemTopic --resource-group $resourceGroupName --system-topic-name $eventGridName --endpoint-type azurefunction --event-delivery-schema eventgridschema --endpoint $functionId/functions/SubscriptionListener

# Create a storage account and container for synapse.  Enable the hierarchical file system.
az storage account create -n $synapseStorageAccountName -g $resourceGroupName -l $location --sku "Standard_LRS" --enable-hierarchical-namespace true
synapseStorageAccountKey=$(az storage account keys list -g $resourceGroupName -n $synapseStorageAccountName --query "[0].value" -o tsv )
az storage container create -n $synapseContainerName -g $resourceGroupName --account-name $synapseStorageAccountName --account-key $synapseStorageAccountKey

# Create a synapse workspace and allow access from anywhere.
az synapse workspace create --name $synapseWorkspace --resource-group $resourceGroupName --storage-account $synapseStorageAccountName --file-system $synapseContainerName --sql-admin-login-user $synapseSQLAdmin --sql-admin-login-password $synapseSQLPassword
az synapse workspace firewall-rule create --name allowAll --workspace-name $synapseWorkspace --resource-group $resourceGroupName --start-ip-address 0.0.0.0 --end-ip-address 255.255.255.255

# Create a sql credential for the cosmos db key.
sqlcmd -S $synapseWorkspace-ondemand.sql.azuresynapse.net -d master -U $synapseSQLAdmin -P $synapseSQLPassword -Q "CREATE CREDENTIAL [tenantmonitoring] WITH IDENTITY = 'SHARED ACCESS SIGNATURE', SECRET = '$cosmosAccountKey'"
# Create a database to hold the views.
sqlcmd -S $synapseWorkspace-ondemand.sql.azuresynapse.net -d master -U $synapseSQLAdmin -P $synapseSQLPassword -Q "CREATE DATABASE [tenantmonitoringdb]"
# Run the script to create the views.
wget https://raw.githubusercontent.com/howardginsburg/TenantMonitoring/main/createviews.sql
sqlcmd -S $synapseWorkspace-ondemand.sql.azuresynapse.net -d tenantmonitoringdb -U $synapseSQLAdmin -P $synapseSQLPassword -v cosmosAccountName="$cosmosAccountName" -i createviews.sql
rm createviews.sql

#Close the firewall and remove sql uid/pwd access.
az synapse workspace firewall-rule delete --name allowAll --workspace-name $synapseWorkspace --resource-group $resourceGroupName
az synapse ad-only-auth enable --resource-group  $resourceGroupName --workspace-name $synapseWorkspace

