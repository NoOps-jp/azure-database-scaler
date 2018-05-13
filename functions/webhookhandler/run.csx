#r "Newtonsoft.Json"
#r "System.Runtime.Serialization"
#load "../common/Define.csx"
#load "../common/Model.csx"

using System.Net;
using Newtonsoft.Json;

private static readonly string _dbRegion = Environment.GetEnvironmentVariable("DB_LOCATION");
private static readonly string _dbResourceGroup = Environment.GetEnvironmentVariable("DB_RESOURCE_GROUP");
private static readonly string _dbName = Environment.GetEnvironmentVariable("DB_NAME");

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, 
                        IAsyncCollector<string> outputQueueItem, TraceWriter log)
{
    log.Info($"WebHook Handler Function was triggered!");

    // Verify ENV Variables
    if ( new List<string> {
            _dbRegion, _dbResourceGroup, _dbName,
            }.Any(i => String.IsNullOrEmpty(i))
        )
    {
        string m = "Please provide values for ENV variables named " +
            "DB_LOCATION, DB_RESOURCE_GROUP, and DB_NAME, ";
        log.Info(m);
        return req.CreateResponse(HttpStatusCode.BadRequest, m);
    }

    // ********************************************************
    // Check Schema and Get Context
    // ********************************************************
    //   JSON Schema for the post from LogicApp
    //         {
    //             "CallbackUrl":"CallbackURL Value",
    //             "Context": "Context Value"
    //         }
    //
    //   Sample Context Value:
    //       "{
    //            \"condition\":
    //                {
    //                    \"metricName\":\"cpu_percent\",
    //                    \"metricUnit\":\"Percent\",
    //                    \"operator\":\"GreaterThan\"
    //                    ...
    //            },
    //            \"resourceName\":\"mysqldemodb001\",
    //            \"resourceRegion\":\"japanwest\",
    //            \"conditionType\":\"Metric\",
    //            \"resourceId\":\"/subscriptions/87c7c7f9-0c9f-47d1-a856-1305a0cbfd7a/resourceGroups/RG-db-demo/providers/Microsoft.DBforMySQL/servers/mysqldemodb001\",
    //            \"resourceGroupName\":\"RG-db-demo\"
    //            ...
    //       }"
    // ********************************************************
    string jsonContent = await req.Content.ReadAsStringAsync();
    log.Info("Request : " + jsonContent);
    dynamic data = JsonConvert.DeserializeObject(jsonContent);
    if (!isRequestedFromLogicApp(data)){
        var m =string.Format("Invalid Request: the webhookHandler currently accept data post only from LogicApp");
        log.Info(m);
        return req.CreateResponse(HttpStatusCode.BadRequest, m);
    } 

    string callbackUrl = data.CallbackUrl;
    dynamic context = JsonConvert.DeserializeObject((string)data.Context);
    // Verify Request Data
    string resourceId = (string) context["resourceId"];
    string resourceRegion = (string) context["resourceRegion"];
    dynamic condition = context["condition"].ToObject<Dictionary<string, string>>();
    string alertMetric  = condition["metricName"];
    string alertOperator  = condition["operator"];
    log.Info(
        string.Format("Input dump - " +
            "callbackUrl:{0}, resourceId:{1}, resourceRegion:{2}, alertMetric:{3}, alertOperator:{4}",
            callbackUrl,resourceId,resourceRegion,alertMetric,alertOperator)
    );

    string[] restokens = resourceId.Split('/');
    if (restokens.Length != 9 ) {
        var m =string.Format("Invalid resource Id: {0}", resourceId);
        log.Info(m);
        return req.CreateResponse(HttpStatusCode.BadRequest, m);
    }
    string dbResourceGroup = restokens[4];
    string resourceProviderNamespace=restokens[6];
    string dbName = restokens[8];
    log.Info(
        string.Format("Input - dbResourceGroup:{0}, resourceProviderNamespace:{1}, dbName:{2}",
            dbResourceGroup,resourceProviderNamespace,dbName)
    );

    // Verify incomming data
    if (!resourceRegion.ToLower().Equals(_dbRegion.ToLower()) ||
        !dbResourceGroup.ToLower().Equals(_dbResourceGroup.ToLower()) ||
        !dbName.ToLower().Equals(_dbName.ToLower()) ||
        (
            !resourceProviderNamespace.Equals(Define.resourceNamespace_MySQL) && 
            !resourceProviderNamespace.Equals(Define.resourceNamespace_PostgreSQL)
        ) || 
        (
            !alertMetric.Equals(Define.metric_CPU_Percent) &&
            !alertMetric.Equals(Define.metric_Storage_Percent) &&
            !alertMetric.Equals(Define.metric_Storage_Used) &&
            !alertMetric.Equals(Define.metric_Storage_Limit)
        )
    )
    {
        string m = string.Format("Invalid incoming data: " +
                "dbRegion:{0}, dbResourceGroup:{1}, dbName:{2}, resourceNamespace:{3}, metric:{4}",
                resourceRegion, dbResourceGroup, dbName,resourceProviderNamespace,alertMetric);
        log.Info(m);
        return req.CreateResponse(HttpStatusCode.BadRequest, m);
    }

    // Serialize InternalRequest Class
    var internalReq = new InternalRequest
    {   
        CallbackUrl = callbackUrl,
        Region = resourceRegion,
        ResourceGroup = dbResourceGroup,
        ResourceProviderNamespace = resourceProviderNamespace,
        DbName = dbName,
        AlertMetric = alertMetric,
        AlertOperator = alertOperator
    };
    string internalReqJson = JsonConvert.SerializeObject(internalReq);
    log.Info(internalReqJson);

    // Enqueue JSON into Auzre Storage Queue
    await outputQueueItem.AddAsync(internalReqJson);
    // Returning 202 'Accepted' to requester
    return req.CreateResponse(HttpStatusCode.Accepted, "Accepted");
}


public static bool isRequestedFromLogicApp(dynamic data)
{
    return (!string.IsNullOrEmpty((string)data.CallbackUrl) && !string.IsNullOrEmpty((string)data.Context));
}