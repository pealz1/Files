// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Shared.Helpers;

namespace Files.Shared.UnitTests;

[TestClass]
public sealed class AsyncCoalescingSignalTests
{
	[TestMethod]
	public async Task SetBeforeWaitCompletesImmediately()
	{
		var signal = new AsyncCoalescingSignal();

		signal.Set();

		await signal.WaitAsync();
	}

	[TestMethod]
	public async Task RepeatedSetsCoalesceIntoOnePendingWakeup()
	{
		var signal = new AsyncCoalescingSignal();
		for (var i = 0; i < 10_000; i++)
			signal.Set();

		await signal.WaitAsync();

		using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
		await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
			await signal.WaitAsync(cancellationTokenSource.Token));
	}

	[TestMethod]
	public async Task CanceledWaitDoesNotConsumeNextSignal()
	{
		var signal = new AsyncCoalescingSignal();
		using var cancellationTokenSource = new CancellationTokenSource();
		var canceledWait = signal.WaitAsync(cancellationTokenSource.Token).AsTask();

		cancellationTokenSource.Cancel();
		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => canceledWait);

		signal.Set();
		await signal.WaitAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
	}

	[TestMethod]
	public async Task ResetDrainsPendingWakeup()
	{
		var signal = new AsyncCoalescingSignal();
		signal.Set();
		signal.Reset();

		using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
		await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
			await signal.WaitAsync(cancellationTokenSource.Token));
	}
}
