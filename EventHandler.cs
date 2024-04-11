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

            _logger.LogInformation("EventHandler constructor called");

            //Get a handle to the cosmos container we want to write records into.
            string databaseName = Environment.GetEnvironmentVariable("CosmosDatabase");
            string containerName = Environment.GetEnvironmentVariable("CosmosContainer");

            if (string.IsNullOrEmpty(databaseName) || string.IsNullOrEmpty(containerName))
            {
                throw new ArgumentException("CosmosDatabase or CosmosContainer environment variables are not set.");
            }

            _cosmosContainer = CosmosHelper.GetContainer(databaseName, containerName);
        }

        [Function("EventHandler")]
        public async Task Run([CosmosDBTrigger(
            databaseName: "TenantMonitoring",
            containerName: "EventLog",
            Connection = "CosmosConnection",
            LeaseContainerName = "leases",
            CreateLeaseContainerIfNotExists = true)] IReadOnlyList<Object> eventItems)
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

                //If the completed date is set, set the ttl so that it will be removed from the collection.  We don't want to delete
                //it, otherwise, SynapseLink won't get the final version.
                 
                if (item.completedDate != DateTime.MinValue)
                {
                    //Note, after we set the ttl and save it, the document will get picked up by changefeed again.  Thus the check
                    //it's not already set.
                    if (item.ttl != -1)
                    {
                        continue;
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
                    foreach (Job job in item.jobConfig.jobs)
                    {
                        if (job.Status == "Active")
                        {
                            //TODO: Add code to perform remediations here.
                            job.RunDate = DateTime.Now;
                        }
                    }

                    item.completedDate = DateTime.Now;   
                    
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
