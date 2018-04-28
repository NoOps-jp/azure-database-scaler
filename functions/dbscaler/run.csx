#r "Newtonsoft.Json"
#r "System.Runtime.Serialization"

//*************************************************************
// Azure Database for MySQL/PostgreSQL Scaling Down/Up Functions
//
// For REST API params, refer to
// https://docs.microsoft.com/en-us/rest/api/mysql/servers/update
//**************************************************************

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using Microsoft.Rest.Azure.Authentication;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.ResourceManager.Models;

//private const string _MySQL_Namespace = "Microsoft.DBforMySQL";
//private const string _PostgreSQL_Namespace = "Microsoft.DBforPostgreSQL";
private const string _apiVersion = "2017-12-01";
private const string _dbTier_basic = "Basic";
private const string _dbTier_generalPurpose = "GeneralPurpose";
private const string _dbTier_memoryOptimized = "MemoryOptimized";
private static int[] _scaleModel_basic = {1,2};
private static int[] _scaleModel_generalPurpose = { 2, 4, 8, 16, 32 };
private static int[] _scaleModel_memoryOptimized = { 2, 4, 8, 16, 32 };

private static readonly string _tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
private static readonly string _clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
private static readonly string _clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
private static readonly string _subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");
private static readonly string _dbAdminUser = Environment.GetEnvironmentVariable("DB_ADMIN_USER");
private static readonly string _dbAdminPassword = Environment.GetEnvironmentVariable("DB_ADMIN_PASSWORD");

private static HttpClient client = new HttpClient();

public static void Run(string myQueueItem, TraceWriter log)
{
    log.Info($"queueTrigger Function was triggerd");
    string jsonContent = myQueueItem;
    dynamic data = JsonConvert.DeserializeObject(jsonContent);
    log.Info("Request : " + jsonContent);
    string callbackUrl = data.CallbackUrl;

    dynamic context = JsonConvert.DeserializeObject((string)data.Context);
    string resourceId = (string) context["resourceId"];
    string resourceRegion = (string) context["resourceRegion"];
    dynamic condition = context["condition"].ToObject<Dictionary<string, string>>();
    string alertOperator  = condition["operator"];
    log.Info("Input - callbackUrl : " + callbackUrl);
    log.Info("Input - resourceId : " + resourceId);
    log.Info("Input - resourceRegion : " + resourceRegion);
    log.Info("Input - alertOperator : " + alertOperator);

    string[] restokens = resourceId.Split('/');
    if (restokens.Length != 9 ) {
        var errmsg=string.Format("Invalid resource Id: {0}", resourceId);
        log.Info(errmsg);
        throw new Exception(errmsg);        
    }

    string dbResourceGroup = restokens[4];
    string resourceProviderNamespace=restokens[6];
    string dbName = restokens[8];
    log.Info(string.Format("Input - dbResourceGroup:{0} resourceProviderNamespace:{1} dbName:{2}",
            dbResourceGroup,
            resourceProviderNamespace,
            dbName ));

    if (new List<string> {
            _tenantId,
            _clientId,
            _clientSecret,
            _subscriptionId,
            _dbAdminUser,
            _dbAdminPassword
            }.Any(i => String.IsNullOrEmpty(i)))
    {
        string errmsg="[ERROR] Please provide ENV vars for AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_SECRET, AZURE_SUBSCRIPTION_ID, DB_ADMIN_USER, and DB_ADMIN_PASSWORD";
        Console.WriteLine(errmsg);
        throw new Exception(errmsg);
    }

    try {
        // Db vCore Scale Change
        ScaleDatabaeVCoreCapacity(
            resourceRegion,
            dbResourceGroup,
            resourceProviderNamespace,
            dbName,
            alertOperator,
            log
            ).Wait();
    }
    catch (Exception ex)
    {
        log.Info("[Exception] " + ex);
        throw new Exception("[Exception] " + ex);
    }
    
    string result = jsonContent;
    client.PostAsJsonAsync<string>(callbackUrl, result);
}


public static async Task ScaleDatabaeVCoreCapacity(
            string region, 
            string resourceGroupName, 
            string resourceProviderNamespace,
            string resourceName,
            string alertOperator,
            TraceWriter log
        )
{

    // Build the service credentials and Azure Resource Manager clients
    var serviceCreds = 
        await ApplicationTokenProvider.LoginSilentAsync(_tenantId, _clientId, _clientSecret);
    var resourceClient = 
        new ResourceManagementClient(serviceCreds);
    resourceClient.SubscriptionId = _subscriptionId;

    //***************************************
    // Get current SKU & Properties
    //***************************************
    GenericResource gr;
    gr = resourceClient.Resources.Get(
            resourceGroupName,
            resourceProviderNamespace,   // Microsoft.DBforMySQL or Microsoft.DBforPostgreSQL
            "",                          // fixed
            "servers",                   // fixed "servers"              
            resourceName,                // db account name              
            _apiVersion                  // API version fixed
        );
    Sku cursku = gr.Sku;
    log.Info( string.Format("Current SKU: name:{0}, Tier:{1}, Family:{2}, Capacity:{3}",
             cursku.Name, cursku.Tier, cursku.Family, cursku.Capacity));

    JObject curProperties_jobject = (JObject)gr.Properties;
    Dictionary<string, object> newProperties = curProperties_jobject.ToObject<Dictionary<string, object>>();
    Sku newsku = cursku; 
    
    //***************************************
    // Decide Capacity
    //***************************************
    int[] scaleModel;
    string tierAcronym; 
    switch (cursku.Tier)
    {
        case _dbTier_basic:
            log.Info("Basic Tier");
            scaleModel = _scaleModel_basic;
            tierAcronym = "B";
            break;
        case _dbTier_generalPurpose:
            log.Info("GeneralPurpose Tier");
            scaleModel = _scaleModel_generalPurpose;
            tierAcronym = "GP";
            break;
        case _dbTier_memoryOptimized:
            log.Info("MemoryOptimized Tier");
            scaleModel = _scaleModel_memoryOptimized;
            tierAcronym = "MO";
            break;
        default:
            log.Info("[Warning] Unknown Tier, then skip the rest of operation");
            return;
    }

    int direction = 0;
    if ( alertOperator.Equals("GreaterThan") || alertOperator.Equals("GreaterThanOrEqual"))
    {
        direction = 1;
    } else if (alertOperator.Equals("LessThan") || alertOperator.Equals("LessThanOrEqual") )
    {
        direction = -1;
    }
    int curIndex = 0;
    int newCapacity = (int)cursku.Capacity;  // by default
    foreach (int c in scaleModel)
    {
        if (c == (int)cursku.Capacity)
        {
            if (direction > 0 && (scaleModel.Length - 1) > curIndex)
            {
                newCapacity = scaleModel[curIndex + 1];
                break;
            }
            if (direction < 0 && 0 < curIndex)
            {
                newCapacity = scaleModel[curIndex - 1];
                break;
            }
        }
        curIndex++;
    }

    //***************************************
    // Update Sku
    //***************************************
    if (newCapacity == (int)cursku.Capacity)
    {
        log.Info(
            string.Format(
            "Skip Database vCore Update as it has reached max or min number: Name: {0}",
             resourceName) );
        return;
    }
    newsku.Capacity = newCapacity;
    newsku.Name = string.Format("{0}_{1}_{2}", tierAcronym, newsku.Family, newCapacity);
    log.Info(
        string.Format("New SKU: name:{0}, Tier:{1}, Family:{2}, Capacity:{3}",
        newsku.Name, newsku.Tier, newsku.Family, newsku.Capacity));

    // AdminLoginPassword need to be added anytime of updating db resource
    // since it isn't loaded in properties object
    if (!newProperties.ContainsKey("administratorLoginPassword"))
    {
        newProperties.Add("administratorLoginPassword", _dbAdminPassword);
        newProperties["administratorLogin"] = _dbAdminUser;  //Set dbAdminuser together with the password for its consistency  
    }
    var databaseParams = new GenericResource
    {
        Location = region,
        Sku = newsku,
        Properties = newProperties
    };
    var database = resourceClient.Resources.CreateOrUpdate(
        resourceGroupName,
        resourceProviderNamespace,   // Microsoft.DBforMySQL or Microsoft.DBforPostgreSQL
        "",                          // fixed        
        "servers",                   // fixed            
        resourceName,                // db account name              
        _apiVersion,                 // API Version fixed 
        databaseParams );            // parameters                

    log.Info(
        string.Format("Database vCore scaling completed successfully: Name: {0} and Id: {1}",
        database.Name, database.Id));

}
