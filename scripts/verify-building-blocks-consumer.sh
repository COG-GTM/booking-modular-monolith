#!/usr/bin/env bash
# Builds a throwaway service that consumes only BookingModularMonolith.BuildingBlocks.Core + .Web
# from the local feed, proving a service can take a minimal subset without EF Core.
# Usage: scripts/verify-building-blocks-consumer.sh [version] [feed-dir]
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${1:-$(dotnet msbuild "$repo_root/src/BuildingBlocks/Core/BuildingBlocks.Core.csproj" -getProperty:PackageVersion -nologo)}"
feed="$(cd "${2:-$repo_root/artifacts/nuget}" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

cat > "$work/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-buildingblocks" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML

cat > "$work/Consumer.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BookingModularMonolith.BuildingBlocks.Core" Version="$version" />
    <PackageReference Include="BookingModularMonolith.BuildingBlocks.Web" Version="$version" />
  </ItemGroup>
</Project>
XML

cat > "$work/Program.cs" <<'CS'
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Web;

var services = new ServiceCollection();
services.AddLogging();
services.AddEventDispatcher();
services.AddHttpContextEventHeaders();
services.AddEventMapper<SampleMapper>();
services.AddScoped<IIntegrationEventPublisher, ConsolePublisher>();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
await scope.ServiceProvider.GetRequiredService<IEventDispatcher>().SendAsync(new Sampled(Guid.NewGuid()));

public sealed record Sampled(Guid Id) : IDomainEvent;
public sealed record SampledIntegration(Guid Id) : IIntegrationEvent;

public sealed class SampleMapper : IEventMapper
{
    public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) => @event is Sampled s ? new SampledIntegration(s.Id) : null;
    public IInternalCommand? MapToInternalCommand(IDomainEvent @event) => null;
}

public sealed class ConsolePublisher : IIntegrationEventPublisher
{
    public Task PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"published {envelope.Message!.GetType().Name}");
        return Task.CompletedTask;
    }

    public Task AddInternalCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : class, IInternalCommand => Task.CompletedTask;
}
CS

dotnet restore "$work/Consumer.csproj" --configfile "$work/nuget.config" -nologo -v quiet
if grep -qi '"Microsoft.EntityFrameworkCore' "$work/obj/project.assets.json"; then
  echo "error: consumer restore graph contains EF Core" >&2
  exit 1
fi
dotnet run --project "$work/Consumer.csproj" --no-restore -nologo | tee "$work/out.txt"
grep -q "published SampledIntegration" "$work/out.txt"
echo "Minimal consumer (Core + Web, no EF Core) restored from $feed and ran OK"
