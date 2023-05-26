using System.Text.Json;

namespace Demo.TenantMonitor

{
   public class EventItem
   {
        public string id { get; set; }
        public dynamic eventgriddata { get; set; }
        public EventJobConfiguration jobConfig { get; set; }
        public int ttl = -1;
        public DateTime createdDate { get; set; }
        public DateTime completedDate { get; set; }

   }


    public record EventJobConfiguration
    {
        public string id { get; set; }
        public List<Job> jobs = new List<Job>();
    }

    public record Job
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Api { get; set; }
        public string Status { get; set; }
        public DateTime RunDate { get; set; }
    }
}