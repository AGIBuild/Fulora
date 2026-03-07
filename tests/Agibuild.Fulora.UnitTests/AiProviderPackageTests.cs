using Agibuild.Fulora.AI;
using Agibuild.Fulora.AI.Ollama;
using Agibuild.Fulora.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class AiProviderPackageTests
{
    [Fact]
    public void AddOllama_registers_named_chat_client()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddOllama("local", new Uri("http://localhost:11434"), "llama3");
        });
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IAiProviderRegistry>();
        var client = registry.GetChatClient("local");

        Assert.NotNull(client);
    }

    [Fact]
    public void AddOllama_uses_default_endpoint_when_not_specified()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddOllama("default"));
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IAiProviderRegistry>();
        var client = registry.GetChatClient("default");
        Assert.NotNull(client);
    }

    [Fact]
    public void AddOllama_registered_as_default_when_first()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddOllama("local"));
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IAiProviderRegistry>();
        var defaultClient = registry.GetChatClient();
        var namedClient = registry.GetChatClient("local");

        Assert.Same(defaultClient, namedClient);
    }

    [Fact]
    public void AddOpenAI_registers_named_chat_client()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddOpenAI("cloud", "test-api-key", "gpt-4o-mini");
        });
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IAiProviderRegistry>();
        var client = registry.GetChatClient("cloud");

        Assert.NotNull(client);
    }

    [Fact]
    public void AddOpenAI_with_custom_endpoint_registers_client()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddOpenAI("azure", "test-key", "gpt-4o",
                endpoint: new Uri("https://custom.openai.azure.com/"));
        });
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IAiProviderRegistry>();
        var client = registry.GetChatClient("azure");
        Assert.NotNull(client);
    }

    [Fact]
    public void AddOpenAI_default_model_is_gpt4o_mini()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddOpenAI("cloud", "test-key"));
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IAiProviderRegistry>();
        var client = registry.GetChatClient("cloud");
        Assert.NotNull(client);
    }

    [Fact]
    public void Multiple_providers_coexist()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddOllama("local");
            ai.AddOpenAI("cloud", "test-key");
        });
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IAiProviderRegistry>();

        var local = registry.GetChatClient("local");
        var cloud = registry.GetChatClient("cloud");

        Assert.NotNull(local);
        Assert.NotNull(cloud);
        Assert.NotSame(local, cloud);
    }

    [Fact]
    public void Ollama_appears_in_provider_names()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddOllama("my-ollama"));
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IAiProviderRegistry>();
        Assert.Contains("my-ollama", registry.ChatClientNames);
    }

    [Fact]
    public void OpenAI_appears_in_provider_names()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddOpenAI("my-openai", "test-key"));
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IAiProviderRegistry>();
        Assert.Contains("my-openai", registry.ChatClientNames);
    }
}
