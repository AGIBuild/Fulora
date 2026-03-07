using Agibuild.Fulora;
using Agibuild.Fulora.AI;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class AiPayloadTests
{
    [Fact]
    public void Store_and_fetch_returns_payload()
    {
        using var store = new InMemoryAiPayloadStore();
        var payload = new AiMediaPayload
        {
            Data = [1, 2, 3],
            MimeType = "image/png",
            Name = "test.png"
        };

        var blobId = store.Store(payload);
        var fetched = store.Fetch(blobId);

        Assert.NotNull(fetched);
        Assert.Equal(payload.Data, fetched.Data);
        Assert.Equal("image/png", fetched.MimeType);
    }

    [Fact]
    public void Fetch_returns_null_for_unknown()
    {
        using var store = new InMemoryAiPayloadStore();
        Assert.Null(store.Fetch("nonexistent"));
    }

    [Fact]
    public void Fetch_returns_null_after_expiry()
    {
        using var store = new InMemoryAiPayloadStore();
        var payload = new AiMediaPayload { Data = [1], MimeType = "test/plain" };

        var blobId = store.Store(payload, ttl: TimeSpan.FromMilliseconds(1));
        Thread.Sleep(10);
        var fetched = store.Fetch(blobId);

        Assert.Null(fetched);
    }

    [Fact]
    public void Remove_deletes_blob()
    {
        using var store = new InMemoryAiPayloadStore();
        var payload = new AiMediaPayload { Data = [1, 2], MimeType = "audio/wav" };
        var blobId = store.Store(payload);

        Assert.True(store.Remove(blobId));
        Assert.Null(store.Fetch(blobId));
    }

    [Fact]
    public void EvictExpired_removes_expired_blobs()
    {
        using var store = new InMemoryAiPayloadStore();
        store.Store(new AiMediaPayload { Data = [1], MimeType = "a/b" }, TimeSpan.FromMilliseconds(1));
        store.Store(new AiMediaPayload { Data = [2], MimeType = "c/d" }, TimeSpan.FromHours(1));
        Thread.Sleep(10);

        var evicted = store.EvictExpired();

        Assert.Equal(1, evicted);
    }

    [Fact]
    public void Router_uses_inline_for_small_payload()
    {
        using var store = new InMemoryAiPayloadStore();
        var router = new AiPayloadRouter(store, thresholdBytes: 1024);
        var payload = new AiMediaPayload { Data = new byte[100], MimeType = "image/png" };

        var result = router.Route(payload);

        Assert.True(result.IsInline);
        Assert.StartsWith("data:image/png;base64,", result.Value);
        Assert.Null(result.BlobId);
    }

    [Fact]
    public void Router_uses_blob_store_for_large_payload()
    {
        using var store = new InMemoryAiPayloadStore();
        var router = new AiPayloadRouter(store, thresholdBytes: 100);
        var payload = new AiMediaPayload { Data = new byte[200], MimeType = "image/jpeg" };

        var result = router.Route(payload);

        Assert.False(result.IsInline);
        Assert.StartsWith("app://ai/blob/", result.Value);
        Assert.NotNull(result.BlobId);

        var fetched = store.Fetch(result.BlobId);
        Assert.NotNull(fetched);
    }

    [Fact]
    public void Router_boundary_exactly_at_threshold_is_inline()
    {
        using var store = new InMemoryAiPayloadStore();
        var router = new AiPayloadRouter(store, thresholdBytes: 100);
        var payload = new AiMediaPayload { Data = new byte[100], MimeType = "text/plain" };

        var result = router.Route(payload);

        Assert.True(result.IsInline);
    }
}
