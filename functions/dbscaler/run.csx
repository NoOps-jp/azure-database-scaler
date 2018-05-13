#r "Newtonsoft.Json"
#r "System.Runtime.Serialization"
#load "../common/Define.csx"
#load "../common/Model.csx"

//*************************************************************
// Azure Database for MySQL/PostgreSQL Scaling Down/Up Functions
//
// For REST API params, refer to
// https://docs.microsoft.com/en-us/rest/api/mysql/servers/update
// For Monitoring Metrics, refer to
// https://docs.microsoft.com/en-us/azure/monitoring-and-diagnostics/monitoring-supported-metrics#microsoftdbformysqlservers
// https://docs.microsoft.com/en-us/azure/monitoring-and-diagnostics/monitoring-supported-metrics#microsoftdbforpostgresqlservers
//**************************************************************

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using Microsoft.Rest.Azure.Authentication;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.ResourceManager.Models;

private static readonly string _tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
private static readonly string _clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
private static readonly string _clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
private static readonly string _subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");
private static readonly string _dbRegion = Environment.GetEnvironmentVariable("DB_LOCATION");
private static readonly string _dbResourceGroup = Environment.GetEnvironmentVariable("DB_RESOURCE_GROUP");
private static readonly string _dbName = Environment.GetEnvironmentVariable("DB_NAME");
private static readonly string _dbAdminUser = Environment.GetEnvironmentVariable("DB_ADMIN_USER");
private static readonly string _dbAdminPassword = Environment.GetEnvironmentVariable("DB_ADMIN_PASSWORD");
private static int _maxStorageScaleLimit = int.Parse(Environment.GetEnvironmentVariable("MAX_STORAGE_SCALE_LIMIT"));  //GB
private static int _storageScaleupSize = int.Parse(Environment.GetEnvironmentVariable("STORAGE_SCALEUP_SIZE"));       //GB

private static HttpClient client = new HttpClient();

public static void Run(string myQueueItem, TraceWriter log)
{
    log.Info($"queueTrigger Function was triggerd");
    string jsonContent = myQueueItem;
    log.Info("Request : " + jsonContent);
    InternalRequest req = JsonConvert.DeserializeObject<InternalRequest>(jsonContent);

    // Verify Request Data
    // Checking all members in InternalRequest instance except CallbackUrl (which is optional)
    if ( new List<string> {
            req.Region, req.AlertMetric, req.AlertOperator,
             req.ResourceGroup, req.ResourceProviderNamespace, req.DbName
            }.Any(i => String.IsNullOrEmpty(i))
        )
    {
        string m = "Invalid Request Data: " + jsonContent;
        log.Info(m);
        throw new Exception(m);
    }

    // Verify ENV Variables
    if ( new List<string> {
            _tenantId,_clientId, _clientSecret, _subscriptionId,
            _dbRegion, _dbResourceGroup, _dbName,
            _dbAdminUser,_dbAdminPassword
            }.Any(i => String.IsNullOrEmpty(i)) ||
        _maxStorageScaleLimit == 0 ||
        _storageScaleupSize == 0
        )
    {
        string m = "Please provide values for ENV variables named " +
            "AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_SECRET, AZURE_SUBSCRIPTION_ID, " +
            "DB_LOCATION, DB_RESOURCE_GROUP, and DB_NAME, " +
            "DB_ADMIN_USER, DB_ADMIN_PASSWORD, " +
            "MAX_STORAGE_SCALE_LIMIT, STORAGE_SCALEUP_SIZE";
        log.Info(m);
        throw new Exception(m);
    }

    //***************************************
    // Try Scale Change 
    //***************************************
    try {
        ProcessScaling(
            req.AlertMetric, 
            req.AlertOperator, 
            req.ResourceProviderNamespace,
            log
        ).Wait();
    }
    catch (Exception ex)
    {
        log.Info("Scale Change Failure: " + ex);
        throw new Exception("Scale Change Failure: " + ex);
    }

    //***************************************
    // Post to CallbackUrl only if CallbackUrl exists
    //***************************************
    string result = jsonContent;
    if (!String.IsNullOrEmpty(req.CallbackUrl) ) 
    {
        client.PostAsJsonAsync<string>(req.CallbackUrl, result);
    }
}


public static async Task ProcessScaling(
    string alertMetric, string alertOperator, string resourceProviderNamespace,TraceWriter log )
{
    
    // Build the service credentials and Azure Resource Manager clients
    var serviceCreds = await ApplicationTokenProvider.LoginSilentAsync(_tenantId, _clientId, _clientSecret);
    var rmClient = new ResourceManagementClient(serviceCreds);
    rmClient.SubscriptionId = _subscriptionId;

    GenericResource gr = rmClient.Resources.Get(
            _dbResourceGroup,
            resourceProviderNamespace,   // Microsoft.DBforMySQL or Microsoft.DBforPostgreSQL
            "",                          // fixed
            "servers",                   // fixed "servers"
            _dbName,                     // db account name
            Define.ApiVersion            // API version fixed
        );

    if (alertMetric.Equals(Define.metric_CPU_Percent))
    {
        // Db vCore Scale Change
        ScaleDatabaeVCoreCapacity(
            _dbRegion,
            _dbResourceGroup,
            resourceProviderNamespace,
            _dbName,
            alertOperator,
            gr,
            rmClient,
            log
            );

    }
    else if (alertMetric.Equals(Define.metric_Storage_Percent) ||
        alertMetric.Equals(Define.metric_Storage_Used) ||
        alertMetric.Equals(Define.metric_Storage_Limit))
    {
       // Storage Scale Change
        ScaleDatabaeStorageCapacity(
            _dbRegion,
            _dbResourceGroup,
            resourceProviderNamespace,
            _dbName,
            alertOperator,
            gr,
            rmClient,
            log
            );
    }
}


public static void ScaleDatabaeVCoreCapacity(
            string region,
            string resourceGroupName,
            string resourceProviderNamespace,
            string resourceName,
            string alertOperator,
            GenericResource gr,
            ResourceManagementClient rmClient,
            TraceWriter log
        )
{

    Sku cursku = gr.Sku;
    JObject curProperties_jobject = (JObject)gr.Properties;
    Dictionary<string, object> curProperties = curProperties_jobject.ToObject<Dictionary<string, object>>();
    JObject curStoragePrifle_jobject = (JObject)curProperties["storageProfile"];
    Dictionary<string, string> curStorageProfile = curStoragePrifle_jobject.ToObject<Dictionary<string, string>>();

    //***************************************
    // Calculate New Capacity
    //***************************************
    int[] scaleModel;
    string tierAcronym;
    switch (cursku.Tier)
    {
        case Define.dbTier_basic:
            scaleModel = Define.vcore_scaleModel_basic;
            tierAcronym = "B";
            break;
        case Define.dbTier_generalPurpose:
            scaleModel = Define.vcore_scaleModel_generalPurpose;
            tierAcronym = "GP";
            break;
        case Define.dbTier_memoryOptimized:
            scaleModel = Define.vcore_scaleModel_memoryOptimized;
            tierAcronym = "MO";
            break;
        default:
            string m = string.Format("Unknown tier: {0}", cursku.Tier);
            log.Info(m);
            throw new Exception(m);
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
    // Update Sku & Properties
    //***************************************
    log.Info(
        string.Format("Current SKU & Properties: " +
        "name:{0}, Tier:{1}, Family:{2}, Capacity:{3}, StorageMB:{4}",
        gr.Sku.Name, gr.Sku.Tier, gr.Sku.Family, gr.Sku.Capacity, curStorageProfile["storageMB"]));
    if (newCapacity == (int)cursku.Capacity)
    {
        string m = string.Format(
            "Cannot scale Database vCore anymore as it has reached max or min number: Name: {0}",
             resourceName);
        log.Info(m);
        throw new Exception(m);
    }
    Sku newsku = cursku;
    newsku.Capacity = newCapacity;
    newsku.Name = string.Format("{0}_{1}_{2}", tierAcronym, newsku.Family, newCapacity);
    log.Info(string.Format("New SKU & Properties: " +
        "name:{0}, Tier:{1}, Family:{2}, Capacity:{3}, StorageMB:{4}",
        newsku.Name, newsku.Tier, newsku.Family, newsku.Capacity, curStorageProfile["storageMB"]));

    // AdminLoginPassword need to be added anytime of updating db resource as it isn't loaded in properties object
    if (!curProperties.ContainsKey("administratorLoginPassword"))
    {
        curProperties.Add("administratorLoginPassword", _dbAdminPassword);
        curProperties["administratorLogin"] = _dbAdminUser;  // Set dbAdminuser together with the password for its consistency
    }

    var databaseParams = new GenericResource
    {
        Location = region,
        Sku = newsku,
        Properties = curProperties
    };
    var database = rmClient.Resources.CreateOrUpdate(
        resourceGroupName,
        resourceProviderNamespace,   // Microsoft.DBforMySQL or Microsoft.DBforPostgreSQL
        "",                          // fixed
        "servers",                   // fixed
        resourceName,                // db account name
        Define.ApiVersion,           // API Version fixed
        databaseParams );            // parameters

    log.Info(string.Format("Database vCore scaling completed successfully: Name: {0} and Id: {1}",
        database.Name, database.Id));
}            


public static void ScaleDatabaeStorageCapacity(
    string region,
    string resourceGroupName,
    string resourceProviderNamespace,
    string resourceName,
    string alertOperator,
    GenericResource gr,
    ResourceManagementClient rmClient,
    TraceWriter log
)
{

    Sku cursku = gr.Sku;
    JObject curProperties_jobject = (JObject)gr.Properties;
    Dictionary<string, object> curProperties = curProperties_jobject.ToObject<Dictionary<string, object>>();
    JObject curStoragePrifle_jobject = (JObject)curProperties["storageProfile"];
    Dictionary<string, string> curStorageProfile = curStoragePrifle_jobject.ToObject<Dictionary<string, string>>();

    //***************************************
    // Calculate New Capacity
    //***************************************
    int[] storageRange;
        switch (cursku.Tier)
    {
        case Define.dbTier_basic:
            storageRange = Define.storageRange_basic;
            break;
        case Define.dbTier_generalPurpose:
            storageRange = Define.storageRange_generalPurpose;
            break;
        case Define.dbTier_memoryOptimized:
            storageRange = Define.storageRange_memoryOptimized;
            break;
        default:
            string m = string.Format("Unknown tier: {0}", cursku.Tier);
            log.Info(m);
            throw new Exception(m);
    }
    // Check if _maxStorageScaleLimit isn't bigger than Max storage size. If it's bigger, assign max storage size to it
    int maxStorageScaleLimit = ( _maxStorageScaleLimit < storageRange[1] ) ? 
            _maxStorageScaleLimit : 
            storageRange[1];
    int maxStorageScaleLimit_MB = maxStorageScaleLimit * 1024;
    int storageScaleupSize_MB = _storageScaleupSize * 1024;
    if (!curStorageProfile.ContainsKey("storageMB"))
    {
        curStorageProfile["storageMB"] = (storageRange[0] * 1024).ToString(); // minimum storage size
    }
    int curCapacity = int.Parse(curStorageProfile["storageMB"]);
    int newCapacity = ((curCapacity + storageScaleupSize_MB) < maxStorageScaleLimit_MB) ?
            curCapacity + storageScaleupSize_MB : 
            maxStorageScaleLimit_MB;

    //***************************************
    // Update Sku & Properties
    //***************************************
    log.Info(string.Format("Current SKU & Properties: " + 
        "name:{0}, Tier:{1}, Family:{2}, Capacity:{3}, StorageMB:{4}",
        gr.Sku.Name, gr.Sku.Tier, gr.Sku.Family, gr.Sku.Capacity, curStorageProfile["storageMB"]));
    Dictionary<string, object> newProperties = curProperties;
    Dictionary<string, string> newStorageProfile = curStorageProfile;

    if (newCapacity == int.Parse(curStorageProfile["storageMB"]))
    {
        string m = string.Format(
            "Cannot scale Database storage size anymore as it has reached max number: Name: {0}",
             resourceName);
        log.Info(m);
        throw new Exception(m);

    }
    newStorageProfile["storageMB"] = newCapacity.ToString();
    newProperties["storageProfile"] = newStorageProfile;
    log.Info(string.Format("New SKU & Properties: " +
        "name:{0}, Tier:{1}, Family:{2}, Capacity:{3}, StorageMB:{4}",
        cursku.Name, cursku.Tier, cursku.Family, cursku.Capacity, newStorageProfile["storageMB"]));

    // AdminLoginPassword need to be added anytime of updating db resource as it's not loaded in properties object
    if (!newProperties.ContainsKey("administratorLoginPassword"))
    {
        newProperties.Add("administratorLoginPassword", _dbAdminPassword);
        newProperties["administratorLogin"] = _dbAdminUser;  // Set dbAdminuser together with the password for its consistency  
    }

    var databaseParams = new GenericResource
    {
        Location = region,
        Sku = cursku,
        Properties = newProperties
    };
    var database = rmClient.Resources.CreateOrUpdate(
        resourceGroupName,
        resourceProviderNamespace,   // Microsoft.DBforMySQL or Microsoft.DBforPostgreSQL
        "",                          // fixed        
        "servers",                   // fixed            
        resourceName,                // db account name              
        Define.ApiVersion,           // API Version fixed 
        databaseParams);             // parameters                

    log.Info(string.Format("Database Storage scaling completed successfully: Name: {0} and Id: {1}",
         database.Name, database.Id));
}