using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Security;

namespace FeeBilling.Web.Security
{
    /// <summary>Roles from dbo.AppUserRoles. Cached in the roles cookie (cacheRolesInCookie in Web.config).</summary>
    public class FeeBillingRoleProvider : RoleProvider
    {
        private static string ConnectionString
        {
            get { return ConfigurationManager.ConnectionStrings["FeeBillingAdo"].ConnectionString; }
        }

        public override string ApplicationName { get; set; }

        public override string[] GetRolesForUser(string username)
        {
            var roles = new List<string>();
            using (var connection = new SqlConnection(ConnectionString))
            using (var command = new SqlCommand(
                "SELECT r.Name FROM dbo.AppRoles r " +
                "INNER JOIN dbo.AppUserRoles ur ON ur.RoleId = r.Id " +
                "INNER JOIN dbo.AppUsers u ON u.Id = ur.UserId " +
                "WHERE u.UserName = @UserName", connection))
            {
                command.Parameters.AddWithValue("@UserName", username);
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read()) roles.Add(reader.GetString(0));
                }
            }
            return roles.ToArray();
        }

        public override bool IsUserInRole(string username, string roleName)
        {
            return GetRolesForUser(username).Contains(roleName, StringComparer.OrdinalIgnoreCase);
        }

        public override string[] GetAllRoles()
        {
            return new[] { "BillingAdmin", "BillingReviewer", "ScheduleEditor" };
        }

        public override bool RoleExists(string roleName)
        {
            return GetAllRoles().Contains(roleName, StringComparer.OrdinalIgnoreCase);
        }

        public override void CreateRole(string roleName) { throw new NotSupportedException(); }
        public override bool DeleteRole(string roleName, bool throwOnPopulatedRole) { throw new NotSupportedException(); }
        public override void AddUsersToRoles(string[] usernames, string[] roleNames) { throw new NotSupportedException(); }
        public override void RemoveUsersFromRoles(string[] usernames, string[] roleNames) { throw new NotSupportedException(); }
        public override string[] GetUsersInRole(string roleName) { throw new NotSupportedException(); }
        public override string[] FindUsersInRole(string roleName, string usernameToMatch) { throw new NotSupportedException(); }
    }
}
