# Azure Tenant Monitoring

The purpose of this repo is to serve as a reference implementation of how to do Azure Tenant/Subscription monitoring and create a framework to remediate resources when Azure Policy is not enough.

## Architecture

![Architecture](images/Tenant%20Monitoring%20Architecture.png)

1. Each Azure Subscription will get an Event Grid Subscription.  The Event Grid Subscription is configured to route messages to an Http Triggered Azure Function which is defined as a WebHook endpoint in Event Grid.  By using an [Http Triggered Function](https://learn.microsoft.com/azure/event-grid/receive-events) vs an Event Grid Triggered Function, allows for cross tenant configuration.
2. The Subscription Listener will query Azure App Config to see if there is a jobs configuration that needs to be run.  If one exists, a document is created in cosmos db that contains the event data, the job configuration, and log data.  The key is built by taking the value of data.authorization.action from the event grid message, converting it to lowercase, and replacing / with .  Ex: Microsoft.Sql/servers/write becomes microsoft.sql.servers.write.
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
select top 10 * from dbo.JobLogs;

--Join the eventdata with the eventlogs
select * from dbo.EventData
inner join dbo.JobLogs
on EventData.id = JobLogs.id
where dbo.EventData.id='<Insert an Event Id from previous query>'
```

## 3. Testing

To test the initial Event Grid subscription, you must use the following configuration in your testing tool:

- Type: Http Post
- Url: `http://localhost:7071/api/SubscriptionListener`
- Body Configuration: `raw json`
- Body: See [samplesubscription.json](/samplesubscription.json)

To test an Event Grid Message, you must use the following configuration in your testing tool:

- Type: Http Post
- Url: `http://localhost:7071/api/SubscriptionListener`
- Body Configuration: `raw json`
- Body: See [sampleevent.json](/sampleevent.json)

## 4. Next Steps

If you decide to use this in a production scenario, consider making the following changes.  This list is not exhaustive, and you should make whatever changes are relevant to your environment.

1. Add error handling and more robust logging to the functions.
1. Add filtering to the event grid subscription to minimize the events that trigger the functions.
1. Add filtering to the SubscriptionListener function to minimize the events that are stored in Cosmos DB.
1. Update the functions to use a Managed Identity to access Cosmos DB or move the Cosmos connection string to a Key Vault.
1. Create remediation functions that the EventHandler triggers.
1. Add an http api that allows long running remediations to update the document logs which in turn will trigger the EventHandler.
1. Build out a proper CICD pipeline for all of this.
1. Add a timer function that will look for documents that have not been updated in X time period and trigger an alert.
1. You may notice that only a single instance of the EventHandler function gets started.  This is because a single instance of a function binds to a physical partition in Cosmos DB.  If you need to be able to process concurrently to keep up, consider scaling up the ru's in Cosmos in intervals of 10,000 ru's as this will force a physcial partition split.  After the split occurs, you can scale back down to a managable and cost effective amount. 