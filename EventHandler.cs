using System;
using System.Collections.Generic;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Cosmos;

namespace Demo.TenantMonitor
{
    public class EventHandler
    {
        private readonly ILogger _logger;
        private readonly Container _cosmosContainer;

        public EventHandler(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<EventHandler>();

            //Get a handle to the cosmos container we want to write records into.
            CosmosClient cosmosClient = new CosmosClient(Environment.GetEnvironmentVariable("CosmosConnection"));
            _cosmosContainer = cosmosClient.GetContainer(Environment.GetEnvironmentVariable("CosmosDatabase"),Environment.GetEnvironmentVariable("CosmosContainer"));
        }

        [Function("EventHandler")]
        public async Task Run([CosmosDBTrigger(
            databaseName: "TenantMonitoring",
            collectionName: "EventLog",
            ConnectionStringSetting = "CosmosConnection",
            LeaseCollectionName = "leases",
            CreateLeaseCollectionIfNotExists = true)] IReadOnlyList<Object> eventItems)
        {
            if (eventItems == null || eventItems.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Documents modified: " + eventItems.Count);

            foreach (Object cosmosDocument in eventItems)
            {
                string json = System.Text.Json.JsonSerializer.Serialize(cosmosDocument);
                EventItem item = JsonConvert.DeserializeObject<EventItem>(json);
                _logger.LogInformation($"EventItem Id: {item.id}" );

                //If the status is done, set the ttl so that it will be removed from the collection.  We don't want to delete
                //it, otherwise, SynapseLink won't get the final version.
                 
                if (item.status.Equals(Status.Done))
                {
                    //Note, after we set the ttl and save it, the document will get picked up by changefeed again.  Thus the check
                    //it's not already set.
                    if (item.ttl != -1)
                    {
                        return;
                    }

                    //Set the ttl to 60 seconds.
                    item.ttl = 60;

                    //Save our new EventItem to cosmos.
                    EventItem createdItem = await _cosmosContainer.UpsertItemAsync<EventItem>(
                        item: item,
                        partitionKey: new PartitionKey(item.id)
                    );
                }
                //Perform remediations and update the status.
                else
                {
                    //TODO: Do remediation...

                    item.logs.Add(new LogItem(DateTime.Now,"Magic has happened!")); //add a log entry.
                    item.status = Status.Done; //set the status to done.
                    
                    //Save it back to cosmos.
                    EventItem createdItem = await _cosmosContainer.UpsertItemAsync<EventItem>(
                        item: item,
                        partitionKey: new PartitionKey(item.id)
                    );
                }
                

            }
        }
    }
}
