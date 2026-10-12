using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Unit.Test.Common;

/// <summary>
/// Makes <c>Database.GetPendingMigrationsAsync()</c> report no pending migrations so the seeder
/// can run against an <c>EnsureCreated</c> SQLite database instead of the real Postgres migrations.
/// </summary>
public class NoMigrationsAssembly : IMigrationsAssembly
{
    public IReadOnlyDictionary<string, TypeInfo> Migrations { get; } = new Dictionary<string, TypeInfo>();

    public ModelSnapshot ModelSnapshot => null;

    public Assembly Assembly => typeof(NoMigrationsAssembly).Assembly;

    public string FindMigrationId(string nameOrId) => null;

    public Migration CreateMigration(TypeInfo migrationClass, string activeProvider) =>
        throw new NotSupportedException();
}
