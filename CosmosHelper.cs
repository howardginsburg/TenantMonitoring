using Microsoft.Azure.Cosmos;

namespace Demo.TenantMonitor
{
   public class CosmosHelper
   {
       private static Lazy<CosmosClient> lazyClient = new Lazy<CosmosClient>(InitializeCosmosClient);
       private static CosmosClient cosmosClient => lazyClient.Value;
       private static CosmosClient InitializeCosmosClient()
       {
            return new CosmosClient(Environment.GetEnvironmentVariable("CosmosConnection"));
       }

       public static Container GetContainer(string databaseName, string containerName)
       {
           return cosmosClient.GetContainer(databaseName, containerName);
       }
   }
}