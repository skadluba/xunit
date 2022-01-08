using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using TestDriven.Framework;
using Xunit.Internal;
using Xunit.Runner.Common;
using Xunit.Runner.v3;
using Xunit.Sdk;

namespace Xunit.Runner.TdNet;

// This class does not use XunitFrontController, because the reference to xunit.runner.tdnet comes via
// the NuGet package for the specific version of xUnit.net your tests are written against. So this only
// needs to be written against the current version of xUnit.net.
public class TdNetRunnerHelper : IAsyncDisposable
{
	bool disposed;
	readonly DisposalTracker disposalTracker = new();
	readonly IFrontController? frontController;
	readonly XunitProjectAssembly? projectAssembly;
	readonly ITestListener? testListener;
	readonly object testListenerLock = new();

	/// <summary>
	/// This constructor is for unit testing purposes only.
	/// </summary>
	[Obsolete("For unit testing purposes only")]
	protected TdNetRunnerHelper()
	{ }

	public TdNetRunnerHelper(
		Assembly assembly,
		ITestListener testListener)
	{
		this.testListener = testListener;

		var assemblyFileName = assembly.GetLocalCodeBase();
		var project = new XunitProject();
		projectAssembly = new XunitProjectAssembly(project)
		{
			Assembly = assembly,
			AssemblyFileName = assemblyFileName,
			TargetFramework = AssemblyUtility.GetTargetFramework(assemblyFileName)
		};
		projectAssembly.Configuration.ShadowCopy = false;
		ConfigReader.Load(projectAssembly.Configuration, assemblyFileName);

		var diagnosticMessages = projectAssembly.Configuration.DiagnosticMessagesOrDefault;
		var internalDiagnosticMessages = projectAssembly.Configuration.InternalDiagnosticMessagesOrDefault;
		var assemblyDisplayName = Path.GetFileNameWithoutExtension(assemblyFileName);
		var diagnosticMessageSink = TdNetDiagnosticMessageSink.TryCreate(testListener, testListenerLock, diagnosticMessages, internalDiagnosticMessages, assemblyDisplayName);

		frontController = Xunit3.ForDiscoveryAndExecution(projectAssembly, diagnosticMessageSink: diagnosticMessageSink);
		disposalTracker.Add(frontController);
	}

	public virtual ValueTask DisposeAsync()
	{
		if (disposed)
			throw new ObjectDisposedException(GetType().FullName);

		disposed = true;

		return disposalTracker.DisposeAsync();
	}

	TestRunState FindAndRun(
		TestRunState initialRunState,
		XunitFilters? filters = null)
	{
		Guard.NotNull($"Attempted to use an uninitialized {GetType().FullName} (testListener is null)", testListener);
		Guard.NotNull($"Attempted to use an uninitialized {GetType().FullName} (frontController is null)", frontController);
		Guard.NotNull($"Attempted to use an uninitialized {GetType().FullName} (projectAssembly is null)", projectAssembly);

		// TODO: This does not yet support fail-on-skip or reporting long-running tests. The fact that these are done via
		// delegating implementations of IExecutionSink is a design problem for this runner.

		try
		{
			var resultSink = new ResultSink(testListener, testListenerLock) { TestRunState = initialRunState };
			disposalTracker.Add(resultSink);

			var discoveryOptions = _TestFrameworkOptions.ForDiscovery(projectAssembly.Configuration);
			var executionOptions = _TestFrameworkOptions.ForExecution(projectAssembly.Configuration);
			var settings = new FrontControllerFindAndRunSettings(discoveryOptions, executionOptions, filters);
			frontController.FindAndRun(resultSink, settings);

			resultSink.Finished.WaitOne();

			return resultSink.TestRunState;
		}
		catch (Exception ex)
		{
			lock (testListenerLock)
				testListener.WriteLine("Error during test execution:\r\n" + ex, Category.Error);

			return TestRunState.Error;
		}
	}

	public virtual TestRunState RunAll(
		TestRunState initialRunState = TestRunState.NoTests) =>
			FindAndRun(initialRunState);

	public virtual TestRunState RunClass(
		Type type,
		TestRunState initialRunState = TestRunState.NoTests)
	{
		if (type == null || type.FullName == null)
			return initialRunState;

		var filters = new XunitFilters();
		filters.IncludedClasses.Add(type.FullName);
		return FindAndRun(initialRunState, filters);
	}

	public virtual TestRunState RunMethod(
		MethodInfo method,
		TestRunState initialRunState = TestRunState.NoTests)
	{
		if (method == null)
			return initialRunState;

		var type = method.ReflectedType ?? method.DeclaringType;
		if (type == null || type.FullName == null)
			return initialRunState;

		var filters = new XunitFilters();
		filters.IncludedMethods.Add($"{type.FullName}.{method.Name}");
		return FindAndRun(initialRunState, filters);
	}

	public virtual TestRunState RunNamespace(
		string @namespace,
		TestRunState initialRunState = TestRunState.NoTests)
	{
		if (string.IsNullOrWhiteSpace(@namespace))
			return initialRunState;

		var filters = new XunitFilters();
		filters.IncludedNamespaces.Add(@namespace);
		return FindAndRun(initialRunState, filters);
	}
}
