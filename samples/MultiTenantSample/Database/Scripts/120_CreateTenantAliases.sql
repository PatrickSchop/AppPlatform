IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[TenantAliases]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[TenantAliases] (
        [Id]       UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [Slug]     NVARCHAR(100)    NOT NULL,
        [TenantId] UNIQUEIDENTIFIER NOT NULL
    );

    CREATE UNIQUE INDEX [IX_TenantAliases_Slug] ON [dbo].[TenantAliases] ([Slug]);
END

IF NOT EXISTS (SELECT * FROM [dbo].[TenantAliases] WHERE [Slug] = N'contoso')
BEGIN
    INSERT INTO [dbo].[TenantAliases] ([Id], [Slug], [TenantId])
    VALUES (NEWID(), N'contoso', N'11111111-1111-1111-1111-111111111111');
END

IF NOT EXISTS (SELECT * FROM [dbo].[TenantAliases] WHERE [Slug] = N'fabrikam')
BEGIN
    INSERT INTO [dbo].[TenantAliases] ([Id], [Slug], [TenantId])
    VALUES (NEWID(), N'fabrikam', N'22222222-2222-2222-2222-222222222222');
END
