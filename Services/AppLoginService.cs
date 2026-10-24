using System;
using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Security.Principal;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Login failed. <see cref="WrongPassword"/> means a saved login should be forgotten.</summary>
public sealed class AppLoginException(string message, bool wrongPassword) : Exception(message)
{
    public bool WrongPassword { get; } = wrongPassword;
}

/// <summary>
/// The login box's work: start the app again as the account typed in (what "Run as different user"
/// does), and check whether the account the app runs as is in Domain Admins.
/// </summary>
public static class AppLoginService
{
    /// <summary>Passed to the copy started by <see cref="LaunchAs"/>, so it opens the main window straight away.</summary>
    public const string LoggedInArgument = "--logged-in";

    /// <summary>jdoe -> (jdoe, null), CORP\jdoe -> (jdoe, CORP), jdoe@corp.local -> (jdoe@corp.local, null).</summary>
    public static (string User, string? Domain) Parse(string loginName)
    {
        loginName = loginName.Trim();
        var slash = loginName.IndexOf('\\');
        return slash > 0 ? (loginName[(slash + 1)..], loginName[..slash]) : (loginName, null);
    }

    /// <summary>True when the login is the account this app already runs as (then there is nothing to restart).</summary>
    public static bool IsCurrentUser(string loginName)
    {
        var (user, domain) = Parse(loginName);
        var at = user.IndexOf('@');
        if (at > 0) user = user[..at];
        return string.Equals(user, Environment.UserName, StringComparison.OrdinalIgnoreCase)
               && (domain is null || string.Equals(domain, Environment.UserDomainName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Starts this exe as <paramref name="loginName"/> (CreateProcessWithLogonW, the same thing "Run as
    /// different user" does, with that user's profile loaded). Windows checks the password; cached
    /// credentials work when no domain controller can be reached. Blocking: call it off the UI thread.
    /// </summary>
    public static void LaunchAs(string loginName, string password)
    {
        var (user, domain) = Parse(loginName);
        if (user.Length == 0) throw new AppLoginException("Enter your user name.", wrongPassword: false);
        if (password.Length == 0) throw new AppLoginException("Enter your password.", wrongPassword: false);

        var exe = Environment.ProcessPath ?? throw new AppLoginException("Could not find the app's exe.", wrongPassword: false);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            UserName = user,
            Domain = domain,
            PasswordInClearText = password,
            LoadUserProfile = true, // %AppData% settings, Credential Manager etc. of that user
            WorkingDirectory = AppContext.BaseDirectory,
        };
        psi.ArgumentList.Add(LoggedInArgument);

        try
        {
            Process.Start(psi)?.Dispose();
        }
        catch (Win32Exception ex)
        {
            throw ex.NativeErrorCode switch
            {
                ERROR_LOGON_FAILURE => new AppLoginException("The user name or password is incorrect.", wrongPassword: true),
                ERROR_PASSWORD_EXPIRED or ERROR_PASSWORD_MUST_CHANGE =>
                    new AppLoginException("The password has expired. Change it, then log in again.", wrongPassword: true),
                ERROR_ACCOUNT_LOCKED_OUT => new AppLoginException("The account is locked out.", wrongPassword: true),
                ERROR_ACCOUNT_DISABLED => new AppLoginException("The account is disabled.", wrongPassword: true),
                ERROR_DIRECTORY => new AppLoginException(
                    $"{loginName} can't open the app's folder ({AppContext.BaseDirectory}). Give the account access or copy the app locally.",
                    wrongPassword: false),
                _ => new AppLoginException($"Could not start the app as {loginName}: {ex.Message}", wrongPassword: false),
            };
        }
    }

    /// <summary>
    /// Whether the account this app runs as is in Domain Admins, directly or through nested groups.
    /// Asks AD, because with UAC the Domain Admins group in the app's own token is deny-only and looks absent.
    /// Blocking: call it off the UI thread.
    /// </summary>
    public static bool IsCurrentUserDomainAdmin()
    {
        var sid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No user SID.");
        if (sid.AccountDomainSid is not { } domainSid) return false; // local account
        var domainAdmins = new SecurityIdentifier(WellKnownSidType.AccountDomainAdminsSid, domainSid);

        try
        {
            using var context = new PrincipalContext(ContextType.Domain);
            using var user = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid.Value);
            if (user is null) return false;

            // tokenGroups holds every group the user is in, nested ones included. Domain Admins is matched
            // by its SID (domain SID + 512), so it works when the group is renamed or not in English.
            var entry = (DirectoryEntry)user.GetUnderlyingObject();
            entry.RefreshCache(new[] { "tokenGroups" });
            return entry.Properties["tokenGroups"]
                .OfType<byte[]>()
                .Any(g => new SecurityIdentifier(g, 0) == domainAdmins);
        }
        catch (PrincipalServerDownException)
        {
            throw new AppLoginException("Could not reach a domain controller to check Domain Admins.", wrongPassword: false);
        }
    }

    private const int ERROR_DIRECTORY = 267;
    private const int ERROR_LOGON_FAILURE = 1326;
    private const int ERROR_ACCOUNT_DISABLED = 1331;
    private const int ERROR_PASSWORD_EXPIRED = 1330;
    private const int ERROR_ACCOUNT_LOCKED_OUT = 1909;
    private const int ERROR_PASSWORD_MUST_CHANGE = 1907;
}
