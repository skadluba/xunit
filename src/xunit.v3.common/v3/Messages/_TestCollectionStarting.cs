using System.Collections.Generic;
using Xunit.Internal;

namespace Xunit.v3;

/// <summary>
/// This message indicates that a test collection is about to start executing.
/// </summary>
public class _TestCollectionStarting : _TestCollectionMessage, _ITestCollectionMetadata
{
	string? testCollectionDisplayName;

	/// <inheritdoc/>
	public string? TestCollectionClass { get; set; }

	/// <inheritdoc/>
	public string TestCollectionDisplayName
	{
		get => this.ValidateNullablePropertyValue(testCollectionDisplayName, nameof(TestCollectionDisplayName));
		set => testCollectionDisplayName = Guard.ArgumentNotNullOrEmpty(value, nameof(TestCollectionDisplayName));
	}

	/// <inheritdoc/>
	public override string ToString() =>
		$"{base.ToString()} name={testCollectionDisplayName.Quoted()} class={TestCollectionClass.Quoted()}";

	/// <inheritdoc/>
	protected override void ValidateObjectState(HashSet<string> invalidProperties)
	{
		base.ValidateObjectState(invalidProperties);

		ValidateNullableProperty(testCollectionDisplayName, nameof(TestCollectionDisplayName), invalidProperties);
	}
}
