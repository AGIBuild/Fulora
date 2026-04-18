using System.Net.WebSockets;
using System.Text.Json;
using Agibuild.Fulora;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

[Collection(StatefulIOCollection.Name)]
public sealed class BridgeDebugServerTests
{
    [Fact]
    public void Server_starts_and_stops_without_error()
    {
        var port = GetAvailablePort();
        var server = new BridgeDebugServer(port);
        server.Start();
        server.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public void IBridgeTracer_events_do_not_throw_when_no_clients_connected()
    {
        var port = GetAvailablePort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var server = new BridgeDebugServer(port);
        server.Start();

        try
        {
            server.OnExportCallStart("Svc", "Method", "{}");
            server.OnExportCallEnd("Svc", "Method", 10, "string");
            server.OnExportCallError("Svc", "Method", 5, new InvalidOperationException("test"));
            server.OnImportCallStart("Svc", "Method", "{}");
            server.OnImportCallEnd("Svc", "Method", 8);
            server.OnServiceExposed("Svc", 3, true);
            server.OnServiceRemoved("Svc");
        }
        finally
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void Service_registry_tracks_exposed_services()
    {
        var port = GetAvailablePort();
        var server = new BridgeDebugServer(port);
        server.Start();

        try
        {
            server.OnServiceExposed("ServiceA", 5, true);
            server.OnServiceExposed("ServiceB", 2, false);
            server.OnServiceExposed("ServiceA", 6, true); // update

            // Connect a client and verify we receive service-registry
            using var client = new ClientWebSocket();
            client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token).GetAwaiter().GetResult();

            var buffer = new byte[4096];
            var segment = new ArraySegment<byte>(buffer);
            var result = client.ReceiveAsync(segment, cts.Token).GetAwaiter().GetResult();
            var json = System.Text.Encoding.UTF8.GetString(buffer.AsSpan(0, result.Count));
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var type = doc.RootElement.GetProperty("type").GetString();
            Assert.Equal("service-registry", type);

            var services = doc.RootElement.GetProperty("services");
            Assert.Equal(2, services.GetArrayLength()); // ServiceA (updated), ServiceB

            var names = services.EnumerateArray()
                .Select(s => s.GetProperty("serviceName").GetString())
                .OrderBy(x => x)
                .ToList();
            Assert.Equal(["ServiceA", "ServiceB"], names);

            var serviceA = services.EnumerateArray().First(s => s.GetProperty("serviceName").GetString() == "ServiceA");
            Assert.Equal(6, serviceA.GetProperty("methodCount").GetInt32());

            server.OnServiceRemoved("ServiceA");
            // Next message would be service-registry update - we've verified the tracking logic
        }
        finally
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
