using Microsoft.AspNetCore.Localization;

namespace MyDemonList.Web.Localization;

public sealed class BlazorCultureCookieProvider : CookieRequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        if (!httpContext.Request.Path.StartsWithSegments("/_blazor"))
            return NullProviderCultureResult;

        return base.DetermineProviderCultureResult(httpContext);
    }
}
