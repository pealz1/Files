// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Files.Shared.Helpers;

/// <summary>
/// An allocation-light async signal that retains at most one pending notification.
/// </summary>
public sealed class AsyncCoalescingSignal
{
	private readonly Channel<byte> channel = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
	{
		AllowSynchronousContinuations = false,
		FullMode = BoundedChannelFullMode.DropWrite,
		SingleReader = true,
		SingleWriter = false,
	});

	public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
	{
		await channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
	}

	public void Set()
	{
		channel.Writer.TryWrite(0);
	}

	public void Reset()
	{
		while (channel.Reader.TryRead(out _))
		{
		}
	}
}
