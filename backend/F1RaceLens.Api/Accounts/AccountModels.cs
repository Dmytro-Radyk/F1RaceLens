using Microsoft.AspNetCore.Identity;

namespace F1RaceLens.Api.Accounts;

public sealed class ApplicationUser : IdentityUser
{
    public string Language { get; set; } = "uk";
    public string Theme { get; set; } = "dark";
}

public sealed class UserFavourite
{
    public string UserId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string EntityId { get; set; } = "";
}

public sealed record LoginRequest(string? Login, string? Password);
public sealed record RegisterRequest(string? Login, string? Email, string? Password, string? Language, string? Theme);
public sealed record PreferencesRequest(string? Language, string? Theme);
public sealed record FavouritesRequest(string[]? DriverIds, string[]? TeamIds);
public sealed record FavouriteResponse(string Kind, string Id, string Name);
public sealed record AccountResponse(string Id, string Login, string Email, string Language, string Theme, string[] DriverIds, string[] TeamIds, FavouriteResponse[] Favourites);
