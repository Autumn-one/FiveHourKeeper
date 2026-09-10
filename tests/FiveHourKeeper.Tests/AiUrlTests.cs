using FiveHourKeeper.Models;
using FiveHourKeeper.Services;

namespace FiveHourKeeper.Tests;

public class AiUrlTests
{
    [Theory]
    [InlineData("https://api.openai.com", "https://api.openai.com/models")]
    [InlineData("https://api.openai.com/", "https://api.openai.com/models")]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/models")]
    [InlineData("https://api.openai.com/v1/", "https://api.openai.com/v1/models")]
    public void OpenAi_ModelsEndpoint(string baseUrl, string expected)
    {
        var u = AiUrl.NormalizeBase(baseUrl);
        Assert.Equal(expected, AiUrl.EnsureModelsEndpoint(u, AiProtocol.OpenAiCompatible).AbsoluteUri);
    }

    [Theory]
    [InlineData("https://api.anthropic.com", "https://api.anthropic.com/v1/models")]
    [InlineData("https://api.anthropic.com/", "https://api.anthropic.com/v1/models")]
    [InlineData("https://api.anthropic.com/v1", "https://api.anthropic.com/v1/models")]
    public void Anthropic_ModelsEndpoint(string baseUrl, string expected)
    {
        var u = AiUrl.NormalizeBase(baseUrl);
        Assert.Equal(expected, AiUrl.EnsureModelsEndpoint(u, AiProtocol.AnthropicCompatible).AbsoluteUri);
    }

    [Fact]
    public void ChatEndpoint_AlreadySet_NotDuplicated()
    {
        var raw = "https://api.example.com/v1/chat/completions";
        var u = AiUrl.NormalizeBase(raw);
        Assert.Equal(raw, AiUrl.EnsureChatEndpoint(u, AiProtocol.OpenAiCompatible).AbsoluteUri);
    }

    [Fact]
    public void AnthropicBaseWithoutV1_AppendsV1SlashMessages()
    {
        var u = AiUrl.NormalizeBase("https://api.anthropic.com");
        var ep = AiUrl.EnsureChatEndpoint(u, AiProtocol.AnthropicCompatible);
        Assert.Equal("https://api.anthropic.com/v1/messages", ep.AbsoluteUri);
    }

    [Fact]
    public void MaskKey_HidesMiddle()
    {
        Assert.Equal("sk-a…abcd", AiUrl.MaskKey("sk-abcdefghijklabcd"));
        Assert.Equal("****", AiUrl.MaskKey("abcd"));
    }
}