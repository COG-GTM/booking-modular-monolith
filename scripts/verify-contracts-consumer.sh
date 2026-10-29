#!/usr/bin/env bash
# Proves producers/consumers build and run against the *packaged* contracts, not the in-repo project:
#  1. a throwaway service restores only BookingModularMonolith.Contracts.Messages <version> from the local feed and
#     round-trips UserCreated through MassTransit's in-memory bus (Identity -> Passenger flow);
#  2. the Identity and Passenger modules build with -p:UseContractsPackage=true pinned to <version>.
# Usage: scripts/verify-contracts-consumer.sh [version] [feed-dir]
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${1:-$(sed -n 's:.*<ContractsMessagesVersion[^>]*>\(.*\)</ContractsMessagesVersion>.*:\1:p' "$repo_root/src/Contracts/Versions.props")}"
feed="$(cd "${2:-$repo_root/artifacts/nuget}" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

cat > "$work/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-contracts" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML

cat > "$work/Consumer.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BookingModularMonolith.Contracts.Messages" Version="[$version]" />
    <PackageReference Include="MassTransit" Version="8.5.8" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.3" />
  </ItemGroup>
</Project>
XML

cat > "$work/Program.cs" <<'CS'
using BuildingBlocks.Contracts.EventBus.Messages;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

var received = new TaskCompletionSource<UserCreated>(TaskCreationOptions.RunContinuationsAsynchronously);
await using var provider = new ServiceCollection()
    .AddSingleton(received)
    .AddMassTransit(x =>
    {
        x.AddConsumer<PassengerConsumer>();
        x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
    })
    .BuildServiceProvider();

var bus = provider.GetRequiredService<IBusControl>();
await bus.StartAsync();
try
{
    var sent = new UserCreated(Guid.NewGuid(), "Sam Hamed", "P1234567");
    await bus.Publish(sent);
    var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));
    if (got != sent)
        throw new InvalidOperationException($"payload mismatch: {got}");
    Console.WriteLine($"consumed {MessageUrn.ForTypeString<UserCreated>()} from {typeof(UserCreated).Assembly.GetName().Name} {typeof(UserCreated).Assembly.GetName().Version}");
}
finally
{
    await bus.StopAsync();
}

public sealed class PassengerConsumer(TaskCompletionSource<UserCreated> received) : IConsumer<UserCreated>
{
    public Task Consume(ConsumeContext<UserCreated> context)
    {
        received.TrySetResult(context.Message);
        return Task.CompletedTask;
    }
}
CS

dotnet restore "$work/Consumer.csproj" --configfile "$work/nuget.config" -nologo -v quiet
if grep -qiE '"(Microsoft\.EntityFrameworkCore|Npgsql|MongoDB|EventStore|BookingModularMonolith\.BuildingBlocks)' "$work/obj/project.assets.json"; then
  echo "error: contracts consumer restore graph pulls in BuildingBlocks or data-store packages" >&2
  exit 1
fi
dotnet run --project "$work/Consumer.csproj" --no-restore -nologo | tee "$work/out.txt"
grep -q "consumed urn:message:BuildingBlocks.Contracts.EventBus.Messages:UserCreated" "$work/out.txt"

# Build from a scratch copy so the package-mode restore never touches the in-repo obj/ folders.
mkdir -p "$work/repo"
(cd "$repo_root" && tar --exclude='*/bin' --exclude='*/obj' -cf - Directory.Build.props global.json .editorconfig src) | tar -xf - -C "$work/repo"
for module in Identity Passenger; do
  echo "==> building $module against BookingModularMonolith.Contracts.Messages $version (package mode)"
  dotnet build "$work/repo/src/Modules/$module/src/$module.csproj" -c Release -nologo -v quiet \
    -p:UseContractsPackage=true -p:ContractsMessagesPackageVersion="$version" -p:RestoreAdditionalProjectSources="$feed"
  grep -q '"BookingModularMonolith.Contracts.Messages/'"$version"'"' "$work/repo/src/Modules/$module/src/obj/project.assets.json"
  if [[ -e "$work/repo/src/Contracts/Contracts.Messages/obj" ]]; then
    echo "error: $module still built the in-repo Contracts.Messages project" >&2
    exit 1
  fi
done
echo "Contracts $version restored from $feed: standalone consumer and Identity/Passenger (package mode) OK"
