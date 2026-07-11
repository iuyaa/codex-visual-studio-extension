using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CodexVsix.Services;
using Xunit;

namespace CodexVsix.Tests;

public sealed class NewtonsoftJsonCompatibilityTests
{
    [Fact]
    public void SerializesCompactAndIndentedJsonAgainstTheVisualStudioCompatibleApi()
    {
        var token = JObject.Parse(@"{ ""value"": 1 }");

        var compact = NewtonsoftJsonCompatibility.Serialize(token, Formatting.None);
        var indented = NewtonsoftJsonCompatibility.Serialize(token, Formatting.Indented);

        Assert.Equal(@"{""value"":1}", compact);
        Assert.Contains("\"value\": 1", indented);
    }
}
