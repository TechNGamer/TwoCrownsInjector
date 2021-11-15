using System;
using System.Diagnostics.CodeAnalysis;

namespace BasicModLoader {
	/// <summary>
	/// Signals to the Mod loader that it should run the static constructor before proceeding to the next assembly.
	/// </summary>
	[AttributeUsage( AttributeTargets.Class, Inherited = false )]
	[SuppressMessage( "ReSharper", "ClassNeverInstantiated.Global" )]
	public class RunStaticConstructorOnLoad : Attribute {
	}
}
