-- Tenant-owned table: it needs a TenantId column and an index on it (see docs/multi-tenancy.md).
IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[Items]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Items] (
        [Id]         UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [TenantId]   UNIQUEIDENTIFIER NOT NULL,
        [Name]       NVARCHAR(200)    NOT NULL,
        [CreatedUtc] DATETIME2        NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE INDEX [IX_Items_TenantId] ON [dbo].[Items] ([TenantId]);
END
