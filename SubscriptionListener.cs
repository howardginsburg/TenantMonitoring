// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using System.Dynamic;

namespace Demo.TenantMonitor
{
    public class SubscriptionListener
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        
        private readonly Container _cosmosContainer;
        
        public SubscriptionListener(ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            //Get the logger.
            _logger = loggerFactory.CreateLogger<SubscriptionListener>();
            _configuration = configuration;

            //Get a handle to the cosmos container we want to write records into.
            CosmosClient cosmosClient = new CosmosClient(Environment.GetEnvironmentVariable("CosmosConnection"));
            _cosmosContainer = cosmosClient.GetContainer(Environment.GetEnvironmentVariable("CosmosDatabase"),Environment.GetEnvironmentVariable("CosmosContainer"));
        }

        [Function("SubscriptionListener")]
        public async Task Run([EventGridTrigger] string input)
        {
            //Note: For this sample, the EventGrid data schema will be dynamic, so we cannot easily
            //map that to an object.  Thus, we let the data come in as a string.  Initial testing used System.Text.Json, 
            //but when you passed a JsonNode as the payload to the cosmos client, you get an exception serializing as the cosmos client
            //is using Newtonsoft.  So, I'm using Newtonsoft everywhere!

            _logger.LogInformation($"EventGrid payload: {input}");

            //Turn the json into an expandoobject and assign it to a dynamic variable so that we can easily interogate the json.
            dynamic eventData = JsonConvert.DeserializeObject<ExpandoObject>(input);

            //TODO: add any filtering here if there are eventgrid messages coming across that you don't need to deal with.
            
            //Create a new EventItem to store the lifecycle of things.
            EventItem item = new EventItem();
            item.id = (string)eventData.id; //use the eventgrid id as the cosmos document id.
            item.eventgriddata = eventData; //store the raw eventgrid data.
            item.createdDate = DateTime.Now; //set the created date.

            //Get the configuration data for this event type.  To access App Config json, you use an index pattern.

            //Build the configuration key name we take the action and replace the / with . and make it all lower case.
            string keyName = eventData.data.authorization.action;
            keyName = keyName.ToLower();
            keyName = keyName.Replace("/", ".");

            //See if there is configration data by looking for the id in the json.
            string id = _configuration[keyName + ":id"];
            if (id != null)
            {
                EventJobConfiguration jobConfig = new EventJobConfiguration();
                jobConfig.id = id;
                //Get the job configurations.
                int index = 0;
                string jobName = _configuration[keyName + ":jobs:" + index + ":name"];
                while (jobName != null)
                {
                    Job job = new Job();
                    job.Name = jobName;
                    job.Description = _configuration[keyName + ":jobs:" + index + ":description"];
                    job.Api = _configuration[keyName + ":jobs:" + index + ":api"];
                    job.Status = _configuration[keyName + ":jobs:" + index + ":status"];
                    jobConfig.jobs.Add(job);
                    
                    index++;
                    jobName = _configuration[keyName + ":" + "jobs:" + index + ":name"];
                }
                item.jobConfig = jobConfig;

                //Save our new EventItem to cosmos.
                EventItem createdItem = await _cosmosContainer.UpsertItemAsync<EventItem>(
                    item: item,
                    partitionKey: new PartitionKey(item.id)
                );
            }
        }
    }
}
