using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BollaImpianto.Web.Services;

/// <summary>
/// Consente l'accesso con il cookie di sessione (utente a video) oppure con una API key
/// nell'header X-Api-Key (sistemi esterni). A differenza di [Authorize] risponde 401 invece
/// di reindirizzare alla pagina di login, cosa inutilizzabile da un client non browser.
/// </summary>
public sealed class ApiKeyOrCookieAttribute : Attribute, IAuthorizationFilter
{
    public const string HeaderName = "X-Api-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            return;
        }

        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (!string.IsNullOrWhiteSpace(provided))
        {
            var configured = context.HttpContext.RequestServices
                .GetRequiredService<IConfiguration>()
                .GetSection("Api:Keys")
                .Get<string[]>() ?? [];

            var providedBytes = Encoding.UTF8.GetBytes(provided);
            foreach (var key in configured)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (CryptographicOperations.FixedTimeEquals(providedBytes, Encoding.UTF8.GetBytes(key)))
                {
                    return;
                }
            }
        }

        context.Result = new UnauthorizedObjectResult(new
        {
            errore = $"Autenticazione richiesta: cookie di sessione oppure header {HeaderName}."
        });
    }
}
