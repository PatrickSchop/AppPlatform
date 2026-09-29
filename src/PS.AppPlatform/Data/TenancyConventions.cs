using Microsoft.EntityFrameworkCore;

namespace PS.AppPlatform.Data;

public static class TenancyConventions
{
    /// <summary>
    /// Returns one message per mapped TenantEntity table that lacks a TenantId
    /// UNIQUEIDENTIFIER NOT NULL column, by querying INFORMATION_SCHEMA.COLUMNS.
    /// </summary>
    public static async Task<IReadOnlyList<string>> FindTenantColumnViolationsAsync(
        DbContext context,
        CancellationToken ct = default)
    {
        var violations = new List<string>();
        var tenantEntityType = typeof(TenantEntity);

        var entityTypesToCheck = context.Model.GetEntityTypes()
            .Where(et => tenantEntityType.IsAssignableFrom(et.ClrType) && et.FindPrimaryKey() != null)
            .ToList();

        if (entityTypesToCheck.Count == 0)
        {
            return violations;
        }

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(ct);

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME IN (SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo')
                ORDER BY TABLE_NAME, ORDINAL_POSITION";

            using var reader = await command.ExecuteReaderAsync(ct);
            var columnsByTable = new Dictionary<string, List<(string Name, string Type, string Nullable)>>(StringComparer.OrdinalIgnoreCase);

            while (await reader.ReadAsync(ct))
            {
                var tableName = reader.GetString(0);
                var columnName = reader.GetString(1);
                var dataType = reader.GetString(2);
                var isNullable = reader.GetString(3);

                if (!columnsByTable.ContainsKey(tableName))
                {
                    columnsByTable[tableName] = new List<(string, string, string)>();
                }

                columnsByTable[tableName].Add((columnName, dataType, isNullable));
            }

            foreach (var entityType in entityTypesToCheck)
            {
                var tableName = entityType.GetTableName();
                if (string.IsNullOrEmpty(tableName))
                {
                    continue;
                }

                if (!columnsByTable.TryGetValue(tableName, out var columns))
                {
                    violations.Add(
                        $"Table '{tableName}' (entity {entityType.ClrType.Name}) not found in database");
                    continue;
                }

                var tenantColumn = columns.FirstOrDefault(c =>
                    c.Name.Equals("TenantId", StringComparison.OrdinalIgnoreCase));

                if (tenantColumn == default)
                {
                    violations.Add(
                        $"Table '{tableName}' (entity {entityType.ClrType.Name}) missing TenantId column");
                }
                else if (!tenantColumn.Type.Equals("uniqueidentifier", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(
                        $"Table '{tableName}' (entity {entityType.ClrType.Name}) TenantId column has wrong type '{tenantColumn.Type}' (must be uniqueidentifier)");
                }
                else if (tenantColumn.Nullable.Equals("YES", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(
                        $"Table '{tableName}' (entity {entityType.ClrType.Name}) TenantId column must be NOT NULL");
                }
            }
        }
        finally
        {
            await connection.CloseAsync();
        }

        return violations;
    }
}
