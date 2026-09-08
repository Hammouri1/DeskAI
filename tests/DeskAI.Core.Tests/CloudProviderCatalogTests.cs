using DeskAI.Core.Ai;

namespace DeskAI.Core.Tests;

public sealed class CloudProviderCatalogTests
{
    [Fact]
    public void EveryProviderUsesAPlainHttpsAddress()
    {
        Assert.NotEmpty(CloudProviderCatalog.All);
        foreach (var provider in CloudProviderCatalog.All)
        {
            Assert.Equal(Uri.UriSchemeHttps, provider.ChatCompletionsEndpoint.Scheme);
            Assert.Empty(provider.ChatCompletionsEndpoint.UserInfo);
            Assert.Empty(provider.ChatCompletionsEndpoint.Query);
            Assert.Empty(provider.ChatCompletionsEndpoint.Fragment);
            Assert.True(provider.ChatCompletionsEndpoint.IsDefaultPort);
            Assert.False(provider.ChatCompletionsEndpoint.IsLoopback);
        }
    }

    [Fact]
    public void ProviderIdentitiesAreUnique()
    {
        Assert.Equal(
            CloudProviderCatalog.All.Count,
            CloudProviderCatalog.All.Select(provider => provider.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            CloudProviderCatalog.All.Count,
            CloudProviderCatalog.All.Select(provider => provider.DisplayName).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EachProviderKeepsItsOwnCredentialEntryAndItsOwnHost()
    {
        // A shared credential reference would let a key saved for one company be sent to
        // another; a shared host would make the choice meaningless.
        Assert.Equal(
            CloudProviderCatalog.All.Count,
            CloudProviderCatalog.All.Select(provider => provider.CredentialReference)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            CloudProviderCatalog.All.Count,
            CloudProviderCatalog.All.Select(provider => provider.ChatCompletionsEndpoint.Host)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryCredentialReferenceStaysUnderTheDeskAiNamespace()
    {
        Assert.All(
            CloudProviderCatalog.All,
            provider => Assert.StartsWith("DeskAI/", provider.CredentialReference, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("openrouter ")]
    [InlineData("OpenRouter")]
    [InlineData("some-service-deskai-does-not-know")]
    public void FindRejectsAnythingOutsideTheAllowList(string? providerId)
    {
        Assert.Null(CloudProviderCatalog.Find(providerId));
        Assert.False(CloudProviderCatalog.IsKnown(providerId));
    }

    [Fact]
    public void FindReturnsAKnownProvider()
    {
        var provider = CloudProviderCatalog.Find("openrouter");

        Assert.NotNull(provider);
        Assert.Equal("OpenRouter", provider.DisplayName);
        Assert.Equal("openrouter.ai", provider.ChatCompletionsEndpoint.Host);
        Assert.True(CloudProviderCatalog.IsKnown("openrouter"));
    }

    [Theory]
    [InlineData("http://api.example.com/v1/chat/completions")]
    [InlineData("https://user:secret@api.example.com/v1/chat/completions")]
    [InlineData("https://api.example.com/v1/chat/completions?key=abc")]
    [InlineData("https://api.example.com/v1/chat/completions#fragment")]
    [InlineData("https://127.0.0.1/v1/chat/completions")]
    [InlineData("https://api.example.com:8443/v1/chat/completions")]
    [InlineData("not-a-url")]
    public void CreateRefusesAnAddressThatIsNotAPlainHttpsDestination(string endpoint)
    {
        // Create is internal, so this exercises the guard through a catalog-shaped call.
        var failure = Record.Exception(() => InvokeCreate(endpoint));

        Assert.IsType<ArgumentException>(failure);
    }

    private static void InvokeCreate(string endpoint)
    {
        var method = typeof(CloudProvider).GetMethod(
            "Create",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CloudProvider.Create was not found.");
        try
        {
            method.Invoke(null, ["test", "Test", endpoint, "DeskAI/Test", "hint", "example.com"]);
        }
        catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }
}
