namespace Identity.Configurations;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Identity.Models;
using Microsoft.AspNetCore.Identity;

public class CommonPasswordValidator : IPasswordValidator<User>
{
    public const int MinimumLength = 12;
    public const int MinimumUniqueChars = 4;

    private const int MinimumIdentifierLength = 3;

    // Base words of the most common leaked passwords; "P@ssw0rd!2024" and "Welcome@1234" reduce to these.
    private static readonly HashSet<string> CommonBaseWords = new(StringComparer.Ordinal)
    {
        "password", "passwd", "pass", "qwerty", "qwertyuiop", "asdf", "asdfgh", "asdfghjkl", "zxcvbn",
        "zxcvbnm", "qazwsx", "welcome", "letmein", "admin", "administrator", "login", "changeme", "iloveyou",
        "monkey", "dragon", "master", "sunshine", "princess", "football", "baseball", "superman", "batman",
        "trustno", "abc", "abcd", "abcde", "abcdef", "test", "user", "secret", "default", "guest", "root",
        "hello", "summer", "winter", "spring", "autumn", "shadow", "freedom", "starwars", "whatever"
    };

    private static readonly Dictionary<char, char> LeetSubstitutions = new()
    {
        ['0'] = 'o', ['1'] = 'i', ['3'] = 'e', ['4'] = 'a', ['5'] = 's', ['7'] = 't', ['@'] = 'a', ['$'] = 's'
    };

    public Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        var errors = new List<IdentityError>();

        if (IsCommonPassword(password))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordTooCommon",
                Description = "Passwords must not be based on a commonly used password."
            });
        }

        if (ContainsIdentifier(password, user.UserName) || ContainsIdentifier(password, EmailLocalPart(user.Email)))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordContainsUserIdentifier",
                Description = "Passwords must not contain the username or email."
            });
        }

        return Task.FromResult(errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray()));
    }

    private static bool IsCommonPassword(string password)
    {
        var nonLetters = password.Where(c => !char.IsLetter(c)).Distinct().ToArray();
        var core = password.ToLowerInvariant().Trim(nonLetters);

        var normalized = new StringBuilder(core.Length);
        foreach (var c in core)
        {
            var mapped = LeetSubstitutions.TryGetValue(c, out var letter) ? letter : c;
            if (char.IsLetter(mapped))
            {
                normalized.Append(mapped);
            }
        }

        return CommonBaseWords.Contains(normalized.ToString());
    }

    private static bool ContainsIdentifier(string password, string? identifier) =>
        identifier is { Length: >= MinimumIdentifierLength } &&
        password.Contains(identifier, StringComparison.OrdinalIgnoreCase);

    private static string? EmailLocalPart(string? email)
    {
        var at = email?.IndexOf('@') ?? -1;
        return at > 0 ? email![..at] : null;
    }
}
