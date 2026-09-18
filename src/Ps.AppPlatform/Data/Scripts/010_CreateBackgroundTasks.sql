IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[BackgroundTasks]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[BackgroundTasks] (
        [Id]                    UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [TaskType]              NVARCHAR(100)    NOT NULL,
        [Status]                INT              NOT NULL DEFAULT 0,
        [StatusMessage]         NVARCHAR(MAX)    NOT NULL DEFAULT '',
        [CompletionPercentage]  INT              NOT NULL DEFAULT 0,
        [Description]           NVARCHAR(MAX)    NOT NULL DEFAULT '',
        [RequiresNotification]  BIT              NOT NULL DEFAULT 0,
        [TaskData]              NVARCHAR(MAX)    NOT NULL DEFAULT '',
        [CreatedDate]           DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedDate]           DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        [StartedDate]           DATETIME2        NULL,
        [CompletedDate]         DATETIME2        NULL,
        [ExecutionManagerId]    UNIQUEIDENTIFIER NULL,
        [LeaseExpiresUtc]       DATETIME2        NULL
    );

    CREATE INDEX [IX_BackgroundTasks_Status] ON [dbo].[BackgroundTasks] ([Status]);
    CREATE INDEX [IX_BackgroundTasks_TaskType] ON [dbo].[BackgroundTasks] ([TaskType]);
    CREATE INDEX [IX_BackgroundTasks_CreatedDate] ON [dbo].[BackgroundTasks] ([CreatedDate]);
    CREATE INDEX [IX_BackgroundTasks_ExecutionManagerId_Status]
        ON [dbo].[BackgroundTasks] ([ExecutionManagerId], [Status]);
    CREATE INDEX [IX_BackgroundTasks_Lease]
        ON [dbo].[BackgroundTasks] ([Status], [LeaseExpiresUtc]);
END
