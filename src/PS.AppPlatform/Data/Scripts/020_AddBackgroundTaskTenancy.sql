IF COL_LENGTH('dbo.BackgroundTasks', 'TenantId') IS NULL
    ALTER TABLE [dbo].[BackgroundTasks] ADD [TenantId] UNIQUEIDENTIFIER NULL;
GO
IF COL_LENGTH('dbo.BackgroundTasks', 'CreatedByUserId') IS NULL
    ALTER TABLE [dbo].[BackgroundTasks] ADD [CreatedByUserId] UNIQUEIDENTIFIER NULL;
GO
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_BackgroundTasks_TenantId_CreatedDate')
    CREATE INDEX [IX_BackgroundTasks_TenantId_CreatedDate]
        ON [dbo].[BackgroundTasks] ([TenantId], [CreatedDate]);
