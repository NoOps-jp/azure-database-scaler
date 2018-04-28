#!/bin/bash
set -x -e

cwd=`dirname "$0"`
expr "$0" : "/.*" > /dev/null || cwd=`(cd "$cwd" && pwd)`
. $cwd/project.conf

##################################################
# Azure Stroage
##################################################

RESOURCE_GROUP=$ResourceGroup
STORAGE_ACCOUNT_NAME=$StorageAccountName

# Create Azure Storage Account for the app
az storage account create \
    --name $STORAGE_ACCOUNT_NAME \
    --resource-group $RESOURCE_GROUP \
    --sku Standard_LRS \
    --kind Storage
