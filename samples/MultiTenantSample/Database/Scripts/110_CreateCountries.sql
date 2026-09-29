IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[Countries]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Countries] (
        [Id]   UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [Code] NVARCHAR(10)     NOT NULL,
        [Name] NVARCHAR(200)    NOT NULL
    );
END

IF NOT EXISTS (SELECT * FROM [dbo].[Countries] WHERE [Code] = N'NL')
BEGIN
    INSERT INTO [dbo].[Countries] ([Id], [Code], [Name]) VALUES (NEWID(), N'NL', N'Netherlands');
END

IF NOT EXISTS (SELECT * FROM [dbo].[Countries] WHERE [Code] = N'US')
BEGIN
    INSERT INTO [dbo].[Countries] ([Id], [Code], [Name]) VALUES (NEWID(), N'US', N'United States');
END
