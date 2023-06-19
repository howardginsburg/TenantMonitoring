using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using System.Dynamic;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;

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

        /**
            
        */
        [Function("SubscriptionListener")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequestData req)
        {
            _logger.LogInformation("SubscriptionListener function triggered!");
            string body = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation(body);
            dynamic[] eventDataArray = JsonConvert.DeserializeObject<ExpandoObject[]>(body);
            _logger.LogInformation($"Received {eventDataArray.Length} events");

            foreach (dynamic eventData in eventDataArray)
            {
                if (eventData.eventType == "Microsoft.EventGrid.SubscriptionValidationEvent")
                {
                    string topic = eventData.topic;
                    string validationCode = eventData.data.validationCode;
                    _logger.LogInformation($"Got SubscriptionValidation event data, validation code: {validationCode}, topic: {topic}");
                    
                    // Do any additional validation (as required) and then return back the below response

                    var responseData = new
                    {
                        ValidationResponse = validationCode
                    };

                    HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
                    await response.WriteAsJsonAsync(responseData);
                    return response;
                }
                // Handle all other events
                else
                {
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
            return req.CreateResponse(HttpStatusCode.OK);
        }
    }
}
