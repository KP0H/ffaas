namespace FfaasLite.Api.Security;

public static class AuthConstants
{
    public static class Schemes
    {
        public const string ApiKey = "ApiKey";
    }

    public static class Roles
    {
        public const string Reader = "Reader";
        public const string Editor = "Editor";
    }

    public static class Policies
    {
        public const string Reader = "reader";
        public const string Editor = "editor";
    }
}
