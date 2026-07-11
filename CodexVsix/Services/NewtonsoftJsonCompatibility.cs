using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

internal static class NewtonsoftJsonCompatibility
{
    internal static string Serialize(JToken token, Formatting formatting)
    {
        if (token is null)
        {
            throw new ArgumentNullException(nameof(token));
        }

        // Visual Studio 18 currently hosts Newtonsoft.Json 13.0.3. The single-argument
        // JToken.ToString(Formatting) overload was added in 13.0.4, while this overload
        // is available in both versions and avoids a MissingMethodException in devenv.exe.
        return token.ToString(formatting, Array.Empty<JsonConverter>());
    }
}
