IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[Projects]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Projects] (
        [Id]         UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [TenantId]   UNIQUEIDENTIFIER NOT NULL,
        [Name]       NVARCHAR(200)    NOT NULL,
        [CreatedUtc] DATETIME2        NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE INDEX [IX_Projects_TenantId] ON [dbo].[Projects] ([TenantId]);
END
