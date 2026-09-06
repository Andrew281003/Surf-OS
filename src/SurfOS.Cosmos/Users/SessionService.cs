namespace SurfOS.Users
{
    public sealed class SessionService
    {
        private readonly UserService _users;

        public SessionService(UserService users)
        {
            _users = users;
        }

        public UserAccount CurrentUser { get; private set; }
        public bool IsAuthenticated { get { return CurrentUser != null; } }

        public bool Login(string username, string password)
        {
            UserAccount account;
            if (!_users.Authenticate(username, password, out account))
            {
                return false;
            }
            CurrentUser = account;
            return true;
        }

        public void Logout()
        {
            CurrentUser = null;
        }
    }
}
