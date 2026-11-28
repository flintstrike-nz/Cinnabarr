using System;
using System.Linq;
using System.Security.Claims;
using Jellyfin.Plugin.Cinnabarr.Configuration;

namespace Jellyfin.Plugin.Cinnabarr.Access
{
    [Flags]
    public enum CinnabarrRoles
    {
        None = 0,
        Requester = 1,
        Manager = 2,
        Player = 4,
        All = Requester | Manager | Player
    }

    /// <summary>
    /// Who may do what. Cinnabarr has one API key and no users, so the plugin decides per Jellyfin user.
    /// </summary>
    public static class RoleResolver
    {
        // The claim and role names Jellyfin's own authentication puts on a request (Jellyfin.Api.Constants).
        public const string UserIdClaim = "Jellyfin-UserId";
        public const string IsApiKeyClaim = "Jellyfin-IsApiKey";
        public const string AdministratorRole = "Administrator";

        public static CinnabarrRoles Resolve(ClaimsPrincipal? user, PluginConfiguration? config)
        {
            if (user == null || user.Identity?.IsAuthenticated != true)
            {
                return CinnabarrRoles.None;
            }

            if (user.IsInRole(AdministratorRole))
            {
                return CinnabarrRoles.All;
            }

            var userId = GetUserId(user);

            if (userId == Guid.Empty || config?.Users == null)
            {
                return CinnabarrRoles.None;
            }

            var access = config.Users.FirstOrDefault(u => u.UserId == userId);

            if (access == null)
            {
                return CinnabarrRoles.None;
            }

            var roles = CinnabarrRoles.None;

            // A manager can always request too.
            if (access.Requester || access.Manager)
            {
                roles |= CinnabarrRoles.Requester;
            }

            if (access.Manager)
            {
                roles |= CinnabarrRoles.Manager;
            }

            if (access.Player)
            {
                roles |= CinnabarrRoles.Player;
            }

            return roles;
        }

        public static Guid GetUserId(ClaimsPrincipal user)
        {
            var value = user.FindFirst(UserIdClaim)?.Value;

            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }

        public static string[] Names(CinnabarrRoles roles)
        {
            return Enum.GetValues<CinnabarrRoles>()
                .Where(r => r != CinnabarrRoles.None && r != CinnabarrRoles.All && roles.HasFlag(r))
                .Select(r => r.ToString().ToLowerInvariant())
                .ToArray();
        }
    }
}
