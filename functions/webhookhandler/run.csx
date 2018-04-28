#r "Newtonsoft.Json"
#r "System.Runtime.Serialization"

using System.Net;
using Newtonsoft.Json;

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, IAsyncCollector<string> outputQueueItem, TraceWriter log)
{
    log.Info($"HTTPTrigger Function was triggered!");
    string jsonContent = await req.Content.ReadAsStringAsync();
    log.Info("jsonContent : " + jsonContent);
    // Simply Enqueue without input validation
    await outputQueueItem.AddAsync(jsonContent);
    return req.CreateResponse(HttpStatusCode.Accepted, "Accepted");
}
