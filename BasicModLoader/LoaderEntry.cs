using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BasicModLoader {
	[SuppressMessage( "ReSharper", "MemberCanBePrivate.Global", Justification = "Some mods might need this information.")]
	public static class LoaderEntry {
		public static bool Loaded { get; private set; }

		public static string ModFolder {
			get {
				string folder;

				// Everything else is handled via the `default` tag.
				// ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
				switch ( Application.platform ) {
					case RuntimePlatform.OSXPlayer:
						// The reason the mod folder is different for macOS is that it is an App Bundle, and I don't have a mac, so inside the bundle it goes.
						folder = Path.Combine( Application.dataPath, "Mods" );
						break;
					case RuntimePlatform.OSXEditor:
					case RuntimePlatform.LinuxEditor:
					case RuntimePlatform.WindowsEditor:
						// Would be interesting for this to be running inside an editor, but since that shouldn't happen, an error will arise.
						throw new PlatformNotSupportedException( "How/Why are you using this on an editor?" );
					case RuntimePlatform.WindowsPlayer:
					case RuntimePlatform.LinuxPlayer:
						// Since it will be in the Data folder, we go up one and then into the mods folder.
						folder = Path.Combine( Application.dataPath, "..", "Mods" );
						break;
					default:
						// This loader doesn't support other platforms.
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
			// It is has ran once, doesn't need to run again.
			if ( Loaded ) {
				return;
			}
			
			// Loops through all the mod folders so it can load them.
			foreach ( var modFolders in Directory.EnumerateDirectories( ModFolder, "*", SearchOption.TopDirectoryOnly ) ) {
				LoadAssemblies( modFolders );
			}

			// The flag for being loaded will be set to true, as to prevent this method from running this again.
			Loaded = true;
		}

		private static void LoadAssemblies( string lookFolder = "" ) {
			var asmFolder = Path.Combine( lookFolder, "Assemblies" );

			Debug.Log( "Verifying that the mod folder contains assemblies." );

			if ( !Directory.Exists( asmFolder ) ) {
				return;
			}

			// This section loads every assembly that is in the Assemblies folder.
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

		// This section will look through each Assembly and run every static constructor that has the `RunStaticConstructorOnLoad`
		private static void CheckAndRun( Assembly asm ) {
			// This is the fun part where Reflection is going to be used to locate all the classes that have the custom attribute `RunStaticConstructorOnLoad` and run their static constructor.
			foreach ( var type in asm.GetTypes() ) {
				if ( type.GetCustomAttributes( false ).FirstOrDefault( a => a is RunStaticConstructorOnLoad ) == null ) {
					continue;
				}

				RuntimeHelpers.RunClassConstructor( type.TypeHandle );
			}
		}
	}
}
