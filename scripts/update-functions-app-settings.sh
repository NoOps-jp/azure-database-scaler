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
SUBSCRIPTION_ID=$SubscriptionId
SP_TENANT_ID=$TenantId
SP_CLIENT_ID=$ClientId
SP_CLIENT_SECRET=$ClientSecret
DB_ADMIN_USER=$DatabaseAdminUser
DB_ADMIN_PASSWORD=$DatabaseAdminPassword

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
    DB_ADMIN_USER=$DB_ADMIN_USER \
    DB_ADMIN_PASSWORD=$DB_ADMIN_PASSWORD
