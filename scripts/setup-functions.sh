#!/bin/bash
set -x -e

cwd=`dirname "$0"`
expr "$0" : "/.*" > /dev/null || cwd=`(cd "$cwd" && pwd)`
. $cwd/project.conf

RESOURCE_GROUP=$ResourceGroup
STORAGE_ACCOUNT_NAME=$StorageAccountName

# Get Storage Key
STORAGE_ACCESS_KEY=$(az storage account keys list --account-name $STORAGE_ACCOUNT_NAME --resource-group $RESOURCE_GROUP --output tsv |head -1 | awk '{print $3}')

##################################################
# Azure Functions
##################################################

FUNCTIONS_APP_NAME=$FunctionsAppName
FUNCTIONS_APP_SERVICE_PLAN_LOCATION=$FunctionsAppConsumptionPlanLocation
ZIPFILE=$cwd/../functions/functions.zip
SUBSCRIPTION_ID=$SubscriptionId
SP_TENANT_ID=$TenantId
SP_CLIENT_ID=$ClientId
SP_CLIENT_SECRET=$ClientSecret
DB_LOCATION=$DatabaseLocation
DB_RESOURCE_GROUP=$DatabaseResourceGroup
DB_NAME=$DatabaseAccountName
DB_ADMIN_USER=$DatabaseAdminUser
DB_ADMIN_PASSWORD=$DatabaseAdminPassword
MAX_STORAGE_SCALE_LIMIT=$MaxStorageScaleLimit
STORAGE_SCALEUP_SIZE=$StorageScaleupSize

# Create Functions App (Consumption Plan)
az functionapp create --name $FUNCTIONS_APP_NAME \
    --resource-group $RESOURCE_GROUP \
    --consumption-plan-location $FUNCTIONS_APP_SERVICE_PLAN_LOCATION \
    --storage-account $STORAGE_ACCOUNT_NAME
### [NOTE] Use 'az functionapp list-consumption-locations' to view available locations

# Zipping functions
cd $cwd/../functions
zip -r $ZIPFILE .

# Deploying functions
az functionapp deployment source config-zip  --name $FUNCTIONS_APP_NAME \
    --resource-group $RESOURCE_GROUP \
    --src $ZIPFILE

## Configure App Settings
az webapp config appsettings set \
  -n $FUNCTIONS_APP_NAME \
  -g $RESOURCE_GROUP \
  --settings \
    AZURE_TENANT_ID=$SP_TENANT_ID \
    AZURE_CLIENT_ID=$SP_CLIENT_ID \
    AZURE_CLIENT_SECRET=$SP_CLIENT_SECRET \
    AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID \
    STORAGE_CONNECTION="DefaultEndpointsProtocol=https;AccountName=$STORAGE_ACCOUNT_NAME;AccountKey=$STORAGE_ACCESS_KEY;EndpointSuffix=core.windows.net" \
    DB_LOCATION=$DB_LOCATION \
    DB_RESOURCE_GROUP=$DB_RESOURCE_GROUP \
    DB_NAME=$DB_NAME \
    DB_ADMIN_USER=$DB_ADMIN_USER \
    DB_ADMIN_PASSWORD=$DB_ADMIN_PASSWORD \
    MAX_STORAGE_SCALE_LIMIT=$MAX_STORAGE_SCALE_LIMIT \
    STORAGE_SCALEUP_SIZE=$STORAGE_SCALEUP_SIZE
