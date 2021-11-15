using System;
using System.Diagnostics.CodeAnalysis;

namespace BasicModLoader {
	[AttributeUsage( AttributeTargets.Class, Inherited = false )]
	[SuppressMessage( "ReSharper", "ClassNeverInstantiated.Global" )]
	public class LoadStaticConstructorOnLoad : Attribute {
	}
}
