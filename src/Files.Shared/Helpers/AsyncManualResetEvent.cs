// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Tasks;

namespace Files.Shared.Helpers;

public sealed class AsyncManualResetEvent
{
	private volatile TaskCompletionSource<bool> m_tcs = CreateTaskCompletionSource();

	public Task WaitAsync(CancellationToken cancellationToken = default)
	{
		return m_tcs.Task.WaitAsync(cancellationToken);
	}

	public async Task<bool> WaitAsync(int milliseconds, CancellationToken cancellationToken = default)
	{
		var signalTask = m_tcs.Task;
		if (signalTask.IsCompleted)
			return await signalTask.ConfigureAwait(false);

		var timeoutTask = Task.Delay(milliseconds, cancellationToken);
		if (await Task.WhenAny(signalTask, timeoutTask).ConfigureAwait(false) == signalTask)
			return await signalTask.ConfigureAwait(false);

		await timeoutTask.ConfigureAwait(false);
		return false;
	}

	public void Set()
	{
		m_tcs.TrySetResult(true);
	}

	public void Reset()
	{
		var newTcs = CreateTaskCompletionSource();
		while (true)
		{
			var tcs = m_tcs;
			if (!tcs.Task.IsCompleted ||
				Interlocked.CompareExchange(ref m_tcs, newTcs, tcs) == tcs)
				return;
		}
	}

	private static TaskCompletionSource<bool> CreateTaskCompletionSource()
	{
		return new(TaskCreationOptions.RunContinuationsAsynchronously);
	}
}
