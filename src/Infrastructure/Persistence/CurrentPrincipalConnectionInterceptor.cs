using System.Data.Common;
using Kart.Wishlist.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Kart.Wishlist.Infrastructure.Persistence;

/// <summary>
/// database-design.md's Row-Level Security session-scoped principal setting: "immediately after
/// acquiring a pooled connection and before any query runs, this service issues
/// <c>SET LOCAL app.current_principal = &lt;id&gt;</c> and
/// <c>SET LOCAL app.current_principal_kind = &lt;'user'|'service'|'system'&gt;</c>." Uses
/// <c>set_config(...)</c> rather than a literal <c>SET</c> statement so the principal id is
/// passed as a bound parameter, never string-interpolated SQL (kart-cart-service/kart-user-service
/// precedent — this interceptor is a byte-for-byte match of theirs, generalized to this service's
/// own <see cref="ICurrentPrincipalAccessor"/>).
/// </summary>
public sealed class CurrentPrincipalConnectionInterceptor(ICurrentPrincipalAccessor principalAccessor) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is NpgsqlConnection npgsqlConnection)
        {
            await SetConfigAsync(npgsqlConnection, "app.current_principal", principalAccessor.PrincipalId, cancellationToken);
            await SetConfigAsync(npgsqlConnection, "app.current_principal_kind", principalAccessor.PrincipalKind, cancellationToken);
        }

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static async Task SetConfigAsync(NpgsqlConnection connection, string settingName, string value, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config($1, $2, false);";
        command.Parameters.Add(new NpgsqlParameter { Value = settingName });
        command.Parameters.Add(new NpgsqlParameter { Value = value });
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
