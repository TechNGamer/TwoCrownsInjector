using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BasicModLoader {
	public static class LoaderEntry {
		public static bool Loaded { get; private set; }

		public static string ModFolder {
			get {
				string folder;

				// Everything else is handled via the `default` tag.
				// ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
				switch ( Application.platform ) {
					case RuntimePlatform.OSXPlayer:
						folder = Path.Combine( Application.dataPath, "Mods" );
						break;
					case RuntimePlatform.OSXEditor:
					case RuntimePlatform.LinuxEditor:
					case RuntimePlatform.WindowsEditor:
						throw new PlatformNotSupportedException( "How/Why are you using this on an editor?" );
					case RuntimePlatform.WindowsPlayer:
					case RuntimePlatform.LinuxPlayer:
						folder = Path.Combine( Application.dataPath, "..", "Mods" );
						break;
					default:
						throw new PlatformNotSupportedException();
				}

				// Just checking to make sure the folder exists, and if it doesn't, to go ahead and create that folder.
				if ( !Directory.Exists( folder ) ) {
					_ = Directory.CreateDirectory( folder );
				}

				return folder;
			}
		}

		[SuppressMessage( "ReSharper", "UnusedMember.Global", Justification = "This is the entry point for loading mods." )]
		public static void BeginModLoading() {
			foreach ( var modFolders in Directory.EnumerateDirectories( ModFolder, "*", SearchOption.TopDirectoryOnly ) ) {
				LoadAssemblies( modFolders );
			}

			Loaded = true;
		}

		private static void LoadAssemblies( string lookFolder = "" ) {
			var asmFolder = Path.Combine( lookFolder, "Assemblies" );

			Debug.Log( "Verifying that the mod folder contains assemblies." );

			if ( !Directory.Exists( asmFolder ) ) {
				return;
			}

			foreach ( var assemblyFile in Directory.EnumerateFiles( asmFolder, "*.dll", SearchOption.TopDirectoryOnly ) ) {
				var assembly = Assembly.LoadFile( assemblyFile );

				Debug.Log( $"Looking at assembly `{assembly.FullName}`." );

				try {
					CheckAndRun( assembly );
				} catch ( Exception e ) {
					Debug.Log( "Failed to run the static constructor(s) inside the assembly, aborting further searches. Please report the exception below to the author of the mod." );
					Debug.LogException( e );
				}
			}

			Debug.Log( $"All done loading Assemblies in `{asmFolder}`." );
		}

		private static void CheckAndRun( Assembly asm ) {
			// This is the fun part where Reflection is going to be used to locate all the classes that have the custom attribute `LoadStaticConstructorOnLoad`.
			foreach ( var type in asm.GetTypes() ) {
				if ( type.GetCustomAttributes( false ).FirstOrDefault( a => a is LoadStaticConstructorOnLoad ) == null ) {
					continue;
				}

				RuntimeHelpers.RunClassConstructor( type.TypeHandle );
			}
		}
	}
}
