using System.Text.Json;

namespace Demo.TenantMonitor

{
   public class EventItem
   {
        public string id { get; set; }
        public dynamic eventgriddata { get; set; }
        public List<LogItem> logs = new List<LogItem>();
        public string status { get; set;}
        public int ttl = -1;

   }

    public record LogItem
    (
        DateTime Date,
        string log
    );

    public static class Status
    {
        public static string New = "New";
        public static string InProgress = "InProgress";
        public static string Done = "Done";
    }
}