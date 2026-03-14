using Agibuild.Fulora;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class SharedStateStoreTests
{
    private readonly SharedStateStore _store = new();

    [Fact]
    public void SetValue_And_GetValue_ReturnsValue()
    {
        _store.SetValue("theme", "{\"dark\":true}");
        Assert.Equal("{\"dark\":true}", _store.GetValue("theme"));
    }

    [Fact]
    public void GetValue_NonExistentKey_ReturnsNull()
    {
        Assert.Null(_store.GetValue("missing"));
    }

    [Fact]
    public void TryGet_ExistingKey_ReturnsTrueWithValue()
    {
        _store.SetValue("key", "value");
        Assert.True(_store.TryGet("key", out var value));
        Assert.Equal("value", value);
    }

    [Fact]
    public void TryGet_NonExistentKey_ReturnsFalse()
    {
        Assert.False(_store.TryGet("missing", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void Remove_ExistingKey_ReturnsTrue()
    {
        _store.SetValue("key", "value");
        Assert.True(_store.Remove("key"));
        Assert.Null(_store.GetValue("key"));
    }

    [Fact]
    public void Remove_NonExistentKey_ReturnsFalse()
    {
        Assert.False(_store.Remove("missing"));
    }

    [Fact]
    public void Remove_FiresStateChanged()
    {
        _store.SetValue("key", "value");
        StateChange? args = null;
        _store.StateChanged += (_, e) => args = e;

        _store.Remove("key");

        Assert.NotNull(args);
        Assert.Equal("key", args.Key);
        Assert.Equal("value", args.OldValue);
        Assert.Null(args.NewValue);
    }

    [Fact]
    public void SetValue_FiresStateChanged()
    {
        StateChange? args = null;
        _store.StateChanged += (_, e) => args = e;

        _store.SetValue("key", "value");

        Assert.NotNull(args);
        Assert.Equal("key", args.Key);
        Assert.Null(args.OldValue);
        Assert.Equal("value", args.NewValue);
    }

    [Fact]
    public void SetValue_SameValue_DoesNotFireStateChanged()
    {
        _store.SetValue("key", "value");
        var eventCount = 0;
        _store.StateChanged += (_, _) => eventCount++;

        _store.SetValue("key", "value");

        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void SetValue_DifferentValue_FiresStateChanged()
    {
        _store.SetValue("key", "old");
        StateChange? args = null;
        _store.StateChanged += (_, e) => args = e;

        _store.SetValue("key", "new");

        Assert.NotNull(args);
        Assert.Equal("old", args.OldValue);
        Assert.Equal("new", args.NewValue);
    }

    [Fact]
    public void LWW_LaterTimestampWins()
    {
        var t1 = DateTimeOffset.UtcNow;
        var t2 = t1.AddSeconds(1);

        _store.SetValue("key", "first", t1);
        _store.SetValue("key", "second", t2);

        Assert.Equal("second", _store.GetValue("key"));
    }

    [Fact]
    public void LWW_StaleWriteIgnored()
    {
        var t1 = DateTimeOffset.UtcNow;
        var t2 = t1.AddSeconds(-1);

        _store.SetValue("key", "fresh", t1);
        _store.SetValue("key", "stale", t2);

        Assert.Equal("fresh", _store.GetValue("key"));
    }

    [Fact]
    public void LWW_StaleWrite_DoesNotFireStateChanged()
    {
        var t1 = DateTimeOffset.UtcNow;
        _store.SetValue("key", "fresh", t1);

        var eventCount = 0;
        _store.StateChanged += (_, _) => eventCount++;

        _store.SetValue("key", "stale", t1.AddSeconds(-1));

        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void GetSnapshot_ReturnsAllEntries()
    {
        _store.SetValue("a", "1");
        _store.SetValue("b", "2");
        _store.SetValue("c", "3");

        var snapshot = _store.GetSnapshot();

        Assert.Equal(3, snapshot.Count);
        Assert.Equal("1", snapshot["a"]);
        Assert.Equal("2", snapshot["b"]);
        Assert.Equal("3", snapshot["c"]);
    }

    [Fact]
    public void GetSnapshot_IsImmutable()
    {
        _store.SetValue("key", "before");
        var snapshot = _store.GetSnapshot();

        _store.SetValue("key", "after");

        Assert.Equal("before", snapshot["key"]);
    }

    [Fact]
    public void SetValueGeneric_And_GetValueGeneric_RoundTrips()
    {
        var prefs = new TestPrefs { Theme = "dark", FontSize = 14 };
        _store.SetValue("prefs", prefs);

        var result = _store.GetValue<TestPrefs>("prefs");

        Assert.NotNull(result);
        Assert.Equal("dark", result.Theme);
        Assert.Equal(14, result.FontSize);
    }

    [Fact]
    public void GetValueGeneric_NonExistentKey_ReturnsDefault()
    {
        var result = _store.GetValue<TestPrefs>("missing");
        Assert.Null(result);
    }

    [Fact]
    public void SetValue_NullKey_ThrowsArgument()
    {
        Assert.Throws<ArgumentNullException>(() => _store.SetValue(null!, "value"));
    }

    [Fact]
    public void GetValue_NullKey_ThrowsArgument()
    {
        Assert.Throws<ArgumentNullException>(() => _store.GetValue(null!));
    }

    [Fact]
    public void Remove_NullKey_ThrowsArgument()
    {
        Assert.Throws<ArgumentNullException>(() => _store.Remove(null!));
    }

    [Fact]
    public void SetValue_NullValue_Allowed()
    {
        _store.SetValue("key", (string?)null);
        Assert.True(_store.TryGet("key", out var val));
        Assert.Null(val);
    }

    private sealed class TestPrefs
    {
        public string? Theme { get; set; }
        public int FontSize { get; set; }
    }
}
