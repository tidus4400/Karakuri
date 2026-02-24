using tidus4400.Karakuri.Shared;

namespace tidus4400.Karakuri.Web.Services;

public sealed class ApiSessionState
{
    private AuthUserDto? _currentUser;

    public event Action? Changed;

    public AuthUserDto? CurrentUser => _currentUser;
    public bool IsAuthenticated => _currentUser is not null;
    public bool IsAdmin => string.Equals(_currentUser?.Role, "Admin", StringComparison.OrdinalIgnoreCase);

    public void SetUser(AuthUserDto? user)
    {
        _currentUser = user;
        Changed?.Invoke();
    }
}
