using System;
using System.Collections.Generic;
using System.Security.Claims;
using Jellyfin.Plugin.Cinnabarr.Access;
using Jellyfin.Plugin.Cinnabarr.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Cinnabarr.Tests
{
    public class RoleResolverTests
    {
        private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

        private static ClaimsPrincipal SignedIn(Guid id, string role = "User")
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(RoleResolver.UserIdClaim, id.ToString("N")), new Claim(ClaimTypes.Role, role) },
                "Jellyfin");

            return new ClaimsPrincipal(identity);
        }

        private static PluginConfiguration Config(params UserAccess[] users)
        {
            return new PluginConfiguration { Users = new List<UserAccess>(users) };
        }

        [Fact]
        public void Anonymous_has_no_roles()
        {
            Assert.Equal(CinnabarrRoles.None, RoleResolver.Resolve(new ClaimsPrincipal(new ClaimsIdentity()), Config()));
            Assert.Equal(CinnabarrRoles.None, RoleResolver.Resolve(null, Config()));
        }

        [Fact]
        public void Administrator_has_every_role()
        {
            Assert.Equal(CinnabarrRoles.All, RoleResolver.Resolve(SignedIn(Alice, RoleResolver.AdministratorRole), Config()));
        }

        [Fact]
        public void Unlisted_user_has_no_roles()
        {
            var config = Config(new UserAccess { UserId = Bob, Requester = true });

            Assert.Equal(CinnabarrRoles.None, RoleResolver.Resolve(SignedIn(Alice), config));
        }

        [Fact]
        public void Listed_user_gets_their_roles()
        {
            var config = Config(new UserAccess { UserId = Alice, Requester = true, Player = true });

            Assert.Equal(CinnabarrRoles.Requester | CinnabarrRoles.Player, RoleResolver.Resolve(SignedIn(Alice), config));
        }

        [Fact]
        public void Manager_can_also_request()
        {
            var config = Config(new UserAccess { UserId = Alice, Manager = true });

            Assert.Equal(CinnabarrRoles.Requester | CinnabarrRoles.Manager, RoleResolver.Resolve(SignedIn(Alice), config));
        }

        [Fact]
        public void Names_lists_single_roles_only()
        {
            Assert.Equal(new[] { "requester", "manager", "player" }, RoleResolver.Names(CinnabarrRoles.All));
            Assert.Empty(RoleResolver.Names(CinnabarrRoles.None));
        }
    }
}
