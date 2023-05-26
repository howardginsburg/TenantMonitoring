# Azure Tenant Monitoring

The purpose of this repo is to serve as a reference implementation of how to do Azure Tenant/Subscription monitoring and create a framework to remediate resources when Azure Policy is not enough.

## Architecture

![Architecture](images/Tenant%20Monitoring%20Architecture.png)

1. Each Azure Subscription will get an Event Grid Subscription.
2. The Subscription Listener will query Azure App Config to see if there is a jobs configuration that needs to be run.  If one exists, a document is created in cosmos db that contains the event data, the job configuration, and log data.
3. Cosmos ChangeFeed will be used to process events and add tracking info back to the document.  Durable Functions can will be used for complex tasks.
4. Once a task is complete, the TTL on the document will be set so that it gets deleted in order to keep the active tasks in the container small.
5. SynapseLink will be used to capture historical data into Cosmos Analytic Store(s).
6. Power BI can connect and run serverless queries to get the data.

## 1. Deploy Resources

The deployment script will create the following assets in Azure:

- Log Analytics Workspace
- Application Insights
- Cosmos DB
  - EventLog collection with 400 ru's
  - Leases collection with 400 ru's
- Storage Accounts
  - Two storage accounts are created.  One is used by the Azure Fuction.  The other has the hierarchical file system enabled and is required to create an Azure Synapse resource.  It is not used in this implementation.
- Azure App Config
  - Stores job configuration data that the SubscriptionListener reads in.
- Azure Function (C# .Net 7)
  - SubscriptionListener - triggered by EventGrid
  - EventHandler - triggered by Cosmos DB changefeed
- Azure Synapse Analytics
  - Creates the connection and views against the Cosmos DB Analytical Store and uses serverless sql queries to view historical changes.

1. From an Azure Cloud Shell bash session

    - `curl https://raw.githubusercontent.com/howardginsburg/TenantMonitoring/master/deploy.sh | bash`

    Note: The script contains an initial sql user and password which is used to easily create the configuration and views against the cosmos analytical store.  They are removed after this has completed.

## 2. Explore The Solution

### Event Grid

1. Select the Event Grid System Topic resource.  The example does not have any filters on the subscription to Event Grid.  You will already see in the metrics that messages are being received.
2. Select the Event Subscription. See that it is configured to route messages to the SubscriptionListener function.

### Azure App Config

1. Select the Azure App Config resource.  Explore the configuration that is stored there.

### Azure Functions

1. Select the Azure Function resource.  Explore both functions and monitoring to see the log information.

### Cosmos DB

1. Select the Cosmos DB resource.  Explore a document to see that it contains the entire payload from the Event Grid message as well as historical logs from processing.

### Azure Synapse Analytics

1. The [createviews.sql](/createviews.sql) script creates a serverless database with two views.
2. Select the Synapse Analytics resource.
3. Open Synapse Studio.
4. Select the develop icon.
5. Select the + icon to create a new SQL Script.
6. Run the following queries to explore the data.

```sql

use tenantmonitoringdb;

--Select top 10 from EventData
select top 10 * from dbo.EventData;

--Select top 10 from EventLogs
select top 10 * from dbo.EventLogs;

--Join the eventdata with the eventlogs
select * from dbo.EventData
inner join dbo.EventLogs
on EventData.id = EventLogs.id
where dbo.EventData.id='<Insert an Event Id from previous query>'
```

## 3. Testing

To test an EventGrid triggered function, you must use the following configuration in your testing tool:

Type: Http Post

Url: `http://localhost:<FunctionPort>/runtime/webhooks/EventGrid?functionName=SubscriptionListener`

Header: aeg-event-type: Notification

Body Configuration: `raw json`

Payload: See [sampleevent.json](/sampleevent.json)

## 4. Next Steps

If you decide to use this in a production scenario, consider making the following changes.  This list is not exhaustive, and you should make whatever changes are relevant to your environment.

1. Add error handling and more robust logging to the functions.
2. Add filtering to the event grid subscription to minimize the events that trigger the functions.
3. Add filtering to the SubscriptionListener function to minimize the events that are stored in Cosmos DB.
4. Update the functions to use a Managed Identity to access Cosmos DB or move the Cosmos connection string to a Key Vault.
5. Create remediation functions that the EventHandler triggers.
6. Add an http api that allows long running remediations to update the document logs which in turn will trigger the EventHandler.
7. Build out a proper CICD pipeline for all of this.
8. Add a timer function that will look for documents that have not been updated in X time period and trigger an alert.