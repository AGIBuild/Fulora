namespace Agibuild.Fulora.Adapters.Abstractions;

internal interface IWebViewPlatformProvider
{
    string Id { get; }

    int Priority { get; }

    bool CanHandleCurrentPlatform();

    IWebViewAdapter CreateAdapter();
}
