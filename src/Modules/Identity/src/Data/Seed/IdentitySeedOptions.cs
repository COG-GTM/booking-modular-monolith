namespace Identity.Data.Seed;

public class IdentitySeedOptions
{
    // Initial passwords for the seeded users. Must come from a managed secret
    // (environment variable, user-secrets, vault). A user whose password is not
    // configured is not seeded.
    public string? AdminPassword { get; set; }

    public string? UserPassword { get; set; }
}
