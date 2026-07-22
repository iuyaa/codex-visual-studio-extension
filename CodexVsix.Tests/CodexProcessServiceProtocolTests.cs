using CodexVsix.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CodexVsix.Tests;

public sealed class CodexProcessServiceProtocolTests
{
    [Fact]
    public void ScalarAppServerErrorDoesNotAttemptChildAccess()
    {
        var error = new JValue("company gateway rejected the request");

        var message = CodexProcessService.ExtractAppServerErrorMessage(error, "fallback");
        var code = CodexProcessService.ExtractAppServerErrorCode(error);

        Assert.Equal("company gateway rejected the request", message);
        Assert.Null(code);
    }

    [Fact]
    public void ObjectAppServerErrorPreservesMessageAndNumericStringCode()
    {
        var error = new JObject
        {
            ["message"] = "invalid provider response",
            ["code"] = "-32602"
        };

        var message = CodexProcessService.ExtractAppServerErrorMessage(error, "fallback");
        var code = CodexProcessService.ExtractAppServerErrorCode(error);

        Assert.Equal("invalid provider response", message);
        Assert.Equal(-32602, code);
    }

    [Fact]
    public void StructuredAppServerErrorCodeIsIgnoredInsteadOfThrowing()
    {
        var error = new JObject
        {
            ["message"] = "malformed code",
            ["code"] = new JObject { ["unexpected"] = true }
        };

        Assert.Null(CodexProcessService.ExtractAppServerErrorCode(error));
    }
}
