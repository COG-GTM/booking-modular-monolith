namespace Integration.Test.Fakes;

using System;
using BuildingBlocks.Web;

public class FakeCurrentUserProvider : ICurrentUserProvider
{
    public static readonly Guid UserId = new("7b0e3c3e-1a4c-4f4e-9d47-2a0f5f0c7a11");

    public long? GetCurrentUserId() => null;

    public Guid? GetCurrentUserGuid() => UserId;
}
