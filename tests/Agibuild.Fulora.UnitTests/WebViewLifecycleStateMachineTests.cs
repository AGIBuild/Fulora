using Agibuild.Fulora;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewLifecycleStateMachineTests
{
    [Fact]
    public void Initial_state_is_created_and_accepts_operations()
    {
        var machine = new WebViewLifecycleStateMachine();

        Assert.Equal(WebViewLifecycleState.Created, machine.CurrentState);
        Assert.Equal("Created", machine.CurrentStateName);
        Assert.False(machine.IsDisposed);
        Assert.False(machine.IsAdapterDestroyed);
        Assert.True(machine.IsOperationAccepted);
    }

    [Fact]
    public void Attaching_state_still_accepts_operations()
    {
        var machine = new WebViewLifecycleStateMachine();

        machine.TransitionToAttaching();

        Assert.Equal(WebViewLifecycleState.Attaching, machine.CurrentState);
        Assert.True(machine.IsOperationAccepted);
    }

    [Fact]
    public void Ready_state_accepts_operations()
    {
        var machine = new WebViewLifecycleStateMachine();

        machine.TransitionToAttaching();
        machine.TransitionToReady();

        Assert.Equal(WebViewLifecycleState.Ready, machine.CurrentState);
        Assert.True(machine.IsOperationAccepted);
    }

    [Fact]
    public void Detaching_state_rejects_new_operations()
    {
        var machine = new WebViewLifecycleStateMachine();

        machine.TransitionToAttaching();
        machine.TransitionToReady();
        machine.TransitionToDetaching();

        Assert.Equal(WebViewLifecycleState.Detaching, machine.CurrentState);
        Assert.Equal("Detaching", machine.CurrentStateName);
        Assert.False(machine.IsOperationAccepted);
    }

    [Fact]
    public void Disposed_state_rejects_new_operations_and_throws_on_guard()
    {
        var machine = new WebViewLifecycleStateMachine();

        Assert.True(machine.TryTransitionToDisposed());

        Assert.True(machine.IsDisposed);
        Assert.Equal(WebViewLifecycleState.Disposed, machine.CurrentState);
        Assert.False(machine.IsOperationAccepted);
        Assert.Throws<ObjectDisposedException>(() => machine.ThrowIfDisposed());
    }

    [Fact]
    public void TryTransitionToDisposed_is_idempotent()
    {
        var machine = new WebViewLifecycleStateMachine();

        Assert.True(machine.TryTransitionToDisposed());
        Assert.False(machine.TryTransitionToDisposed());
        Assert.False(machine.TryTransitionToDisposed());
    }

    [Fact]
    public void ThrowIfDisposed_is_noop_when_alive()
    {
        var machine = new WebViewLifecycleStateMachine();

        machine.ThrowIfDisposed();
        machine.TransitionToAttaching();
        machine.ThrowIfDisposed();
        machine.TransitionToReady();
        machine.ThrowIfDisposed();
    }

    [Fact]
    public void MarkAdapterDestroyedOnce_invokes_raise_exactly_once()
    {
        var machine = new WebViewLifecycleStateMachine();
        var raiseCount = 0;

        machine.MarkAdapterDestroyedOnce(() => raiseCount++);
        machine.MarkAdapterDestroyedOnce(() => raiseCount++);
        machine.MarkAdapterDestroyedOnce(() => raiseCount++);

        Assert.Equal(1, raiseCount);
        Assert.True(machine.IsAdapterDestroyed);
    }

    [Fact]
    public void MarkAdapterDestroyedOnce_null_raise_throws()
    {
        var machine = new WebViewLifecycleStateMachine();

        Assert.Throws<ArgumentNullException>(() => machine.MarkAdapterDestroyedOnce(null!));
    }

    [Fact]
    public void MarkAdapterDestroyedOnce_does_not_affect_disposal_flag()
    {
        var machine = new WebViewLifecycleStateMachine();

        machine.MarkAdapterDestroyedOnce(() => { });

        Assert.True(machine.IsAdapterDestroyed);
        Assert.False(machine.IsDisposed);
        Assert.True(machine.IsOperationAccepted);
    }

    [Fact]
    public void Transitions_produce_expected_state_names_for_diagnostics()
    {
        var machine = new WebViewLifecycleStateMachine();

        Assert.Equal("Created", machine.CurrentStateName);

        machine.TransitionToAttaching();
        Assert.Equal("Attaching", machine.CurrentStateName);

        machine.TransitionToReady();
        Assert.Equal("Ready", machine.CurrentStateName);

        machine.TransitionToDetaching();
        Assert.Equal("Detaching", machine.CurrentStateName);

        machine.TryTransitionToDisposed();
        Assert.Equal("Disposed", machine.CurrentStateName);
    }

    [Fact]
    public void Full_attach_detach_dispose_sequence_tracks_expected_flags()
    {
        var machine = new WebViewLifecycleStateMachine();
        var raiseCount = 0;

        machine.TransitionToAttaching();
        machine.TransitionToReady();
        Assert.True(machine.IsOperationAccepted);

        machine.TransitionToDetaching();
        machine.MarkAdapterDestroyedOnce(() => raiseCount++);
        Assert.Equal(1, raiseCount);
        Assert.True(machine.IsAdapterDestroyed);
        Assert.False(machine.IsOperationAccepted);

        Assert.True(machine.TryTransitionToDisposed());
        machine.MarkAdapterDestroyedOnce(() => raiseCount++);

        Assert.Equal(1, raiseCount);
        Assert.True(machine.IsDisposed);
    }
}
