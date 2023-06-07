CREATE OR ALTER VIEW EventData
AS SELECT [id], [subject], [resourceuri], [status]
CREATE OR ALTER VIEW EventData
AS SELECT [id], [subject], [resourceuri], [action], Convert(datetime2, createdDate, 126) as [createddate], Convert(datetime2, completedDate, 126) as [completeddate]
FROM OPENROWSET(​PROVIDER = 'CosmosDB',
                CONNECTION = 'Account=$(cosmosAccountName);Database=TenantMonitoring',
                OBJECT = 'EventLog',
                SERVER_CREDENTIAL = 'tenantmonitoring'
) 
WITH (  id    varchar(100),
        subject     varchar(1000) '$.eventgriddata.subject',
        resourceUri varchar(1000) '$.eventgriddata.data.resourceUri',
        action      varchar(1000) '$.eventgriddata.data.authorization.action',
        createdDate varchar(8000) '$.createdDate',
        completedDate varchar(8000) '$.completedDate'
) AS rows
GO

CREATE OR ALTER VIEW JobLogs
AS SELECT [id], [name], [description],  [api], [status], Convert(datetime2, rundate, 126) as [rundate]
FROM OPENROWSET(​PROVIDER = 'CosmosDB',
                CONNECTION = 'Account=$(cosmosAccountName);Database=TenantMonitoring',
                OBJECT = 'EventLog',
                SERVER_CREDENTIAL = 'tenantmonitoring'
) 
WITH (  id    varchar(100),
        jobs varchar(max) '$.jobsConfig.jobs'
) AS rows
CROSS APPLY OPENJSON ( jobs )
WITH (
    Name varchar(1000) '$.Name',
    Description varchar(max) '$.Description',
    api varchar(max) '$.Api',
    status varchar(100) '$.Status',
    rundate varchar(8000) '$.RunDate'
) AS jobrows
GO