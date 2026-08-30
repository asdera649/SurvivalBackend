using Microsoft.AspNetCore.Mvc;

namespace SurvivalBackend.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireApiKeyAttribute : TypeFilterAttribute
{
    public RequireApiKeyAttribute(ApiKeyRole role) : base(typeof(ApiKeyAuthorizationFilter))
    {
        Arguments = [role];
    }
}
