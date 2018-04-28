# azure-database-scaler
Event driven Azure Logic App & Functions App that scale-up or scale-down the capacity of your Azure database service instance (Currently only vCore # is supported)

Targets Azure Databases to scale in this project:
- [Azure Database for MySQL](https://azure.microsoft.com/en-us/services/mysql/)
- [Azure Database for PostgreSQL](https://azure.microsoft.com/en-us/services/postgresql/)

What to scale up/down?
- vCores ([What is vCore?](https://docs.microsoft.com/en-us/azure/mysql/concepts-pricing-tiers#compute-generations-vcores-and-memory))

How to scale up/down?
- azure-database-scaler starts scaling-up or scaling-down the number of vCore when it is triggered by Azure Monitor Metric Alerts (Alerts providers must be either `Microsoft.DBforMySQL` or `Microsoft.DBforPostgreSQL`)
- azure-database-scaler scale-up or scale-down the number of vCore within the same database tier & the same compute generations (Gen4 / Gen5) of your instance in the way like:
    - Basic Tier: 1 <-> 2
    - General Purpose Tier: 2 <-> 4 <-> 8 <-> 16 <-> 32
    - Memory Optimized Tier: 2 <-> 4 <-> 8 <-> 16 <-> 32
- azure-database-scaler scale-up the number of vCore when Alert Operator of triggering Alerts is either `GreaterThan` or `GreaterThanOrEqual`
- azure-database-scaler scale-down the number of vCore when Alert Operator of triggering Alerts is either `LessThan` or `LessThanOrEqual`

Architecture:
![](images/architecture-overview.png)

Relevant Services:
- [Azure Database for MySQL](https://azure.microsoft.com/en-us/services/mysql/)
- [Azure Database for PostgreSQL](https://azure.microsoft.com/en-us/services/postgresql/)
- [Azure Functions](https://azure.microsoft.com/en-us/services/functions/)
- [Azure Logic Apps](https://azure.microsoft.com/en-us/services/logic-apps/)
- [Azure Monitor Alerts (Classic)](https://docs.microsoft.com/en-us/azure/monitoring-and-diagnostics/monitoring-overview-alerts)
- [Azure Monitor Alerts (New)](https://docs.microsoft.com/en-us/azure/monitoring-and-diagnostics/monitoring-overview-unified-alerts)
- [Slack (for notification)](https://slack.com)

## How to deploy the scaler app
- [How to deploy the database scaler app](./docs/HOW-TO-DEPLOY-APP.md)

## How to setup Azure Metric Monitor for Autoscaling
- [[Classic Metric Alert] How to setup Azure Monitor Metric Alerts](./docs/HOW-TO-SETUP-ALERTS.md)
- \[New Metric Alert\] How to setup Azure Monitor Metric Alerts

