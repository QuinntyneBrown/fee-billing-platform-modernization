using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web.Security;

namespace FeeBilling.Web.Security
{
    /// <summary>
    /// Custom membership over dbo.AppUsers. Only ValidateUser is really used (login page);
    /// users are created by a support script.
    /// </summary>
    public class FeeBillingMembershipProvider : MembershipProvider
    {
        private static string ConnectionString
        {
            get { return ConfigurationManager.ConnectionStrings["FeeBillingAdo"].ConnectionString; }
        }

        public override string ApplicationName { get; set; }

        public override bool ValidateUser(string username, string password)
        {
            using (var connection = new SqlConnection(ConnectionString))
            using (var command = new SqlCommand("dbo.usp_ValidateUser", connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@UserName", username);
                connection.Open();

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return false;
                    if ((bool)reader["IsLockedOut"]) return false;

                    return PasswordHasher.Verify((string)reader["PasswordSalt"], password, (string)reader["PasswordHash"]);
                }
            }
        }

        public override MembershipUser GetUser(string username, bool userIsOnline)
        {
            using (var connection = new SqlConnection(ConnectionString))
            using (var command = new SqlCommand("SELECT Id, UserName, Email, IsLockedOut, CreatedOn, LastLoginOn FROM dbo.AppUsers WHERE UserName = @UserName", connection))
            {
                command.Parameters.AddWithValue("@UserName", username);
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    var created = (DateTime)reader["CreatedOn"];
                    var lastLogin = reader["LastLoginOn"] as DateTime? ?? created;
                    return new MembershipUser(Name, (string)reader["UserName"], reader["Id"], reader["Email"] as string,
                        null, null, true, (bool)reader["IsLockedOut"], created, lastLogin, lastLogin, created, DateTime.MinValue);
                }
            }
        }

        public override bool EnablePasswordRetrieval { get { return false; } }
        public override bool EnablePasswordReset { get { return false; } }
        public override bool RequiresQuestionAndAnswer { get { return false; } }
        public override int MaxInvalidPasswordAttempts { get { return int.MaxValue; } }   // no lockout
        public override int PasswordAttemptWindow { get { return 0; } }
        public override bool RequiresUniqueEmail { get { return false; } }
        public override MembershipPasswordFormat PasswordFormat { get { return MembershipPasswordFormat.Hashed; } }
        public override int MinRequiredPasswordLength { get { return 6; } }
        public override int MinRequiredNonAlphanumericCharacters { get { return 0; } }
        public override string PasswordStrengthRegularExpression { get { return string.Empty; } }

        public override MembershipUser CreateUser(string username, string password, string email, string passwordQuestion, string passwordAnswer, bool isApproved, object providerUserKey, out MembershipCreateStatus status) { throw new NotSupportedException(); }
        public override bool ChangePasswordQuestionAndAnswer(string username, string password, string newPasswordQuestion, string newPasswordAnswer) { throw new NotSupportedException(); }
        public override string GetPassword(string username, string answer) { throw new NotSupportedException(); }
        public override bool ChangePassword(string username, string oldPassword, string newPassword) { throw new NotSupportedException(); }
        public override string ResetPassword(string username, string answer) { throw new NotSupportedException(); }
        public override void UpdateUser(MembershipUser user) { throw new NotSupportedException(); }
        public override bool UnlockUser(string userName) { throw new NotSupportedException(); }
        public override MembershipUser GetUser(object providerUserKey, bool userIsOnline) { throw new NotSupportedException(); }
        public override string GetUserNameByEmail(string email) { throw new NotSupportedException(); }
        public override bool DeleteUser(string username, bool deleteAllRelatedData) { throw new NotSupportedException(); }
        public override MembershipUserCollection GetAllUsers(int pageIndex, int pageSize, out int totalRecords) { throw new NotSupportedException(); }
        public override int GetNumberOfUsersOnline() { throw new NotSupportedException(); }
        public override MembershipUserCollection FindUsersByName(string usernameToMatch, int pageIndex, int pageSize, out int totalRecords) { throw new NotSupportedException(); }
        public override MembershipUserCollection FindUsersByEmail(string emailToMatch, int pageIndex, int pageSize, out int totalRecords) { throw new NotSupportedException(); }
    }
}
