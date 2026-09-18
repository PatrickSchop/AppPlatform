-- Run against the app's database as an Entra SQL admin on the SQL server.
-- Replace <identity-name> with the managed identity name from the bicep output, e.g. id-recipes.
-- The managed identity needs database-level permissions to authenticate and run migrations.

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'<identity-name>')
BEGIN
    CREATE USER [<identity-name>] FROM EXTERNAL PROVIDER;
END

ALTER ROLE db_datareader ADD MEMBER [<identity-name>];
ALTER ROLE db_datawriter ADD MEMBER [<identity-name>];
-- db_ddladmin is required because the app runs its own migrations.
-- If you move migrations outside the app (e.g., to a separate tool), you can drop this role.
ALTER ROLE db_ddladmin  ADD MEMBER [<identity-name>];
