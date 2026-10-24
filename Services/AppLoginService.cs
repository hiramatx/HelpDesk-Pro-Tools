using System;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Security.Principal;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>The user logged in to the app (the login box), and whether they are in Domain Admins.</summary>
/// <param name="LoginName">What was typed in the login box (jdoe, DOMAIN\jdoe or jdoe@corp.local), used to log in again.</param>
/// <param name="UserName">The account's sAMAccountName, shown in the header.</param>
public sealed record AppLogin(string LoginName, string UserName, bool IsDomainAdmin);

/// <summary>Login failed. <see cref="WrongPassword"/> is false when the domain couldn't be reached.</summary>
public sealed class AppLoginException(string message, bool wrongPassword) : Exception(message)
{
    public bool WrongPassword { get; } = wrongPassword;
}

/// <summary>
/// Checks a user name and password against Active Directory and whether that user is a member of
/// Domain Admins (directly or through nested groups).
///
/// This is only the app login: the remote tools still run as the Windows account the app runs as.
/// </summary>
public static class AppLoginService
{
    /// <summary>Blocking (talks to a domain controller): call it off the UI thread.</summary>
    public static AppLogin LogIn(string loginName, string password)
    {
        loginName = loginName.Trim();
        if (loginName.Length == 0) throw new AppLoginException("Enter your user name.", wrongPassword: true);
        if (password.Length == 0) throw new AppLoginException("Enter your password.", wrongPassword: true);

        // jdoe          -> this PC's domain, sAMAccountName jdoe
        // CORP\jdoe     -> domain CORP,       sAMAccountName jdoe
        // jdoe@corp.com -> domain corp.com,   UPN jdoe@corp.com
        string? domain = null;
        var name = loginName;
        var identityType = IdentityType.SamAccountName;
        var slash = loginName.IndexOf('\\');
        if (slash > 0)
        {
            domain = loginName[..slash];
            name = loginName[(slash + 1)..];
        }
        else if (loginName.Contains('@'))
        {
            domain = loginName[(loginName.LastIndexOf('@') + 1)..];
            identityType = IdentityType.UserPrincipalName;
        }

        try
        {
            using (var context = new PrincipalContext(ContextType.Domain, domain))
            {
                if (!context.ValidateCredentials(name, password, ContextOptions.Negotiate | ContextOptions.Signing | ContextOptions.Sealing))
                    throw new AppLoginException("The user name or password is incorrect.", wrongPassword: true);
            }

            // Look the user up with their own credentials, so it works whatever account the app runs as.
            using var userContext = new PrincipalContext(ContextType.Domain, domain, loginName, password);
            using var user = UserPrincipal.FindByIdentity(userContext, identityType, name)
                             ?? throw new AppLoginException($"{loginName} was not found in Active Directory.", wrongPassword: true);

            return new AppLogin(loginName, user.SamAccountName ?? name, IsDomainAdmin(user));
        }
        catch (AppLoginException)
        {
            throw;
        }
        catch (PrincipalServerDownException)
        {
            throw new AppLoginException(
                domain is null ? "Could not reach a domain controller. Is this PC on the network?" : $"Could not reach the {domain} domain.",
                wrongPassword: false);
        }
        catch (Exception ex)
        {
            throw new AppLoginException($"Login failed: {ex.Message}", wrongPassword: false);
        }
    }

    /// <summary>
    /// Domain Admins is matched by its SID (domain SID + 512), so it works when the group is renamed or
    /// the domain isn't in English. tokenGroups holds every group the user is in, nested ones included.
    /// </summary>
    private static bool IsDomainAdmin(UserPrincipal user)
    {
        if (user.Sid?.AccountDomainSid is not { } domainSid) return false;
        var domainAdmins = new SecurityIdentifier(WellKnownSidType.AccountDomainAdminsSid, domainSid);

        var entry = (DirectoryEntry)user.GetUnderlyingObject();
        entry.RefreshCache(new[] { "tokenGroups" });
        return entry.Properties["tokenGroups"]
            .OfType<byte[]>()
            .Any(sid => new SecurityIdentifier(sid, 0) == domainAdmins);
    }
}
