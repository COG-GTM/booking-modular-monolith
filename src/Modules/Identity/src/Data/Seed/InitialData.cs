namespace Identity.Data.Seed;

using System;
using System.Collections.Generic;
using Identity.Models;

public static class InitialData
{
    // Stable ids let the seeder prove that an existing account is the one it created
    // (a self-registered account sharing the username gets a random id and is never touched).
    public static readonly Guid AdminUserId = new("1d6a1a3e-6b38-4d4a-9a4e-2c0b1e5f7a01");
    public static readonly Guid UserUserId = new("1d6a1a3e-6b38-4d4a-9a4e-2c0b1e5f7a02");

    public static List<User> Users { get; }

    static InitialData()
    {
        Users = new List<User>
        {
            new User
            {
                Id = AdminUserId,
                FirstName = "Sam",
                LastName = "H",
                UserName = "samh",
                PassPortNumber = "12345678",
                Email = "sam@test.com",
                SecurityStamp = Guid.NewGuid().ToString(),
            },
            new User
            {
                Id = UserUserId,
                FirstName = "Sam2",
                LastName = "H2",
                UserName = "samh2",
                PassPortNumber = "87654321",
                Email = "sam2@test.com",
                SecurityStamp = Guid.NewGuid().ToString(),
            },
        };
    }
}
