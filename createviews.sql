CREATE OR ALTER VIEW EventData
AS SELECT [id], [subject], [resourceuri], [status]
FROM OPENROWSET(​PROVIDER = 'CosmosDB',
                CONNECTION = 'Account=$(cosmosAccountName);Database=TenantMonitoring',
                OBJECT = 'EventLog',
                SERVER_CREDENTIAL = 'tenantmonitoring'
) 
WITH (  id    varchar(100),
        subject     varchar(1000) '$.eventgriddata.subject',
        resourceUri varchar(1000) '$.eventgriddata.data.resourceUri',
        status      varchar(15)
) AS rows
GO

CREATE OR ALTER VIEW EventLogs
AS SELECT [id], [Date] as logdate, [log] as logdetail
FROM OPENROWSET(​PROVIDER = 'CosmosDB',
                CONNECTION = 'Account=$(cosmosAccountName);Database=TenantMonitoring',
                OBJECT = 'EventLog',
                SERVER_CREDENTIAL = 'tenantmonitoring'
) 
WITH (  id    varchar(100),
        logs varchar(max) '$.logs'
) AS rows
CROSS APPLY OPENJSON ( logs )
WITH (
    Date datetime2,
    log varchar(max)
) AS logrows
GO