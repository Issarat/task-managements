using System.Security.Cryptography;
using TaskManager.Domain.Entities;

namespace TaskManager.Domain.Extensions;

public static class AvatarExtension
{
    private static readonly string[] Adjectives =
    [
        "Brave",
        "Bright",
        "Calm",
        "Clever",
        "Curious",
        "Friendly",
        "Gentle",
        "Happy",
        "Kind",
        "Lucky",
        "Quick",
        "Wise"
    ];

    private static readonly string[] Animals =
    [
        "Bear",
        "Cat",
        "Deer",
        "Dolphin",
        "Falcon",
        "Fox",
        "Koala",
        "Otter",
        "Owl",
        "Panda",
        "Rabbit",
        "Tiger"
    ];

    private static readonly string[] Colors =
    [
        "#2563EB",
        "#4F46E5",
        "#7C3AED",
        "#9333EA",
        "#C026D3",
        "#DB2777",
        "#DC2626",
        "#EA580C",
        "#0D9488",
        "#059669",
        "#16A34A",
        "#0891B2"
    ];

    public static AvatarProfile GenerateRandom()
    {
        var adjective = PickRandom(Adjectives);
        var animal = PickRandom(Animals);
        var color = PickRandom(Colors);
        var displayName = $"{adjective} {animal}";

        return new AvatarProfile(
            DisplayName: displayName,
            ShortName: CreateShortName(displayName),
            Color: color);
    }

    public static string CreateShortName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return string.Concat(
            displayName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(part => char.ToUpperInvariant(part[0])));
    }

    public static void ApplyRandomAvatar(this AnonymousUser anonymousUser)
    {
        ArgumentNullException.ThrowIfNull(anonymousUser);

        var avatar = GenerateRandom();

        anonymousUser.DisplayName = avatar.DisplayName;
        anonymousUser.AvatarColor = avatar.Color;
    }

    private static string PickRandom(string[] values)
    {
        return values[RandomNumberGenerator.GetInt32(values.Length)];
    }
}

public sealed record AvatarProfile(
    string DisplayName,
    string ShortName,
    string Color);
