using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace MesApp.Client.Web.Auth;

/// <summary>TokenStoreの内容からBlazorの認証状態を組み立てる</summary>
public class ApiAuthenticationStateProvider(TokenStore tokenStore) : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!tokenStore.IsAuthenticated)
        {
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        }

        var user = tokenStore.User!;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName),
            new("display_name", user.DisplayName),
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var identity = new ClaimsIdentity(claims, authenticationType: "jwt");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    public void NotifyChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
