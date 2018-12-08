﻿// Copyright (c) 2018 Jon P Smith, GitHub: JonPSmith, web: http://www.thereformedprogrammer.net/
// Licensed under MIT license. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PermissionParts;
using TestWebApp.Data;

namespace TestWebApp.AddedCode
{
    public class AuthCookieValidate
    {
        private readonly DbContextOptions<ExtraAuthorizeDbContext> _rolesDbOptions;

        /// <summary>
        /// This sets up
        /// </summary>
        /// <param name="rolesDbOptions"></param>
        public AuthCookieValidate(DbContextOptions<ExtraAuthorizeDbContext> rolesDbOptions)
        {
            _rolesDbOptions = rolesDbOptions;
        }

        public async Task ValidateAsync(CookieValidatePrincipalContext context)
        {
            if (context.Principal.Claims.Any(x => x.Type == PermissionConstants.PackedPermissionClaimType))
                return;

            //No permissions in the claims so we need to add it
            //This is only happen once after the user has logged in
            var claims = new List<Claim>();
            foreach (var claim in context.Principal.Claims)
            {
                claims.Add(claim);
            }

            var usersRoles = context.Principal.Claims.Where(x => x.Type == ClaimTypes.Role).Select(x => x.Value)
                .ToList();
            //I can't inject the DbContext here because that is dynamic, but I can pass in the database options because that is a singleton
            //From that I can create a valid dbContext to access the database
            using (var dbContext = new ExtraAuthorizeDbContext(_rolesDbOptions))
            {
                //This gets all the permissions, with a distinct to remove duplicates
                var permissionsForUser = await dbContext.RolesToPermissions.Where(x => usersRoles.Contains(x.RoleName))
                    .SelectMany(x => x.PermissionsInRole)
                    .Distinct()
                    .ToListAsync();
                //we get the modules this user is allows to see
                var userModules =
                    dbContext.ModulesForUsers.Find(
                        context.Principal.Claims.SingleOrDefault(x => x.Type == ClaimTypes.Email))?.AllowedPaidForModules ?? PaidForModules.None;
                //Now we remove permissions that are linked to modules that the user has no access to
                var filteredPermissions =
                    from permission in permissionsForUser
                    let moduleAttr = typeof(Permissions).GetMember(permission.ToString())[0]
                        .GetCustomAttribute<PermissionLinkedToModuleAttribute>()
                    where moduleAttr == null || userModules.HasFlag(moduleAttr.PaidForModule)
                    select permission;

                  //Now add it to the claim
                  claims.Add(new Claim(PermissionConstants.PackedPermissionClaimType,
                      filteredPermissions.PackPermissionsIntoString()));
            }

            var identity = new ClaimsIdentity(claims, "Cookie");
            var newPrincipal = new ClaimsPrincipal(identity);

            context.ReplacePrincipal(newPrincipal);
            context.ShouldRenew = true;
        }
    }
}