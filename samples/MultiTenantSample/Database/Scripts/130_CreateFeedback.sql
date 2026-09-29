IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[Feedback]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Feedback] (
        [Id]         UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [TenantId]   UNIQUEIDENTIFIER NOT NULL,
        [Text]       NVARCHAR(MAX)    NOT NULL,
        [CreatedUtc] DATETIME2        NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE INDEX [IX_Feedback_TenantId] ON [dbo].[Feedback] ([TenantId]);
END
