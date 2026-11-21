namespace MeetingRecorder.TestSupport;

/// <summary>Supplies a cancellable deadline to each xUnit asynchronous test body.</summary>
internal static class TestDeadline
{
    /// <summary>Runs a test with its own deadline and releases the source after completion.</summary>
    internal static Task Run(Func<CancellationToken, Task> test)
    {
        var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return RunAsync(test, deadline, deadline.Token);
    }

    private static async Task RunAsync(Func<CancellationToken, Task> test,
        CancellationTokenSource deadline, CancellationToken cancellationToken)
    {
        using (deadline)
        {
            await test(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
