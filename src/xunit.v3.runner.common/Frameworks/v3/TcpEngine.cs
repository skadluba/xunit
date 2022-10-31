using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit.Internal;
using Xunit.Sdk;
using Xunit.v3;

namespace Xunit.Runner.v3;

/// <summary>
/// A base class used for TCP engines (specifically, <see cref="TcpRunnerEngine"/> and
/// <see cref="T:Xunit.Runner.v3.TcpExecutionEngine"/>).
/// </summary>
public abstract class TcpEngine : IAsyncDisposable, _IMessageSink
{
	/// <summary>
	/// Gets the operation ID that is used for broadcast messages (messages which are not associated with any specific
	/// operation ID, especially diagnostic/internal diagnostic messages).
	/// </summary>
	public const string BroadcastOperationID = "::BROADCAST::";

	readonly List<(byte[] command, Action<ReadOnlyMemory<byte>?> handler)> commandHandlers = new();
	TcpEngineState state = TcpEngineState.Unknown;

	/// <summary>
	/// Initializes a new instance of the <see cref="TcpEngine"/> class.
	/// </summary>
	/// <param name="engineID">The engine ID (used for diagnostic messages).</param>
	public TcpEngine(
		string engineID)
	{
		EngineID = Guard.ArgumentNotNullOrEmpty(engineID);
		MessagePrefix = $"{GetType().Name}({EngineID}):";
	}

	/// <summary>
	/// Gets the disposal tracker that's automatically cleaned up during <see cref="DisposeAsync"/>.
	/// </summary>
	protected DisposalTracker DisposalTracker { get; } = new();

	/// <summary>
	/// Gets the engine ID.
	/// </summary>
	protected string EngineID { get; }

	/// <summary>
	/// Gets the message prefix to be used when sending diagnostic and internal diagnostic messages.
	/// </summary>
	protected string MessagePrefix { get; }

	/// <summary>
	/// Gets the current state of the engine.
	/// </summary>
	public TcpEngineState State
	{
		get => state;
		protected set
		{
			// TODO: Should we offer an event for state changes?
			SendInternalDiagnosticMessage("{0} [INF] Engine state transition from {1} to {2}", MessagePrefix, state, value);
			state = value;
		}
	}

	/// <summary>
	/// An object which can be used for locks which test and change state.
	/// </summary>
	protected object StateLock { get; } = new();

	/// <summary>
	/// Adds a command handler to the engine.
	/// </summary>
	/// <param name="command">The command (in byte array form) to be handled</param>
	/// <param name="handler">The handler to be called when the command is issued</param>
	protected void AddCommandHandler(byte[] command, Action<ReadOnlyMemory<byte>?> handler) =>
		commandHandlers.Add((command, handler));

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		lock (StateLock)
		{
			if (State == TcpEngineState.Disconnecting || State == TcpEngineState.Disconnected)
				throw new ObjectDisposedException(MessagePrefix);

			State = TcpEngineState.Disconnecting;
		}

		try
		{
			await DisposalTracker.DisposeAsync();
		}
		catch (Exception ex)
		{
			SendInternalDiagnosticMessage("{0} [ERR] Error during disposal: {1}", MessagePrefix, ex);
		}

		lock (StateLock)
			State = TcpEngineState.Disconnected;
	}

	// This allows this type to be used as a diagnostic message sink, which then converts the messages it receives
	// into calls to SendXxx.
	bool _IMessageSink.OnMessage(_MessageSinkMessage message)
	{
		if (message is _DiagnosticMessage diagnosticMessage)
			SendDiagnosticMessage("{0}", diagnosticMessage.Message);
		else if (message is _InternalDiagnosticMessage internalDiagnosticMessage)
			SendInternalDiagnosticMessage("{0}", internalDiagnosticMessage.Message);

		return true;
	}

	/// <summary>
	/// Processes a request provided by the <see cref="BufferedTcpClient"/>. Dispatches to
	/// the appropriate command handler, as registered with <see cref="AddCommandHandler"/>.
	/// </summary>
	/// <param name="request">The received request.</param>
	protected void ProcessRequest(ReadOnlyMemory<byte> request)
	{
		var (command, data) = TcpEngineMessages.SplitOnSeparator(request);

		foreach (var commandHandler in commandHandlers)
			if (command.Span.SequenceEqual(commandHandler.command))
			{
				try
				{
					commandHandler.handler(data);
				}
				catch (Exception ex)
				{
					SendInternalDiagnosticMessage("{0} [ERR] Error during message processing '{1}': {2}", MessagePrefix, Encoding.UTF8.GetString(request.ToArray()), ex);
				}

				return;
			}

		SendInternalDiagnosticMessage("{0} [ERR] Received unknown command '{1}'", MessagePrefix, Encoding.UTF8.GetString(request.ToArray()));
	}

	/// <summary>
	/// Sends a diagnostic message (typically an instance of <see cref="_DiagnosticMessage"/>) to either a local listener and/or
	/// a remote-side engine.
	/// </summary>
	protected abstract void SendDiagnosticMessage(
		string format,
		params object[] args);

	/// <summary>
	/// Sends am internal diagnostic message (typically an instance of <see cref="_InternalDiagnosticMessage"/>) to either a local
	/// listener and/or a remote-side engine.
	/// </summary>
	protected abstract void SendInternalDiagnosticMessage(
		string format,
		params object[] args);
}
