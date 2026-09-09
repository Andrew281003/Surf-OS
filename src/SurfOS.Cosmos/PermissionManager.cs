using System;
using SurfOS.Users;

namespace SurfOS.Security
{
    public sealed class PermissionManager
    {
        private readonly SessionService _sessions;
        private readonly AuthenticationConsole _authentication;

        public PermissionManager(SessionService sessions, AuthenticationConsole authentication)
        {
            _sessions = sessions;
            _authentication = authentication;
        }

        public void AuthorizeProtectedOperation(bool force, string operation)
        {
            if (!_sessions.IsAuthenticated || !_sessions.CurrentUser.IsAdministrator)
            {
                throw new UnauthorizedAccessException("Administrator permission is required for " + operation + ".");
            }
            if (!force)
            {
                throw new UnauthorizedAccessException("Protected operation. Repeat with --force to authenticate.");
            }
            if (!_authentication.ReauthenticateCurrentUser())
            {
                throw new UnauthorizedAccessException("Authentication failed. Operation rejected.");
            }
        }
    }
}
