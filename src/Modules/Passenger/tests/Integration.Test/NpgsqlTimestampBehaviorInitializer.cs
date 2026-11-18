using System;
using System.Runtime.CompilerServices;

namespace Integration.Test;

internal static class NpgsqlTimestampBehaviorInitializer
{
    // Npgsql reads this switch once per process; set it before any test opens a connection,
    // matching BuildingBlocks.EFCore.Extensions.AddCustomDbContext.
    [ModuleInitializer]
    internal static void Initialize()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }
}
