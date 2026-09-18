IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[__SchemaVersions]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[__SchemaVersions] (
        [ScriptName]  NVARCHAR(255) NOT NULL PRIMARY KEY,
        [AppliedUtc]  DATETIME2     NOT NULL CONSTRAINT [DF___SchemaVersions_AppliedUtc] DEFAULT GETUTCDATE(),
        [Checksum]    NVARCHAR(64)  NULL
    );
END
