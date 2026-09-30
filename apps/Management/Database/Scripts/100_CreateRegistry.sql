IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[Applications]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Applications] (
        [Id]                UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [Key]               NVARCHAR(40)     NOT NULL,
        [DisplayName]       NVARCHAR(200)    NOT NULL,
        [Tenancy]           INT              NOT NULL,
        [ServicePrincipalId] UNIQUEIDENTIFIER NULL,
        [IsDisabled]        BIT              NOT NULL DEFAULT 0,
        [RegisteredUtc]     DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedUtc]        DATETIME2        NOT NULL DEFAULT GETUTCDATE()
    );

    CREATE UNIQUE INDEX [IX_Applications_Key] ON [dbo].[Applications] ([Key]);
END

IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[Roles]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Roles] (
        [Id]            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
        [Name]          NVARCHAR(64)     NOT NULL,
        [DisplayName]   NVARCHAR(200)    NOT NULL,
        [Description]   NVARCHAR(500)    NULL,
        [IsDeprecated]  BIT              NOT NULL DEFAULT 0,

        CONSTRAINT [FK_Roles_Applications] FOREIGN KEY ([ApplicationId])
            REFERENCES [dbo].[Applications] ([Id]) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX [IX_Roles_ApplicationId_Name] ON [dbo].[Roles] ([ApplicationId], [Name]);
END

IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[Tenants]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Tenants] (
        [Id]            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
        [Name]          NVARCHAR(200)    NOT NULL,
        [IsDisabled]    BIT              NOT NULL DEFAULT 0,
        [CreatedUtc]    DATETIME2        NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT [FK_Tenants_Applications] FOREIGN KEY ([ApplicationId])
            REFERENCES [dbo].[Applications] ([Id]) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX [IX_Tenants_ApplicationId_Name] ON [dbo].[Tenants] ([ApplicationId], [Name]);
END

IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[Teams]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Teams] (
        [Id]        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [TenantId]  UNIQUEIDENTIFIER NOT NULL,
        [Name]      NVARCHAR(200)    NOT NULL,
        [IsDefault] BIT              NOT NULL DEFAULT 0,

        CONSTRAINT [FK_Teams_Tenants] FOREIGN KEY ([TenantId])
            REFERENCES [dbo].[Tenants] ([Id]) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX [IX_Teams_TenantId_Name] ON [dbo].[Teams] ([TenantId], [Name]);
    CREATE UNIQUE INDEX [IX_Teams_TenantId_IsDefault] ON [dbo].[Teams] ([TenantId]) WHERE [IsDefault] = 1;
END

IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Users] (
        [Id]             UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [ObjectId]       NVARCHAR(64)     NULL,
        [IssuerTenantId] NVARCHAR(64)     NULL,
        [DisplayName]    NVARCHAR(200)    NOT NULL,
        [Email]          NVARCHAR(254)    NULL,
        [IsDisabled]     BIT              NOT NULL DEFAULT 0,
        [CreatedUtc]     DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        [BoundUtc]       DATETIME2        NULL
    );

    CREATE UNIQUE INDEX [IX_Users_ObjectId_IssuerTenantId]
        ON [dbo].[Users] ([ObjectId], [IssuerTenantId])
        WHERE [ObjectId] IS NOT NULL;
END

IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[TeamMembers]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[TeamMembers] (
        [Id]     UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [TeamId] UNIQUEIDENTIFIER NOT NULL,
        [UserId] UNIQUEIDENTIFIER NOT NULL,

        CONSTRAINT [FK_TeamMembers_Teams] FOREIGN KEY ([TeamId])
            REFERENCES [dbo].[Teams] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TeamMembers_Users] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[Users] ([Id]) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX [IX_TeamMembers_TeamId_UserId] ON [dbo].[TeamMembers] ([TeamId], [UserId]);
END

IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[RoleAssignments]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[RoleAssignments] (
        [Id]           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [TeamMemberId] UNIQUEIDENTIFIER NOT NULL,
        [RoleId]       UNIQUEIDENTIFIER NOT NULL,

        CONSTRAINT [FK_RoleAssignments_TeamMembers] FOREIGN KEY ([TeamMemberId])
            REFERENCES [dbo].[TeamMembers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RoleAssignments_Roles] FOREIGN KEY ([RoleId])
            REFERENCES [dbo].[Roles] ([Id]) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX [IX_RoleAssignments_TeamMemberId_RoleId]
        ON [dbo].[RoleAssignments] ([TeamMemberId], [RoleId]);
END

IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[Invitations]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[Invitations] (
        [Id]              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [UserId]          UNIQUEIDENTIFIER NOT NULL,
        [TokenHash]       CHAR(64)         NOT NULL,
        [ExpiresUtc]      DATETIME2        NOT NULL,
        [AcceptedUtc]     DATETIME2        NULL,
        [RevokedUtc]      DATETIME2        NULL,
        [CreatedByUserId] UNIQUEIDENTIFIER NOT NULL,

        CONSTRAINT [FK_Invitations_Users] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Invitations_Users_CreatedBy] FOREIGN KEY ([CreatedByUserId])
            REFERENCES [dbo].[Users] ([Id]) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX [IX_Invitations_TokenHash] ON [dbo].[Invitations] ([TokenHash]);
END
