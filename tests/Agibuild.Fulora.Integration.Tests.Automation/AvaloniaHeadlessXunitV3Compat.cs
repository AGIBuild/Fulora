using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Xunit;

namespace Avalonia.Headless.XUnit;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class AvaloniaTestApplicationAttribute : Attribute
{
    public AvaloniaTestApplicationAttribute(Type appBuilderType)
    {
        AvaloniaHeadlessXunitV3Runtime.RegisterAppBuilderType(appBuilderType);
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AvaloniaFactAttribute : FactAttribute
{
    public AvaloniaFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AvaloniaTheoryAttribute : TheoryAttribute
{
    public AvaloniaTheoryAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
    }
}

public sealed class AvaloniaHeadlessFixture
{
    public AvaloniaHeadlessFixture()
    {
        AvaloniaUiThreadRunner.EnsureStarted();
    }
}

public static class AvaloniaUiThreadRunner
{
    private sealed record UiWorkItem(Action Action, TaskCompletionSource<object?> Completion);

    private static readonly BlockingCollection<UiWorkItem> Queue = [];
    private static readonly object Sync = new();
    private static Thread? _uiThread;
    private static int _started;

    public static void EnsureStarted()
    {
        if (Volatile.Read(ref _started) == 1)
            return;

        lock (Sync)
        {
            if (_started == 1)
                return;

            var ready = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _uiThread = new Thread(() =>
            {
                try
                {
                    AvaloniaHeadlessXunitV3Runtime.EnsureInitialized();
                    ready.SetResult(null);

                    foreach (var work in Queue.GetConsumingEnumerable())
                    {
                        try
                        {
                            work.Action();
                            work.Completion.SetResult(null);
                        }
                        catch (Exception ex)
                        {
                            work.Completion.SetException(ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ready.SetException(ex);
                }
            })
            {
                IsBackground = true,
                Name = "AvaloniaHeadlessXunitV3UiThread"
            };

            if (OperatingSystem.IsWindows())
            {
                _uiThread.SetApartmentState(ApartmentState.STA);
            }
            _uiThread.Start();
            WaitAndUnwrap(ready.Task);
            Volatile.Write(ref _started, 1);
        }
    }

    public static void Run(Action action)
    {
        EnsureStarted();
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add(new UiWorkItem(action, completion));
        WaitAndUnwrap(completion.Task);
    }

    public static Task RunAsync(Func<Task> action)
    {
        EnsureStarted();
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add(new UiWorkItem(() =>
        {
            var task = action();
            WaitAndUnwrap(task);
        }, completion));
        return completion.Task;
    }

    private static void WaitAndUnwrap(Task task)
    {
        try
        {
            task.Wait();
        }
        catch (AggregateException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }
}

internal static class AvaloniaHeadlessXunitV3Runtime
{
    private static Type? _appBuilderType;
    private static int _initialized;
    private static readonly object Sync = new();

    public static void RegisterAppBuilderType(Type appBuilderType)
    {
        _appBuilderType = appBuilderType;
    }

    public static void EnsureInitialized()
    {
        if (Volatile.Read(ref _initialized) == 1)
            return;

        lock (Sync)
        {
            if (_initialized == 1)
                return;

            var builderType = _appBuilderType ?? ResolveDefaultTestAppBuilderType();

            var buildMethod = builderType.GetMethod("BuildAvaloniaApp",
                BindingFlags.Public | BindingFlags.Static);
            if (buildMethod is null)
                throw new MissingMethodException(builderType.FullName, "BuildAvaloniaApp");

            var builder = buildMethod.Invoke(null, null) as AppBuilder
                          ?? throw new InvalidOperationException(
                              $"BuildAvaloniaApp on '{builderType.FullName}' must return Avalonia.AppBuilder.");

            builder.SetupWithoutStarting();
            Volatile.Write(ref _initialized, 1);
        }
    }

    private static Type ResolveDefaultTestAppBuilderType()
    {
        var fallback = Type.GetType("Agibuild.Fulora.Integration.Tests.Automation.TestAppBuilder, Agibuild.Fulora.Integration.Tests.Automation");
        return fallback ?? throw new InvalidOperationException(
            "Avalonia test app builder is not registered and default TestAppBuilder type could not be resolved.");
    }
}
