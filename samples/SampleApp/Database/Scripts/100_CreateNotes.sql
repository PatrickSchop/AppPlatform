IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[Notes]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Notes] (
        [Id]         UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [Title]      NVARCHAR(200)    NOT NULL,
        [Body]       NVARCHAR(MAX)    NOT NULL DEFAULT '',
        [WordCount]  INT              NULL,
        [CreatedUtc] DATETIME2        NOT NULL DEFAULT GETUTCDATE()
    );
END
