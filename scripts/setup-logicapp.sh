#!/bin/bash
set -x -e

cwd=`dirname "$0"`
expr "$0" : "/.*" > /dev/null || cwd=`(cd "$cwd" && pwd)`
. $cwd/project.conf

RESOURCE_GROUP=$ResourceGroup
STORAGE_ACCOUNT_NAME=$StorageAccountName

##################################################
# Logic App
##################################################

LOGICAPP_NAME=$LogicAppName
FUNCTIONS_APP_NAME=$FunctionsAppName
WEBHOOK_ENDPOINT=$WebhookSubscribeAPIEndpoint
TEMPLATE_FILE=$cwd/../logicapp/LogicApp.json
PARAM_TEMPLATE_FILE=$cwd/../logicapp/LogicApp.parameters.json
PARAM_FILE=$cwd/../logicapp/_LogicApp.parameters.json

# Set variables
## [NOTE]
##   Change to use replacing values on the fly from inplace mode 
##   as inplace mode (-i) doesn't work on some environment
#perl -p -i -e "s,{logicAppName},$LOGICAPP_NAME,g" $PARAM_FILE
#perl -p -i -e "s,{webhookSubscribeEndpoint},$WEBHOOK_ENDPOINT,g" $PARAM_FILE
cat $PARAM_TEMPLATE_FILE | \
        perl -p -e "s,{logicAppName},$LOGICAPP_NAME,g"  | \
        perl -p -e "s,{webhookSubscribeEndpoint},$WEBHOOK_ENDPOINT,g" > $PARAM_FILE

az group deployment create --name $LOGICAPP_NAME \
     --resource-group $RESOURCE_GROUP \
     --template-file $TEMPLATE_FILE  \
     --parameters @$PARAM_FILE

rm $PARAM_FILE
